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
                           Remarks          AS szRemarks,
                           IsPrecondition   AS isPrecondition,
                           PreSID           AS szPreSID,
                           PreOperator      AS szPreOperator,
                           PreValue         AS dPreValue,
                           PreDelaySec      AS nPreDelaySec,
                           RecoveryNotifyLine  AS isRecoveryNotifyLine,
                           RecoveryNotifyEmail AS isRecoveryNotifyEmail,
                           RecoveryNotifySms   AS isRecoveryNotifySms,
                           AlarmDelaySec    AS nAlarmDelaySec
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
                           r.IsPrecondition   AS isPrecondition,
                           r.PreSID           AS szPreSID,
                           r.PreOperator      AS szPreOperator,
                           r.PreValue         AS dPreValue,
                           r.PreDelaySec      AS nPreDelaySec,
                           r.RecoveryNotifyLine  AS isRecoveryNotifyLine,
                           r.RecoveryNotifyEmail AS isRecoveryNotifyEmail,
                           r.RecoveryNotifySms   AS isRecoveryNotifySms,
                           r.AlarmDelaySec    AS nAlarmDelaySec,
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
        /// 取得單一 SID 的規則（ScadaPage 右鍵「警報設定」彈窗載入用）；無規則回 null
        /// </summary>
        public async Task<AlarmRuleModel?> GetRuleBySidAsync(string szSID)
        {
            var rules = await GetAllRulesAsync();
            return rules.FirstOrDefault(r => string.Equals(r.szSID, szSID, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>前置條件 / 延遲秒數上限（1 天）</summary>
        public const int MaxDelaySec = 86400;

        /// <summary>
        /// 驗證規則 DTO，回傳錯誤訊息 resx key；通過回 null。
        /// 前置條件：點位必填、不可等於本點、不可為 DMD-/NRG* 虛擬點（平時不進 Engine 評估器，閘門會永遠關閉）、
        /// 運算子白名單、比較運算子須有比較值；延遲 0 ~ 86400 秒。
        /// </summary>
        public static string? ValidateRule(AlarmRuleSaveDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.sid))
                return "alarm.error.sid_required";
            if (dto.alarmDelaySec < 0 || dto.alarmDelaySec > MaxDelaySec)
                return "alarm.error.delay_range";
            if (!dto.isPrecondition)
                return null;

            var szPreSid = dto.preSid?.Trim() ?? string.Empty;
            if (szPreSid.Length == 0)
                return "alarm.error.pre_sid_required";
            if (string.Equals(szPreSid, dto.sid.Trim(), StringComparison.OrdinalIgnoreCase))
                return "alarm.error.pre_sid_self";
            if (IsVirtualSid(szPreSid))
                return "alarm.error.pre_sid_virtual";

            var szOp = dto.preOperator?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!ValidPreOperators.Contains(szOp))
                return "alarm.error.pre_operator_invalid";
            if (szOp != "ON" && szOp != "OFF" && !dto.preValue.HasValue)
                return "alarm.error.pre_value_required";
            if (dto.preDelaySec < 0 || dto.preDelaySec > MaxDelaySec)
                return "alarm.error.delay_range";
            return null;
        }

        /// <summary>前置條件運算子白名單（與 Engine AlarmPreconditionGate.ValidOperators 一致）</summary>
        public static readonly string[] ValidPreOperators = ["GE", "GT", "LE", "LT", "EQ", "NE", "ON", "OFF"];

        /// <summary>需量 / 迴路用電虛擬點位（Engine 另行發布，平時不進警報評估器）</summary>
        public static bool IsVirtualSid(string szSid)
            => szSid.StartsWith("DMD-", StringComparison.OrdinalIgnoreCase)
            || szSid.StartsWith("NRGD-", StringComparison.OrdinalIgnoreCase)
            || szSid.StartsWith("NRGM-", StringComparison.OrdinalIgnoreCase)
            || szSid.StartsWith("NRGP-", StringComparison.OrdinalIgnoreCase);

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
                            Remarks = @Remarks,
                            IsPrecondition = @IsPrecondition, PreSID = @PreSID, PreOperator = @PreOperator,
                            PreValue = @PreValue, PreDelaySec = @PreDelaySec,
                            RecoveryNotifyLine = @RecoveryNotifyLine, RecoveryNotifyEmail = @RecoveryNotifyEmail,
                            RecoveryNotifySms = @RecoveryNotifySms, AlarmDelaySec = @AlarmDelaySec,
                            UpdatedAt = GETDATE()
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
                                Remarks = @Remarks,
                                IsPrecondition = @IsPrecondition, PreSID = @PreSID, PreOperator = @PreOperator,
                                PreValue = @PreValue, PreDelaySec = @PreDelaySec,
                                RecoveryNotifyLine = @RecoveryNotifyLine, RecoveryNotifyEmail = @RecoveryNotifyEmail,
                                RecoveryNotifySms = @RecoveryNotifySms, AlarmDelaySec = @AlarmDelaySec,
                                UpdatedAt = GETDATE()
                            WHERE SID = @SID
                        ELSE
                            INSERT INTO AlarmRules
                                (SID, IsEnabled, IsAlarmHigh, AlarmHighValue, DeadbandHigh, AlarmHighSeverity,
                                 IsAlarmLow, AlarmLowValue, DeadbandLow, AlarmLowSeverity,
                                 IsDiAlarm, DiTriggerState, DiAlarmSeverity, DiOnLabel, DiOffLabel, Remarks,
                                 IsPrecondition, PreSID, PreOperator, PreValue, PreDelaySec,
                                 RecoveryNotifyLine, RecoveryNotifyEmail, RecoveryNotifySms, AlarmDelaySec)
                            VALUES
                                (@SID, @IsEnabled, @IsAlarmHigh, @AlarmHighValue, @DeadbandHigh, @AlarmHighSeverity,
                                 @IsAlarmLow, @AlarmLowValue, @DeadbandLow, @AlarmLowSeverity,
                                 @IsDiAlarm, @DiTriggerState, @DiAlarmSeverity, @DiOnLabel, @DiOffLabel, @Remarks,
                                 @IsPrecondition, @PreSID, @PreOperator, @PreValue, @PreDelaySec,
                                 @RecoveryNotifyLine, @RecoveryNotifyEmail, @RecoveryNotifySms, @AlarmDelaySec)";
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
                    Remarks          = dto.remarks,
                    IsPrecondition   = dto.isPrecondition,
                    PreSID           = string.IsNullOrWhiteSpace(dto.preSid) ? null : dto.preSid.Trim(),
                    PreOperator      = string.IsNullOrWhiteSpace(dto.preOperator) ? null : dto.preOperator.Trim().ToUpperInvariant(),
                    PreValue         = dto.preValue,
                    PreDelaySec      = dto.preDelaySec,
                    RecoveryNotifyLine  = dto.recoveryNotifyLine,
                    RecoveryNotifyEmail = dto.recoveryNotifyEmail,
                    RecoveryNotifySms   = dto.recoveryNotifySms,
                    AlarmDelaySec    = dto.alarmDelaySec
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
