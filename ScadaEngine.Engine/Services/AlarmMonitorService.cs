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
    private readonly EmailNotificationService _emailService;
    private readonly SmsNotificationService _smsService;
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

    /// <summary>
    /// 全點位最新值快取（key = SID）— 前置條件判斷 X 的值、以及 1 秒 tick 補判本點時使用。
    /// DB / OPC UA 來源只推變動值，不能靠「被推資料才評估」完成延遲判定。
    /// </summary>
    private readonly ConcurrentDictionary<string, LatestSample> _latestValues = new();

    /// <summary>前置條件閘門（key = 規則 SID）；前置設定未變的規則在 reload 時沿用，避免計時被打斷</summary>
    private readonly ConcurrentDictionary<string, GateEntry> _gates = new();

    /// <summary>本點 on-delay 計時器（key = SID:type）</summary>
    private readonly ConcurrentDictionary<string, OnDelayTimer> _onDelays = new();

    /// <summary>同一 SID 的評估序列化（資料批次與 tick 可能同時評估同一規則，避免重複觸發）</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sidLocks = new();

    /// <summary>1 秒 tick：只掃有前置條件或 AlarmDelaySec &gt; 0 的規則</summary>
    private readonly Timer _tickTimer;
    private int _nTickRunning = 0;

    public AlarmMonitorService(
        ILogger<AlarmMonitorService> logger,
        AlarmEventLogRepository repository,
        LineNotificationService lineService,
        EmailNotificationService emailService,
        SmsNotificationService smsService,
        AlarmMqttPublisher mqttPublisher,
        IDataRepository dataRepository)
    {
        _logger = logger;
        _repository = repository;
        _lineService = lineService;
        _emailService = emailService;
        _smsService = smsService;
        _mqttPublisher = mqttPublisher;
        _dataRepository = dataRepository;

        // 計時器先不啟動，等 InitializeAsync 完成後再開
        _reloadTimer = new Timer(async _ => await ReloadRulesAsync(),
            null, Timeout.Infinite, Timeout.Infinite);
        _tickTimer = new Timer(async _ => await TickAsync(),
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

        // 預填最新值快取：避免前置點位（變動才推的 DB/OPC UA 點）在第一次變動前被視為「無資料 → 閘門關閉」
        await PrefillLatestValuesAsync();

        // 立即清掃一次孤立警報（規則已刪除/停用但 EventLog 仍未恢復的事件）
        await CleanupOrphanAlarmsAsync();

        // 啟動定期重載計時器（60 秒間隔）
        _reloadTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        _isInitialized = true;

        // 啟動延遲 / 前置條件 tick（1 秒）
        _tickTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
                // 品質檢查 — Engine 端品質值為 "Good"，使用不區分大小寫比對
                bool isGood = string.Equals(data.szQuality, "Good", StringComparison.OrdinalIgnoreCase);

                // 不論是否有規則都記最新值（可能被其他規則引用為前置條件）
                var sample = UpdateLatestValue(data, isGood);

                if (!_rules.TryGetValue(data.szSID, out var rule))
                    continue;

                if (!isGood)
                    continue;

                await EvaluateRuleAsync(rule, sample, DateTime.Now);
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

            SyncGatesAndTimers();

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

    /// <summary>
    /// 單一規則評估：前置條件閘門 → 本點高/低/DI 越限 → on-delay → 狀態轉換。
    /// sample 為本點最新值（tick 補判時可能為 null = 尚無資料）。
    /// </summary>
    private async Task EvaluateRuleAsync(AlarmRuleModel rule, LatestSample? sample, DateTime dtNow)
    {
        var sidLock = _sidLocks.GetOrAdd(rule.szSID, _ => new SemaphoreSlim(1, 1));
        await sidLock.WaitAsync();
        try
        {
            // ── 前置條件閘門 ──
            var gateState = PreconditionGateState.Open;
            if (_gates.TryGetValue(rule.szSID, out var gateEntry))
            {
                _latestValues.TryGetValue(gateEntry.szPreSID, out var preSample);
                gateState = gateEntry.gate.Update(preSample?.dValue, preSample?.isGood ?? false, dtNow);
            }

            string szName = ResolveName(rule.szSID, sample);

            if (gateState == PreconditionGateState.Closed)
            {
                ResetOnDelays(rule.szSID);
                await ClearByPreconditionAsync(rule, sample, szName);
                return;
            }

            if (sample == null || !sample.isGood)
            {
                if (gateState != PreconditionGateState.Open)
                    ResetOnDelays(rule.szSID);
                return;
            }

            foreach (var check in BuildChecks(rule, sample.dValue, szName))
            {
                string szKey = $"{rule.szSID}:{check.szType}";
                bool wasActive = _alarmStates.TryGetValue(szKey, out var st) && st.isActive;
                var timer = _onDelays.GetOrAdd(szKey, _ => new OnDelayTimer());

                bool isEffective;
                if (gateState != PreconditionGateState.Open)
                {
                    // 閘門 Pending：不觸發新警報、on-delay 保持歸零；已 active 者仍可自行恢復
                    timer.Reset();
                    isEffective = check.isTriggered && wasActive;
                }
                else
                {
                    bool isDelaySatisfied = timer.Update(check.isTriggered, dtNow, rule.nAlarmDelaySec);
                    // 已 active 者不受 on-delay 影響（恢復立即、持續越限維持 active）
                    isEffective = check.isTriggered && (wasActive || isDelaySatisfied);
                }

                await CheckTransitionAsync(rule.szSID, szName, rule.nId, check, isEffective);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "評估警報失敗: SID={SID}", rule.szSID);
        }
        finally
        {
            sidLock.Release();
        }
    }

    /// <summary>依規則與本點值組出高 / 低 / DI 三段判定（僅含啟用且設定完整的段）</summary>
    private static List<AlarmCheck> BuildChecks(AlarmRuleModel rule, double dVal, string szName)
    {
        var checks = new List<AlarmCheck>(3);

        // ── 上限警報 ──
        if (rule.isAlarmHigh && rule.dAlarmHighValue.HasValue)
        {
            double dThreshold = rule.dAlarmHighValue.Value;
            double dDeadband = rule.dDeadbandHigh ?? 0;
            checks.Add(new AlarmCheck("high", 2, dVal >= (dThreshold - dDeadband),
                dVal, dThreshold, rule.nAlarmHighSeverity,
                $"{szName} 超過上限 {dThreshold}", "alarm.high_exceed",
                new Dictionary<string, string?> { ["name"] = szName, ["threshold"] = dThreshold.ToString() }));
        }

        // ── 下限警報 ──
        if (rule.isAlarmLow && rule.dAlarmLowValue.HasValue)
        {
            double dThreshold = rule.dAlarmLowValue.Value;
            double dDeadband = rule.dDeadbandLow ?? 0;
            checks.Add(new AlarmCheck("low", 3, dVal <= (dThreshold + dDeadband),
                dVal, dThreshold, rule.nAlarmLowSeverity,
                $"{szName} 低於下限 {dThreshold}", "alarm.low_below",
                new Dictionary<string, string?> { ["name"] = szName, ["threshold"] = dThreshold.ToString() }));
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
            checks.Add(new AlarmCheck("di", 4, isTriggered,
                dVal, isOn ? 1 : 0, rule.nDiAlarmSeverity,
                $"{szName} 狀態為 {szStateLabel} 觸發警報", "alarm.di_triggered",
                new Dictionary<string, string?> { ["name"] = szName, ["state"] = szStateLabel }));
        }

        return checks;
    }

    /// <summary>
    /// 前置條件不成立 → 清除本點所有 active 警報（EventLog ClearedAt + MQTT clear），
    /// 恢復通知與一般恢復相同，依規則 RecoveryNotifyLine / Email / Sms 決定。
    /// </summary>
    private async Task ClearByPreconditionAsync(AlarmRuleModel rule, LatestSample? sample, string szName)
    {
        foreach (var check in BuildChecks(rule, sample?.dValue ?? 0, szName))
        {
            string szKey = $"{rule.szSID}:{check.szType}";
            if (!_alarmStates.TryGetValue(szKey, out var prevState) || !prevState.isActive)
                continue;

            _alarmStates[szKey] = new AlarmState
            {
                isActive = false,
                szType = null,
                dtLastTriggered = prevState.dtLastTriggered
            };

            _logger.LogInformation("前置條件不成立，清除警報: {SID} [{Type}]（前置點位 {PreSID}）",
                rule.szSID, check.szType, rule.szPreSID);

            await _repository.ClearEventByOperatorAsync(rule.szSID, check.nOperator);

            try { await _mqttPublisher.PublishAlarmClearedAsync(rule.szSID, check.nOperator); }
            catch (Exception ex) { _logger.LogError(ex, "發布前置清除 MQTT 訊息失敗: SID={SID}", rule.szSID); }

            await NotifyRecoveryAsync(rule, BuildContext(rule.szSID, szName, rule.nId, check, 0));
        }
    }

    /// <summary>
    /// 恢復通知：依規則 RecoveryNotifyLine / Email / Sms 分別決定是否派送（一般恢復與前置清除共用）。
    /// 簡訊服務內部另檢查 SmsSetting.SendRecovery（全域開關），兩者皆開才發。
    /// </summary>
    private async Task NotifyRecoveryAsync(AlarmRuleModel? rule, NotifyContext ctx)
    {
        // 規則已不在快取（理論上不會發生於恢復當下）→ 沿用舊行為三通道都送
        var plan = rule != null ? RecoveryNotifyPlan.From(rule) : new RecoveryNotifyPlan(true, true, true);
        if (plan.isLine)
        {
            try { await _lineService.NotifyClearedAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "Line 恢復通知派送失敗: SID={SID}", ctx.szSID); }
        }
        if (plan.isEmail)
        {
            try { await _emailService.NotifyClearedAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "Email 恢復通知派送失敗: SID={SID}", ctx.szSID); }
        }
        if (plan.isSms)
        {
            try { await _smsService.NotifyClearedAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "簡訊恢復通知派送失敗: SID={SID}", ctx.szSID); }
        }
    }

    /// <summary>1 秒 tick：以快取最新值補判有前置條件 / on-delay 的規則（延遲期滿但值沒變也能觸發）</summary>
    private async Task TickAsync()
    {
        if (!_isInitialized)
            return;
        if (Interlocked.Exchange(ref _nTickRunning, 1) == 1)
            return;
        try
        {
            var dtNow = DateTime.Now;
            foreach (var rule in _rules.Values)
            {
                if (!NeedsTick(rule))
                    continue;
                _latestValues.TryGetValue(rule.szSID, out var sample);
                await EvaluateRuleAsync(rule, sample, dtNow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "警報延遲 tick 失敗");
        }
        finally
        {
            Interlocked.Exchange(ref _nTickRunning, 0);
        }
    }

    private static bool HasPrecondition(AlarmRuleModel rule)
        => rule.isPrecondition
           && !string.IsNullOrWhiteSpace(rule.szPreSID)
           && AlarmPreconditionGate.ValidOperators.Contains((rule.szPreOperator ?? string.Empty).Trim().ToUpperInvariant());

    private static bool NeedsTick(AlarmRuleModel rule)
        => HasPrecondition(rule) || rule.nAlarmDelaySec > 0;

    /// <summary>
    /// 規則載入後同步閘門與 on-delay 計時器：前置設定未變者沿用（計時不中斷），
    /// 變更者重建（延遲從重載後重新起算），已無前置條件 / 已刪除規則者移除。
    /// </summary>
    private void SyncGatesAndTimers()
    {
        foreach (var rule in _rules.Values)
        {
            if (!HasPrecondition(rule))
            {
                _gates.TryRemove(rule.szSID, out _);
                continue;
            }

            string szPreSID = rule.szPreSID!.Trim();
            string szOp = rule.szPreOperator!.Trim().ToUpperInvariant();
            if (_gates.TryGetValue(rule.szSID, out var existing)
                && existing.szPreSID == szPreSID
                && existing.gate.szOperator == szOp
                && Nullable.Equals(existing.gate.dValue, rule.dPreValue)
                && existing.gate.nDelaySec == Math.Max(0, rule.nPreDelaySec))
                continue;

            _gates[rule.szSID] = new GateEntry(szPreSID, new AlarmPreconditionGate(szOp, rule.dPreValue, rule.nPreDelaySec));
        }

        foreach (var szSID in _gates.Keys)
            if (!_rules.ContainsKey(szSID))
                _gates.TryRemove(szSID, out _);

        foreach (var szKey in _onDelays.Keys)
        {
            int nColon = szKey.LastIndexOf(':');
            if (nColon < 0 || !_rules.ContainsKey(szKey.Substring(0, nColon)))
                _onDelays.TryRemove(szKey, out _);
        }
    }

    private void ResetOnDelays(string szSID)
    {
        foreach (var szType in new[] { "high", "low", "di" })
            if (_onDelays.TryGetValue($"{szSID}:{szType}", out var timer))
                timer.Reset();
    }

    /// <summary>寫入最新值快取；較舊的資料（如 reload 時讀 LatestData）不覆蓋較新的即時值</summary>
    private LatestSample UpdateLatestValue(RealtimeDataModel data, bool isGood)
    {
        var dtTs = data.dtTimestamp == default ? DateTime.Now : data.dtTimestamp;
        var incoming = new LatestSample((double)data.fValue, isGood, dtTs,
            string.IsNullOrEmpty(data.szTagName) ? null : data.szTagName);
        return _latestValues.AddOrUpdate(data.szSID, incoming, (_, old) =>
            old.dtTimestamp > incoming.dtTimestamp
                ? old with { szTagName = incoming.szTagName ?? old.szTagName }
                : incoming with { szTagName = incoming.szTagName ?? old.szTagName });
    }

    private async Task PrefillLatestValuesAsync()
    {
        try
        {
            var latestList = await _dataRepository.GetLatestDataAsync(int.MaxValue);
            foreach (var latest in latestList)
            {
                UpdateLatestValue(new RealtimeDataModel
                {
                    szSID = latest.szSID,
                    fValue = latest.fValue,
                    dtTimestamp = latest.dtTimestamp,
                    szTagName = string.Empty
                }, latest.nQuality == 1);
            }
            _logger.LogInformation("警報最新值快取預填 {Count} 筆", _latestValues.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "預填警報最新值快取失敗");
        }
    }

    private static string ResolveName(string szSID, LatestSample? sample)
        => !string.IsNullOrEmpty(sample?.szTagName) ? sample!.szTagName! : szSID;

    private static NotifyContext BuildContext(string szSID, string szName, int nAlarmRuleId,
        AlarmCheck check, long nRelatedEventId) => new()
    {
        nSeverity = check.nSeverity,
        szSID = szSID,
        szName = szName,
        szMessageKey = check.szMessageKey,
        args = check.args,
        dtTime = DateTime.Now,
        nRelatedEventId = nRelatedEventId,
        nAlarmRuleId = nAlarmRuleId
    };

    /// <summary>
    /// 把 args dict 序列化成 JSON（EventLog.MessageArgs 用），與 Web AlarmMessageLocalizer 對齊。
    /// 簡單字串 escape：替換 backslash / 雙引號。
    /// </summary>
    private static string BuildArgsJson(IDictionary<string, string?> args)
    {
        var sb = new System.Text.StringBuilder("{");
        bool first = true;
        foreach (var kv in args)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":\"")
              .Append((kv.Value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""))
              .Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private async Task CheckTransitionAsync(
        string szSID, string szName, int nAlarmRuleId, AlarmCheck check, bool isTriggered)
    {
        string szType = check.szType;
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
                szSID, szType, check.szMessage, check.dTriggerValue);

            var dtNow = DateTime.Now;
            var eventModel = new EventLogModel
            {
                szSID = szSID,
                nEventType = 0,  // Alarm
                nSeverity = check.nSeverity,
                dTriggerValue = check.dTriggerValue,
                dThresholdValue = check.dThresholdValue,
                nOperator = check.nOperator,
                szMessage = check.szMessage,
                szMessageKey = check.szMessageKey,
                szMessageArgs = BuildArgsJson(check.args),
                dtOccurredAt = dtNow
            };
            await _repository.InsertEventAsync(eventModel); // 寫入後 eventModel.nId 已填回

            // 發布 MQTT 警報觸發訊息（retained）
            try
            {
                await _mqttPublisher.PublishAlarmActiveAsync(eventModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發布警報觸發 MQTT 訊息失敗（不影響警報流程）: SID={SID}", szSID);
            }

            var ctx = BuildContext(szSID, szName, nAlarmRuleId, check, eventModel.nId);
            ctx.dtTime = dtNow;

            // Line 通知（失敗不影響警報流程）
            try { await _lineService.NotifyAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "Line 通知派送失敗但警報流程繼續: SID={SID}", szSID); }

            // Email 通知（失敗不影響警報流程）
            try { await _emailService.NotifyAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "Email 通知派送失敗但警報流程繼續: SID={SID}", szSID); }

            // 簡訊通知（進背景佇列，失敗不影響警報流程）
            try { await _smsService.NotifyAsync(ctx); }
            catch (Exception ex) { _logger.LogError(ex, "簡訊通知派送失敗但警報流程繼續: SID={SID}", szSID); }
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

            // 只關同類型（Operator）那一列，不連帶關掉同 SID 其他類型仍在警報中的事件
            await _repository.ClearEventByOperatorAsync(szSID, check.nOperator);

            // 發布 MQTT 恢復訊息（空 payload 清除 retained）
            try
            {
                await _mqttPublisher.PublishAlarmClearedAsync(szSID, check.nOperator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發布警報恢復 MQTT 訊息失敗（不影響警報流程）: SID={SID}", szSID);
            }

            // 恢復通知（依規則 RecoveryNotify* 分通道決定）
            _rules.TryGetValue(szSID, out var rule);
            await NotifyRecoveryAsync(rule, BuildContext(szSID, szName, nAlarmRuleId, check, 0));
        }
    }

    // ── 內部狀態追蹤 ──

    private class AlarmState
    {
        public bool isActive { get; set; }
        public string? szType { get; set; }
        public DateTime dtLastTriggered { get; set; }
    }

    /// <summary>點位最新值快取項</summary>
    private sealed record LatestSample(double dValue, bool isGood, DateTime dtTimestamp, string? szTagName);

    /// <summary>規則對應的前置點位 + 閘門</summary>
    private sealed record GateEntry(string szPreSID, AlarmPreconditionGate gate);

    /// <summary>單段（高 / 低 / DI）判定結果與訊息素材</summary>
    private sealed record AlarmCheck(
        string szType, byte nOperator, bool isTriggered,
        double dTriggerValue, double dThresholdValue, byte nSeverity,
        string szMessage, string szMessageKey, IDictionary<string, string?> args);
}
