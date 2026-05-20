using System.Collections.Concurrent;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端警報監控服務 — 接收即時資料批次，比對 AlarmRules 規則，直接寫入 EventLog
/// 設計為 Singleton，由 Worker.OnModbusDataCollected 事件驅動呼叫
/// </summary>
public class AlarmMonitorService
{
    private readonly ILogger<AlarmMonitorService> _logger;
    private readonly AlarmEventLogRepository _repository;
    private readonly LineNotificationService _lineService;
    private readonly AlarmMqttPublisher _mqttPublisher;
    private readonly IDataRepository _dataRepository;

    /// <summary>快取所有啟用的規則（key = SID）</summary>
    private readonly ConcurrentDictionary<string, AlarmRuleModel> _rules = new();

    /// <summary>追蹤每個 SID:type 目前的警報狀態</summary>
    private readonly ConcurrentDictionary<string, AlarmState> _alarmStates = new();

    /// <summary>規則重新載入計時器</summary>
    private readonly Timer _reloadTimer;

    /// <summary>序列化 ReloadAndReevaluateAsync，避免短時間連續觸發造成 _rules / _alarmStates 半更新狀態</summary>
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    /// <summary>是否已完成初始化</summary>
    private bool _isInitialized = false;

    public AlarmMonitorService(
        ILogger<AlarmMonitorService> logger,
        AlarmEventLogRepository repository,
        LineNotificationService lineService,
        AlarmMqttPublisher mqttPublisher,
        IDataRepository dataRepository)
    {
        _logger = logger;
        _repository = repository;
        _lineService = lineService;
        _mqttPublisher = mqttPublisher;
        _dataRepository = dataRepository;

        // 計時器先不啟動，等 InitializeAsync 完成後再開
        _reloadTimer = new Timer(async _ => await ReloadRulesAsync(),
            null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 初始化：載入規則 + 還原活躍警報狀態
    /// 應在 Engine 啟動後、開始接收資料前呼叫
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("警報監控服務初始化中...");

        // 啟動時清除所有未恢復的 Fault 事件（EventType=1）
        // 系統型故障靠記憶體計數器追蹤，Engine 重啟後狀態歸零；若故障仍存在會由執行邏輯重新累積觸發
        await _repository.ClearAllUnresolvedFaultEventsAsync();

        await ReloadRulesAsync();
        await InitAlarmStatesFromDbAsync();

        // 立即清掃一次孤立警報（規則已刪除/停用但 EventLog 仍未恢復的事件）
        await CleanupOrphanAlarmsAsync();

        // 啟動定期重載計時器（60 秒間隔）
        _reloadTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        _isInitialized = true;
        _logger.LogInformation("警報監控服務初始化完成");

        // Engine 重啟後，republish 所有目前 active 警報（覆蓋 broker 殘留 retained，
        // 並讓重啟後的 Web 訂閱者立即收到當前實際狀態）
        await RepublishActiveAlarmsAsync();
    }

    /// <summary>
    /// 對 DB 中所有未恢復警報重新發布 MQTT retained message，覆蓋 broker 上可能殘留的舊訊息
    /// </summary>
    private async Task RepublishActiveAlarmsAsync()
    {
        try
        {
            var activeAlarms = await _repository.GetActiveAlarmsAsync();
            int nCount = 0;
            foreach (var alarm in activeAlarms)
            {
                await _mqttPublisher.PublishAlarmActiveAsync(alarm);
                nCount++;
            }
            _logger.LogInformation("Engine 啟動後 republish {Count} 筆 active 警報至 MQTT", nCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Republish active 警報失敗");
        }
    }

    /// <summary>
    /// 批次評估即時資料 — 由 Worker.OnModbusDataCollected 呼叫
    /// </summary>
    public async Task EvaluateBatchAsync(List<RealtimeDataModel> realtimeDataList)
    {
        if (!_isInitialized)
            return;

        foreach (var data in realtimeDataList)
        {
            try
            {
                if (!_rules.TryGetValue(data.szSID, out var rule))
                    continue;

                // 品質檢查 — Engine 端品質值為 "Good"，使用不區分大小寫比對
                if (!string.Equals(data.szQuality, "Good", StringComparison.OrdinalIgnoreCase))
                    continue;

                await EvaluateAlarmAsync(data, rule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "警報檢查失敗: SID={SID}", data.szSID);
            }
        }
    }

    /// <summary>
    /// 由 Web 端「警報規則異動」MQTT 通知觸發：重載規則 + 對 LatestData 重新評估，
    /// 讓使用者改完規則後 ~1 秒內即可看到觸發 / 恢復事件、Active 警報面板同步更新。
    /// 使用 SemaphoreSlim 序列化，避免短時間連續儲存時兩個 reload 並行污染快取。
    /// </summary>
    public async Task ReloadAndReevaluateAsync()
    {
        // 尚未初始化（與 InitializeAsync 競態）→ 直接 return；Engine 啟動時本來就會做完整 reload
        if (!_isInitialized)
        {
            _logger.LogDebug("收到規則 reload 通知但服務尚未初始化完成，跳過");
            return;
        }

        await _reloadGate.WaitAsync();
        try
        {
            // 1. 重載規則（內部已包含啟用後的孤立警報清掃）
            await ReloadRulesAsync();

            // 2. 從 LatestData 取所有最新值（含計算點，因為 CalculatedPointService 也寫入 LatestData）
            //    nLimit 給足夠大的數字以涵蓋全部點位
            var latestList = await _dataRepository.GetLatestDataAsync(int.MaxValue);
            var realtimeList = latestList.Select(latest => new RealtimeDataModel
            {
                szSID = latest.szSID,
                fValue = latest.fValue,
                szQuality = latest.nQuality == 1 ? "Good" : "Bad",
                dtTimestamp = latest.dtTimestamp,
                szTagName = string.Empty
            }).ToList();

            // 3. 重新評估
            await EvaluateBatchAsync(realtimeList);

            _logger.LogInformation("規則異動觸發即時重評，rules={RuleCount}, points={PointCount}",
                _rules.Count, realtimeList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReloadAndReevaluateAsync 失敗");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    // ── 私有方法 ──

    private async Task ReloadRulesAsync()
    {
        try
        {
            var rules = await _repository.GetEnabledRulesAsync();

            _rules.Clear();
            foreach (var rule in rules)
            {
                _rules[rule.szSID] = rule;
            }

            _logger.LogDebug("已載入 {Count} 條警報規則", _rules.Count);

            // 規則異動後清掃孤立警報（規則已刪除/停用，但 EventLog 仍標示警報中）
            // 初始化階段 _alarmStates 尚未還原，跳過；由 InitializeAsync 末段補一次清掃
            if (_isInitialized)
                await CleanupOrphanAlarmsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入警報規則失敗");
        }
    }

    /// <summary>
    /// 清掃孤立警報：規則已刪除 / 整條停用 / 特定類型旗標關閉 / 門檻欄位已被清空，
    /// 但 EventLog 仍有 ClearedAt = NULL 的事件 → 視為孤立，自動恢復避免永遠卡在警報中。
    /// </summary>
    private async Task CleanupOrphanAlarmsAsync()
    {
        foreach (var kvp in _alarmStates.ToArray())
        {
            try
            {
                if (!kvp.Value.isActive) continue;

                var szKey = kvp.Key;
                var nColon = szKey.LastIndexOf(':');
                if (nColon < 0) continue;

                var szSID = szKey.Substring(0, nColon);
                var szType = szKey.Substring(nColon + 1);

                bool isOrphan;
                byte nOperator;
                if (!_rules.TryGetValue(szSID, out var rule))
                {
                    isOrphan = true;
                    nOperator = szType switch { "high" => 2, "low" => 3, "di" => 4, _ => 0 };
                }
                else
                {
                    (isOrphan, nOperator) = szType switch
                    {
                        "high" => (!rule.isAlarmHigh || !rule.dAlarmHighValue.HasValue, (byte)2),
                        "low"  => (!rule.isAlarmLow  || !rule.dAlarmLowValue.HasValue,  (byte)3),
                        "di"   => (!rule.isDiAlarm   || string.IsNullOrEmpty(rule.szDiTriggerState), (byte)4),
                        _      => (false, (byte)0)
                    };
                }

                if (!isOrphan || nOperator == 0) continue;

                await _repository.ClearEventByOperatorAsync(szSID, nOperator);

                _alarmStates[szKey] = new AlarmState
                {
                    isActive = false,
                    szType = null,
                    dtLastTriggered = kvp.Value.dtLastTriggered
                };

                // 同步清除 MQTT retained 訊息
                try
                {
                    await _mqttPublisher.PublishAlarmClearedAsync(szSID, nOperator);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清除孤立警報的 MQTT retained 訊息失敗: SID={SID}", szSID);
                }

                _logger.LogInformation("規則已移除/停用，自動清除孤立警報: SID={SID} [{Type}]", szSID, szType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清掃孤立警報失敗: Key={Key}", kvp.Key);
            }
        }
    }

    private async Task InitAlarmStatesFromDbAsync()
    {
        try
        {
            var activeAlarms = await _repository.GetActiveAlarmsAsync();

            foreach (var alarm in activeAlarms)
            {
                string szType = alarm.nOperator switch
                {
                    2 => "high",
                    3 => "low",
                    4 => "di",
                    _ => "high"
                };

                string szKey = $"{alarm.szSID}:{szType}";
                _alarmStates[szKey] = new AlarmState
                {
                    isActive = true,
                    szType = szType,
                    dtLastTriggered = alarm.dtOccurredAt
                };
            }

            _logger.LogInformation("從 EventLog 還原 {Count} 筆活躍警報狀態", _alarmStates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "從 EventLog 還原警報狀態失敗");
        }
    }

    private async Task EvaluateAlarmAsync(RealtimeDataModel data, AlarmRuleModel rule)
    {
        try
        {
            // fValue 是 float，threshold 是 double? — 統一轉為 double
            double dVal = (double)data.fValue;
            string szSID = data.szSID;
            string szName = data.szTagName ?? szSID;

            // ── 上限警報 ──
            if (rule.isAlarmHigh && rule.dAlarmHighValue.HasValue)
            {
                double dThreshold = rule.dAlarmHighValue.Value;
                double dDeadband = rule.dDeadbandHigh ?? 0;
                bool isTriggered = dVal >= (dThreshold - dDeadband);

                var args = BuildArgsJson(("name", szName), ("threshold", dThreshold.ToString()));
                await CheckTransitionAsync(szSID, "high", isTriggered,
                    dVal, dThreshold, 2, rule.nAlarmHighSeverity,
                    $"{szName} 超過上限 {dThreshold}",
                    "alarm.high_exceed", args);
            }

            // ── 下限警報 ──
            if (rule.isAlarmLow && rule.dAlarmLowValue.HasValue)
            {
                double dThreshold = rule.dAlarmLowValue.Value;
                double dDeadband = rule.dDeadbandLow ?? 0;
                bool isTriggered = dVal <= (dThreshold + dDeadband);

                var args = BuildArgsJson(("name", szName), ("threshold", dThreshold.ToString()));
                await CheckTransitionAsync(szSID, "low", isTriggered,
                    dVal, dThreshold, 3, rule.nAlarmLowSeverity,
                    $"{szName} 低於下限 {dThreshold}",
                    "alarm.low_below", args);
            }

            // ── DI 警報 ──
            if (rule.isDiAlarm && !string.IsNullOrEmpty(rule.szDiTriggerState))
            {
                bool isOn = Math.Abs(dVal - 1.0) < 0.01;
                bool isTriggered = (rule.szDiTriggerState == "ON" && isOn)
                                || (rule.szDiTriggerState == "OFF" && !isOn);

                string szStateLabel = isOn
                    ? (rule.szDiOnLabel ?? "ON")
                    : (rule.szDiOffLabel ?? "OFF");

                // szStateLabel 是使用者自填中文（如「啟動/停機」），視為 user input 直接帶入英文模板，不額外翻譯
                var args = BuildArgsJson(("name", szName), ("state", szStateLabel));
                await CheckTransitionAsync(szSID, "di", isTriggered,
                    dVal, isOn ? 1 : 0, 4, rule.nDiAlarmSeverity,
                    $"{szName} 狀態為 {szStateLabel} 觸發警報",
                    "alarm.di_triggered", args);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "評估警報失敗: SID={SID}", data.szSID);
        }
    }

    /// <summary>
    /// 把 (key, value) 對組成最小 JSON，避免引入 System.Text.Json 處理小型 dict 開銷。
    /// 簡單字串 escape：替換 backslash / 雙引號。
    /// </summary>
    private static string BuildArgsJson(params (string key, string value)[] args)
    {
        var sb = new System.Text.StringBuilder("{");
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(args[i].key).Append("\":\"")
              .Append((args[i].value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""))
              .Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private async Task CheckTransitionAsync(
        string szSID, string szType, bool isTriggered,
        double dTriggerValue, double dThresholdValue, byte nOperator,
        byte nSeverity, string szMessage,
        string szMessageKey, string szMessageArgsJson)
    {
        string szKey = $"{szSID}:{szType}";
        _alarmStates.TryGetValue(szKey, out var prevState);
        bool wasActive = prevState?.isActive ?? false;

        if (isTriggered && !wasActive)
        {
            // 正常 → 警報
            _alarmStates[szKey] = new AlarmState
            {
                isActive = true,
                szType = szType,
                dtLastTriggered = DateTime.Now
            };

            _logger.LogWarning("警報觸發: {SID} [{Type}] {Message}, 值={Value}",
                szSID, szType, szMessage, dTriggerValue);

            var dtNow = DateTime.Now;
            var eventModel = new EventLogModel
            {
                szSID = szSID,
                nEventType = 0,  // Alarm
                nSeverity = nSeverity,
                dTriggerValue = dTriggerValue,
                dThresholdValue = dThresholdValue,
                nOperator = nOperator,
                szMessage = szMessage,
                szMessageKey = szMessageKey,
                szMessageArgs = szMessageArgsJson,
                dtOccurredAt = dtNow
            };
            await _repository.InsertEventAsync(eventModel);

            // 發布 MQTT 警報觸發訊息（retained）
            try
            {
                await _mqttPublisher.PublishAlarmActiveAsync(eventModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發布警報觸發 MQTT 訊息失敗（不影響警報流程）: SID={SID}", szSID);
            }

            // Line 通知（失敗不影響警報流程；服務內部會自行篩選符合嚴重度上限的群組）
            try
            {
                await _lineService.NotifyAsync(nSeverity, szSID, szMessage, dtNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Line 通知派送失敗但警報流程繼續: SID={SID}", szSID);
            }
        }
        else if (!isTriggered && wasActive)
        {
            // 警報 → 正常
            _alarmStates[szKey] = new AlarmState
            {
                isActive = false,
                szType = null,
                dtLastTriggered = prevState!.dtLastTriggered
            };

            _logger.LogInformation("警報恢復: {SID} [{Type}]", szSID, szType);

            await _repository.ClearEventAsync(szSID);

            // 發布 MQTT 恢復訊息（空 payload 清除 retained）
            try
            {
                await _mqttPublisher.PublishAlarmClearedAsync(szSID, nOperator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發布警報恢復 MQTT 訊息失敗（不影響警報流程）: SID={SID}", szSID);
            }
        }
    }

    // ── 內部狀態追蹤 ──

    private class AlarmState
    {
        public bool isActive { get; set; }
        public string? szType { get; set; }
        public DateTime dtLastTriggered { get; set; }
    }
}
