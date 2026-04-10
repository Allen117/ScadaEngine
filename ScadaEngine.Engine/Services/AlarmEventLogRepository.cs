using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端警報事件記錄資料存取 — 只負責 InsertEvent、ClearEvent、GetActiveAlarms、GetEnabledRules
/// </summary>
public class AlarmEventLogRepository
{
    private readonly ILogger<AlarmEventLogRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public AlarmEventLogRepository(
        ILogger<AlarmEventLogRepository> logger,
        DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    private async Task EnsureConnectionStringAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
    }

    /// <summary>取得所有啟用的警報規則</summary>
    public async Task<IEnumerable<AlarmRuleModel>> GetEnabledRulesAsync()
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                SELECT Id               AS nId,
                       SID              AS szSID,
                       IsEnabled        AS isEnabled,
                       IsAlarmHigh      AS isAlarmHigh,
                       AlarmHighValue   AS dAlarmHighValue,
                       DeadbandHigh     AS dDeadbandHigh,
                       AlarmHighSeverity AS nAlarmHighSeverity,
                       IsAlarmLow       AS isAlarmLow,
                       AlarmLowValue    AS dAlarmLowValue,
                       DeadbandLow      AS dDeadbandLow,
                       AlarmLowSeverity AS nAlarmLowSeverity,
                       IsDiAlarm        AS isDiAlarm,
                       DiTriggerState   AS szDiTriggerState,
                       DiAlarmSeverity  AS nDiAlarmSeverity,
                       DiOnLabel        AS szDiOnLabel,
                       DiOffLabel       AS szDiOffLabel,
                       Remarks          AS szRemarks
                FROM AlarmRules
                WHERE IsEnabled = 1";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            return await connection.QueryAsync<AlarmRuleModel>(szSql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得啟用警報規則失敗");
            return Enumerable.Empty<AlarmRuleModel>();
        }
    }

    /// <summary>新增一筆事件記錄（警報觸發時呼叫）</summary>
    public async Task<bool> InsertEventAsync(EventLogModel model)
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                INSERT INTO EventLog
                    (SID, EventType, Severity, TriggerValue, ThresholdValue,
                     Operator, Message, OccurredAt)
                VALUES
                    (@SID, @EventType, @Severity, @TriggerValue, @ThresholdValue,
                     @Operator, @Message, @OccurredAt)";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            var nAffected = await connection.ExecuteAsync(szSql, new
            {
                SID            = model.szSID,
                EventType      = model.nEventType,
                Severity       = model.nSeverity,
                TriggerValue   = model.dTriggerValue,
                ThresholdValue = model.dThresholdValue,
                Operator       = model.nOperator,
                Message        = model.szMessage,
                OccurredAt     = model.dtOccurredAt
            });

            _logger.LogInformation("已寫入事件記錄: SID={SID}, Message={Message}",
                model.szSID, model.szMessage);
            return nAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入事件記錄失敗: SID={SID}", model.szSID);
            return false;
        }
    }

    /// <summary>標記指定 SID 最新一筆未恢復事件的 ClearedAt</summary>
    public async Task<bool> ClearEventAsync(string szSID)
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                UPDATE EventLog
                SET ClearedAt = GETDATE()
                WHERE SID = @SID
                  AND ClearedAt IS NULL";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            var nAffected = await connection.ExecuteAsync(szSql, new { SID = szSID });

            if (nAffected > 0)
                _logger.LogInformation("事件已恢復: SID={SID}, 更新 {Count} 筆", szSID, nAffected);

            return nAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "標記事件恢復失敗: SID={SID}", szSID);
            return false;
        }
    }

    /// <summary>查詢所有未解除的警報（用於啟動時還原狀態）</summary>
    public async Task<IEnumerable<EventLogModel>> GetActiveAlarmsAsync()
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                SELECT Id             AS nId,
                       SID            AS szSID,
                       EventType      AS nEventType,
                       Severity       AS nSeverity,
                       TriggerValue   AS dTriggerValue,
                       ThresholdValue AS dThresholdValue,
                       Operator       AS nOperator,
                       Message        AS szMessage,
                       OccurredAt     AS dtOccurredAt,
                       ClearedAt      AS dtClearedAt,
                       IsAcknowledged AS isAcknowledged,
                       AcknowledgedBy AS szAcknowledgedBy,
                       AcknowledgedAt AS dtAcknowledgedAt,
                       Remarks        AS szRemarks
                FROM EventLog
                WHERE ClearedAt IS NULL
                ORDER BY OccurredAt DESC";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            return await connection.QueryAsync<EventLogModel>(szSql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢未解除警報失敗");
            return Enumerable.Empty<EventLogModel>();
        }
    }
}
