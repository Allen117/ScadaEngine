using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// Email 群組 / 收件人 / 群組-規則對應 CRUD
    /// </summary>
    public class EmailGroupService
    {
        private readonly ILogger<EmailGroupService> _logger;
        private readonly DatabaseConfigService _configService;
        private string _szConnectionString = string.Empty;

        public EmailGroupService(ILogger<EmailGroupService> logger, DatabaseConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _configService.GetConnectionStringAsync();
        }

        // ── Groups ──

        public async Task<IEnumerable<EmailGroupModel>> GetAllGroupsAsync()
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           Name        AS szName,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           Language    AS szLanguage,
                           IsEnabled   AS isEnabled,
                           Remarks     AS szRemarks,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM EmailGroups
                    ORDER BY Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<EmailGroupModel>(szSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Email 群組失敗");
                return Enumerable.Empty<EmailGroupModel>();
            }
        }

        public async Task<EmailGroupModel?> GetGroupByIdAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           Name        AS szName,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           Language    AS szLanguage,
                           IsEnabled   AS isEnabled,
                           Remarks     AS szRemarks,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM EmailGroups
                    WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryFirstOrDefaultAsync<EmailGroupModel>(szSql, new { Id = nId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Email 群組失敗: Id={Id}", nId);
                return null;
            }
        }

        public async Task<int> SaveGroupAsync(EmailGroupSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();

                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    const string szSql = @"
                        UPDATE EmailGroups SET
                            Name        = @Name,
                            Label       = @Label,
                            MaxSeverity = @MaxSeverity,
                            Language    = @Language,
                            IsEnabled   = @IsEnabled,
                            Remarks     = @Remarks,
                            UpdatedAt   = GETDATE()
                        WHERE Id = @Id";
                    var nAffected = await connection.ExecuteAsync(szSql, new
                    {
                        Id          = dto.id.Value,
                        Name        = dto.name.Trim(),
                        Label       = dto.label.Trim(),
                        MaxSeverity = dto.maxSeverity,
                        Language    = string.IsNullOrEmpty(dto.language) ? "zh-TW" : dto.language,
                        IsEnabled   = dto.isEnabled,
                        Remarks     = dto.remarks
                    });
                    return nAffected > 0 ? dto.id.Value : 0;
                }
                else
                {
                    const string szSql = @"
                        INSERT INTO EmailGroups (Name, Label, MaxSeverity, Language, IsEnabled, Remarks)
                        OUTPUT INSERTED.Id
                        VALUES (@Name, @Label, @MaxSeverity, @Language, @IsEnabled, @Remarks)";
                    var nNewId = await connection.ExecuteScalarAsync<int>(szSql, new
                    {
                        Name        = dto.name.Trim(),
                        Label       = dto.label.Trim(),
                        MaxSeverity = dto.maxSeverity,
                        Language    = string.IsNullOrEmpty(dto.language) ? "zh-TW" : dto.language,
                        IsEnabled   = dto.isEnabled,
                        Remarks     = dto.remarks
                    });
                    return nNewId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 Email 群組失敗: Name={Name}", dto.name);
                return 0;
            }
        }

        public async Task<bool> DeleteGroupAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                // 一起刪 group / recipients / rule map（無 FK，應用程式保證一致性）
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                using var tx = connection.BeginTransaction();
                await connection.ExecuteAsync("DELETE FROM EmailRecipients WHERE GroupId = @Id", new { Id = nId }, tx);
                await connection.ExecuteAsync("DELETE FROM EmailGroupRuleMap WHERE GroupId = @Id", new { Id = nId }, tx);
                var nAffected = await connection.ExecuteAsync("DELETE FROM EmailGroups WHERE Id = @Id", new { Id = nId }, tx);
                tx.Commit();
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除 Email 群組失敗: Id={Id}", nId);
                return false;
            }
        }

        public async Task<bool> ToggleGroupEnabledAsync(int nId, bool isEnabled)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    UPDATE EmailGroups SET IsEnabled = @IsEnabled, UpdatedAt = GETDATE() WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.ExecuteAsync(szSql, new { Id = nId, IsEnabled = isEnabled }) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切換 Email 群組啟用失敗: Id={Id}", nId);
                return false;
            }
        }

        // ── Recipients ──

        public async Task<IEnumerable<EmailRecipientModel>> GetRecipientsByGroupAsync(int nGroupId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id           AS nId,
                           GroupId      AS nGroupId,
                           EmailAddress AS szEmailAddress,
                           DisplayName  AS szDisplayName,
                           IsEnabled    AS isEnabled,
                           CreatedAt    AS dtCreatedAt,
                           UpdatedAt    AS dtUpdatedAt
                    FROM EmailRecipients
                    WHERE GroupId = @GroupId
                    ORDER BY Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<EmailRecipientModel>(szSql, new { GroupId = nGroupId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Email 收件人失敗: GroupId={GroupId}", nGroupId);
                return Enumerable.Empty<EmailRecipientModel>();
            }
        }

        public async Task<bool> SaveRecipientAsync(EmailRecipientSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                string szSql;
                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    szSql = @"
                        UPDATE EmailRecipients SET
                            EmailAddress = @EmailAddress,
                            DisplayName  = @DisplayName,
                            IsEnabled    = @IsEnabled,
                            UpdatedAt    = GETDATE()
                        WHERE Id = @Id";
                }
                else
                {
                    szSql = @"
                        INSERT INTO EmailRecipients (GroupId, EmailAddress, DisplayName, IsEnabled)
                        VALUES (@GroupId, @EmailAddress, @DisplayName, @IsEnabled)";
                }
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.ExecuteAsync(szSql, new
                {
                    Id           = dto.id ?? 0,
                    GroupId      = dto.groupId,
                    EmailAddress = dto.emailAddress.Trim(),
                    DisplayName  = dto.displayName,
                    IsEnabled    = dto.isEnabled
                }) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 Email 收件人失敗: GroupId={GroupId}", dto.groupId);
                return false;
            }
        }

        public async Task<bool> DeleteRecipientAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.ExecuteAsync(
                    "DELETE FROM EmailRecipients WHERE Id = @Id", new { Id = nId }) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除 Email 收件人失敗: Id={Id}", nId);
                return false;
            }
        }

        // ── Group-Rule Mapping ──

        public async Task<List<int>> GetRuleIdsByGroupAsync(int nGroupId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql =
                    "SELECT AlarmRuleId FROM EmailGroupRuleMap WHERE GroupId = @GroupId";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return (await connection.QueryAsync<int>(szSql, new { GroupId = nGroupId })).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Email 群組規則對應失敗: GroupId={GroupId}", nGroupId);
                return new List<int>();
            }
        }

        /// <summary>用 dto.alarmRuleIds 覆寫 nGroupId 的所有對應</summary>
        public async Task<bool> SaveMappingAsync(EmailGroupRuleMappingDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                using var tx = connection.BeginTransaction();

                await connection.ExecuteAsync(
                    "DELETE FROM EmailGroupRuleMap WHERE GroupId = @GroupId",
                    new { GroupId = dto.groupId }, tx);

                if (dto.alarmRuleIds != null && dto.alarmRuleIds.Count > 0)
                {
                    var rows = dto.alarmRuleIds.Distinct().Select(rid => new
                    {
                        GroupId = dto.groupId,
                        AlarmRuleId = rid
                    });
                    await connection.ExecuteAsync(
                        "INSERT INTO EmailGroupRuleMap (GroupId, AlarmRuleId) VALUES (@GroupId, @AlarmRuleId)",
                        rows, tx);
                }
                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 Email 群組規則對應失敗: GroupId={GroupId}", dto.groupId);
                return false;
            }
        }
    }
}
