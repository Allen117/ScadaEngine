using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// 警報規則 CRUD 服務 — 對應 AlarmRules 資料表
    /// </summary>
    public class AlarmRuleService
    {
        private readonly ILogger<AlarmRuleService> _logger;
        private readonly DatabaseConfigService _configService;
        private readonly AlarmRuleReloadPublisher _reloadPublisher;
        private string _szConnectionString = string.Empty;

        public AlarmRuleService(
            ILogger<AlarmRuleService> logger,
            DatabaseConfigService configService,
            AlarmRuleReloadPublisher reloadPublisher)
        {
            _logger = logger;
            _configService = configService;
            _reloadPublisher = reloadPublisher;
        }

        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _configService.GetConnectionStringAsync();
        }

        /// <summary>
        /// 取得所有啟用的警報規則（AlarmMonitorService 用）
        /// </summary>
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

        /// <summary>
        /// 取得所有警報規則（管理頁面用，含點位名稱）
        /// </summary>
        public async Task<IEnumerable<AlarmRuleModel>> GetAllRulesAsync()
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT r.Id               AS nId,
                           r.SID              AS szSID,
                           r.IsEnabled        AS isEnabled,
                           r.IsAlarmHigh      AS isAlarmHigh,
                           r.AlarmHighValue   AS dAlarmHighValue,
                           r.DeadbandHigh     AS dDeadbandHigh,
                           r.AlarmHighSeverity AS nAlarmHighSeverity,
                           r.IsAlarmLow       AS isAlarmLow,
                           r.AlarmLowValue    AS dAlarmLowValue,
                           r.DeadbandLow      AS dDeadbandLow,
                           r.AlarmLowSeverity AS nAlarmLowSeverity,
                           r.IsDiAlarm        AS isDiAlarm,
                           r.DiTriggerState   AS szDiTriggerState,
                           r.DiAlarmSeverity  AS nDiAlarmSeverity,
                           r.DiOnLabel        AS szDiOnLabel,
                           r.DiOffLabel       AS szDiOffLabel,
                           r.Remarks          AS szRemarks,
                           COALESCE(p.Name, cp.Name) AS szPointName
                    FROM AlarmRules r
                    LEFT JOIN ModbusPoints p ON r.SID = p.SID
                    LEFT JOIN CalculatedPoints cp ON r.SID = cp.SID
                    ORDER BY r.SID";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<AlarmRuleModel>(szSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得所有警報規則失敗");
                return Enumerable.Empty<AlarmRuleModel>();
            }
        }

        /// <summary>
        /// 新增或更新規則（UPSERT by SID）
        /// </summary>
        public async Task<bool> SaveRuleAsync(AlarmRuleSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                // 若有 id 則 UPDATE，否則檢查 SID 是否已存在
                string szSql;
                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    szSql = @"
                        UPDATE AlarmRules SET
                            SID = @SID, IsEnabled = @IsEnabled,
                            IsAlarmHigh = @IsAlarmHigh, AlarmHighValue = @AlarmHighValue,
                            DeadbandHigh = @DeadbandHigh, AlarmHighSeverity = @AlarmHighSeverity,
                            IsAlarmLow = @IsAlarmLow, AlarmLowValue = @AlarmLowValue,
                            DeadbandLow = @DeadbandLow, AlarmLowSeverity = @AlarmLowSeverity,
                            IsDiAlarm = @IsDiAlarm, DiTriggerState = @DiTriggerState,
                            DiAlarmSeverity = @DiAlarmSeverity,
                            DiOnLabel = @DiOnLabel, DiOffLabel = @DiOffLabel,
                            Remarks = @Remarks, UpdatedAt = GETDATE()
                        WHERE Id = @Id";
                }
                else
                {
                    szSql = @"
                        IF EXISTS (SELECT 1 FROM AlarmRules WHERE SID = @SID)
                            UPDATE AlarmRules SET
                                IsEnabled = @IsEnabled,
                                IsAlarmHigh = @IsAlarmHigh, AlarmHighValue = @AlarmHighValue,
                                DeadbandHigh = @DeadbandHigh, AlarmHighSeverity = @AlarmHighSeverity,
                                IsAlarmLow = @IsAlarmLow, AlarmLowValue = @AlarmLowValue,
                                DeadbandLow = @DeadbandLow, AlarmLowSeverity = @AlarmLowSeverity,
                                IsDiAlarm = @IsDiAlarm, DiTriggerState = @DiTriggerState,
                                DiAlarmSeverity = @DiAlarmSeverity,
                                DiOnLabel = @DiOnLabel, DiOffLabel = @DiOffLabel,
                                Remarks = @Remarks, UpdatedAt = GETDATE()
                            WHERE SID = @SID
                        ELSE
                            INSERT INTO AlarmRules
                                (SID, IsEnabled, IsAlarmHigh, AlarmHighValue, DeadbandHigh, AlarmHighSeverity,
                                 IsAlarmLow, AlarmLowValue, DeadbandLow, AlarmLowSeverity,
                                 IsDiAlarm, DiTriggerState, DiAlarmSeverity, DiOnLabel, DiOffLabel, Remarks)
                            VALUES
                                (@SID, @IsEnabled, @IsAlarmHigh, @AlarmHighValue, @DeadbandHigh, @AlarmHighSeverity,
                                 @IsAlarmLow, @AlarmLowValue, @DeadbandLow, @AlarmLowSeverity,
                                 @IsDiAlarm, @DiTriggerState, @DiAlarmSeverity, @DiOnLabel, @DiOffLabel, @Remarks)";
                }

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new
                {
                    Id               = dto.id ?? 0,
                    SID              = dto.sid,
                    IsEnabled        = dto.isEnabled,
                    IsAlarmHigh      = dto.isAlarmHigh,
                    AlarmHighValue   = dto.alarmHighValue,
                    DeadbandHigh     = dto.deadbandHigh ?? 0.0,
                    AlarmHighSeverity = (byte)dto.alarmHighSeverity,
                    IsAlarmLow       = dto.isAlarmLow,
                    AlarmLowValue    = dto.alarmLowValue,
                    DeadbandLow      = dto.deadbandLow ?? 0.0,
                    AlarmLowSeverity = (byte)dto.alarmLowSeverity,
                    IsDiAlarm        = dto.isDiAlarm,
                    DiTriggerState   = dto.diTriggerState,
                    DiAlarmSeverity  = (byte)dto.diAlarmSeverity,
                    DiOnLabel        = dto.diOnLabel,
                    DiOffLabel       = dto.diOffLabel,
                    Remarks          = dto.remarks
                });

                _logger.LogInformation("儲存警報規則: SID={SID}, Affected={Count}", dto.sid, nAffected);

                // DB 寫入成功後通知 Engine 即時重評（失敗不影響回應，已在 publisher 內捕捉）
                if (nAffected > 0)
                    await _reloadPublisher.PublishReloadAsync(dto.sid);

                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存警報規則失敗: SID={SID}", dto.sid);
                return false;
            }
        }

        /// <summary>
        /// 刪除指定規則
        /// </summary>
        public async Task<bool> DeleteRuleAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = "DELETE FROM AlarmRules WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId });
                _logger.LogInformation("刪除警報規則: Id={Id}, Affected={Count}", nId, nAffected);

                // DB 刪除成功後通知 Engine 即時清掃孤立警報（不知道對應 SID，傳 null 由 Engine 全量重評）
                if (nAffected > 0)
                    await _reloadPublisher.PublishReloadAsync(null);

                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除警報規則失敗: Id={Id}", nId);
                return false;
            }
        }
    }
}
