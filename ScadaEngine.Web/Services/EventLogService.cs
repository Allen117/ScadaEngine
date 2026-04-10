using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// 事件/警報記錄服務 — 負責 EventLog 資料表的 CRUD 操作
    /// </summary>
    public class EventLogService
    {
        private readonly ILogger<EventLogService> _logger;
        private readonly DatabaseConfigService _configService;
        private string _szConnectionString = string.Empty;

        public EventLogService(ILogger<EventLogService> logger, DatabaseConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        /// <summary>
        /// 確保連線字串已初始化
        /// </summary>
        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
            {
                _szConnectionString = await _configService.GetConnectionStringAsync();
            }
        }

        /// <summary>
        /// 新增一筆事件記錄（警報觸發時呼叫）
        /// </summary>
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

                _logger.LogInformation("已寫入事件記錄: SID={SID}, Message={Message}", model.szSID, model.szMessage);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "寫入事件記錄失敗: SID={SID}", model.szSID);
                return false;
            }
        }

        /// <summary>
        /// 標記指定 SID 最新一筆未恢復事件的 ClearedAt（警報恢復時呼叫）
        /// </summary>
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

        /// <summary>
        /// 查詢所有未解除的警報（ClearedAt IS NULL）
        /// </summary>
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

        /// <summary>
        /// 依條件查詢事件記錄（Report 頁面使用）
        /// </summary>
        public async Task<IEnumerable<EventLogModel>> QueryEventsAsync(
            DateTime dtStart, DateTime dtEnd,
            int? nEventType = null, int? nSeverity = null,
            string? szSID = null, int? nAcknowledged = null)
        {
            await EnsureConnectionStringAsync();
            try
            {
                var szSql = @"
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
                    WHERE OccurredAt >= @StartTime
                      AND OccurredAt <= @EndTime";

                if (nEventType.HasValue)
                    szSql += " AND EventType = @EventType";
                if (nSeverity.HasValue)
                    szSql += " AND Severity = @Severity";
                if (!string.IsNullOrWhiteSpace(szSID))
                    szSql += " AND SID LIKE @SID";
                if (nAcknowledged.HasValue)
                    szSql += " AND IsAcknowledged = @IsAcknowledged";

                szSql += " ORDER BY OccurredAt DESC";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<EventLogModel>(szSql, new
                {
                    StartTime      = dtStart,
                    EndTime        = dtEnd,
                    EventType      = nEventType,
                    Severity       = nSeverity,
                    SID            = string.IsNullOrWhiteSpace(szSID) ? null : $"%{szSID}%",
                    IsAcknowledged = nAcknowledged.HasValue ? (nAcknowledged.Value == 1) : (bool?)null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢事件記錄失敗");
                return Enumerable.Empty<EventLogModel>();
            }
        }

        /// <summary>
        /// 確認（Acknowledge）指定事件
        /// </summary>
        public async Task<bool> AcknowledgeEventAsync(long nId, string szUsername)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    UPDATE EventLog
                    SET IsAcknowledged = 1,
                        AcknowledgedBy = @AcknowledgedBy,
                        AcknowledgedAt = GETDATE()
                    WHERE Id = @Id
                      AND IsAcknowledged = 0";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new
                {
                    Id             = nId,
                    AcknowledgedBy = szUsername
                });

                if (nAffected > 0)
                    _logger.LogInformation("事件已確認: Id={Id}, 操作者={User}", nId, szUsername);

                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "確認事件失敗: Id={Id}", nId);
                return false;
            }
        }
    }
}
