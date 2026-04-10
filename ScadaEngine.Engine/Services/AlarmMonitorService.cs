using System.Collections.Concurrent;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端警報監控服務 — 接收即時資料批次，比對 AlarmRules 規則，直接寫入 EventLog
/// 設計為 Singleton，由 Worker.OnModbusDataCollected 事件驅動呼叫
/// </summary>
public class AlarmMonitorService
{
    private readonly ILogger<AlarmMonitorService> _logger;
    private readonly AlarmEventLogRepository _repository;

    /// <summary>快取所有啟用的規則（key = SID）</summary>
    private readonly ConcurrentDictionary<string, AlarmRuleModel> _rules = new();

    /// <summary>追蹤每個 SID:type 目前的警報狀態</summary>
    private readonly ConcurrentDictionary<string, AlarmState> _alarmStates = new();

    /// <summary>規則重新載入計時器</summary>
    private readonly Timer _reloadTimer;

    /// <summary>是否已完成初始化</summary>
    private bool _isInitialized = false;

    public AlarmMonitorService(
        ILogger<AlarmMonitorService> logger,
        AlarmEventLogRepository repository)
    {
        _logger = logger;
        _repository = repository;

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

        await ReloadRulesAsync();
        await InitAlarmStatesFromDbAsync();

        // 啟動定期重載計時器（60 秒間隔）
        _reloadTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        _isInitialized = true;
        _logger.LogInformation("警報監控服務初始化完成");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入警報規則失敗");
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

                await CheckTransitionAsync(szSID, "high", isTriggered,
                    dVal, dThreshold, 2, rule.nAlarmHighSeverity,
                    $"{szName} 超過上限 {dThreshold}");
            }

            // ── 下限警報 ──
            if (rule.isAlarmLow && rule.dAlarmLowValue.HasValue)
            {
                double dThreshold = rule.dAlarmLowValue.Value;
                double dDeadband = rule.dDeadbandLow ?? 0;
                bool isTriggered = dVal <= (dThreshold + dDeadband);

                await CheckTransitionAsync(szSID, "low", isTriggered,
                    dVal, dThreshold, 3, rule.nAlarmLowSeverity,
                    $"{szName} 低於下限 {dThreshold}");
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

                await CheckTransitionAsync(szSID, "di", isTriggered,
                    dVal, isOn ? 1 : 0, 4, rule.nDiAlarmSeverity,
                    $"{szName} 狀態為 {szStateLabel} 觸發警報");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "評估警報失敗: SID={SID}", data.szSID);
        }
    }

    private async Task CheckTransitionAsync(
        string szSID, string szType, bool isTriggered,
        double dTriggerValue, double dThresholdValue, byte nOperator,
        byte nSeverity, string szMessage)
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

            await _repository.InsertEventAsync(new EventLogModel
            {
                szSID = szSID,
                nEventType = 0,  // Alarm
                nSeverity = nSeverity,
                dTriggerValue = dTriggerValue,
                dThresholdValue = dThresholdValue,
                nOperator = nOperator,
                szMessage = szMessage,
                dtOccurredAt = DateTime.Now
            });
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
