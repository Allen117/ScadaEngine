using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// Line 通知收件群組 CRUD — 對應 LineNotifyTargets 資料表
    /// </summary>
    public class LineTargetService
    {
        private readonly ILogger<LineTargetService> _logger;
        private readonly DatabaseConfigService _configService;
        private string _szConnectionString = string.Empty;

        public LineTargetService(ILogger<LineTargetService> logger, DatabaseConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _configService.GetConnectionStringAsync();
        }

        public async Task<IEnumerable<LineNotifyTargetModel>> GetAllAsync()
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           GroupId     AS szGroupId,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           IsEnabled   AS isEnabled,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM LineNotifyTargets
                    ORDER BY Id";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<LineNotifyTargetModel>(szSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Line 收件群組失敗");
                return Enumerable.Empty<LineNotifyTargetModel>();
            }
        }

        public async Task<LineNotifyTargetModel?> GetByIdAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           GroupId     AS szGroupId,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           IsEnabled   AS isEnabled,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM LineNotifyTargets
                    WHERE Id = @Id";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryFirstOrDefaultAsync<LineNotifyTargetModel>(szSql, new { Id = nId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 Line 收件群組失敗: Id={Id}", nId);
                return null;
            }
        }

        public async Task<bool> SaveAsync(LineTargetSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                string szSql;
                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    szSql = @"
                        UPDATE LineNotifyTargets SET
                            GroupId = @GroupId,
                            Label = @Label,
                            MaxSeverity = @MaxSeverity,
                            IsEnabled = @IsEnabled,
                            UpdatedAt = GETDATE()
                        WHERE Id = @Id";
                }
                else
                {
                    szSql = @"
                        INSERT INTO LineNotifyTargets
                            (GroupId, Label, MaxSeverity, IsEnabled)
                        VALUES
                            (@GroupId, @Label, @MaxSeverity, @IsEnabled)";
                }

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new
                {
                    Id          = dto.id ?? 0,
                    GroupId     = dto.groupId.Trim(),
                    Label       = dto.label.Trim(),
                    MaxSeverity = dto.maxSeverity,
                    IsEnabled   = dto.isEnabled
                });

                _logger.LogInformation("儲存 Line 收件群組: Id={Id}, GroupId={Group}, Affected={N}",
                    dto.id, dto.groupId, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 Line 收件群組失敗: GroupId={Group}", dto.groupId);
                return false;
            }
        }

        public async Task<bool> DeleteAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = "DELETE FROM LineNotifyTargets WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId });
                _logger.LogInformation("刪除 Line 收件群組: Id={Id}, Affected={N}", nId, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除 Line 收件群組失敗: Id={Id}", nId);
                return false;
            }
        }

        public async Task<bool> ToggleEnabledAsync(int nId, bool isEnabled)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    UPDATE LineNotifyTargets
                    SET IsEnabled = @IsEnabled, UpdatedAt = GETDATE()
                    WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId, IsEnabled = isEnabled });
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切換 Line 收件群組啟用狀態失敗: Id={Id}", nId);
                return false;
            }
        }
    }
}
