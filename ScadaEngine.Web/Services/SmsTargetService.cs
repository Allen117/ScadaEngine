using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// 簡訊通知收訊號碼 CRUD — 對應 SmsNotifyTargets 資料表
    /// </summary>
    public class SmsTargetService
    {
        private readonly ILogger<SmsTargetService> _logger;
        private readonly DatabaseConfigService _configService;
        private string _szConnectionString = string.Empty;

        public SmsTargetService(ILogger<SmsTargetService> logger, DatabaseConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        private async Task EnsureConnectionStringAsync()
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _configService.GetConnectionStringAsync();
        }

        public async Task<IEnumerable<SmsNotifyTargetModel>> GetAllAsync()
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           PhoneNumber AS szPhoneNumber,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           Language    AS szLanguage,
                           IsEnabled   AS isEnabled,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM SmsNotifyTargets
                    ORDER BY Id";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryAsync<SmsNotifyTargetModel>(szSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取簡訊收訊號碼失敗");
                return Enumerable.Empty<SmsNotifyTargetModel>();
            }
        }

        public async Task<SmsNotifyTargetModel?> GetByIdAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    SELECT Id          AS nId,
                           PhoneNumber AS szPhoneNumber,
                           Label       AS szLabel,
                           MaxSeverity AS nMaxSeverity,
                           Language    AS szLanguage,
                           IsEnabled   AS isEnabled,
                           CreatedAt   AS dtCreatedAt,
                           UpdatedAt   AS dtUpdatedAt
                    FROM SmsNotifyTargets
                    WHERE Id = @Id";

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                return await connection.QueryFirstOrDefaultAsync<SmsNotifyTargetModel>(szSql, new { Id = nId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取簡訊收訊號碼失敗: Id={Id}", nId);
                return null;
            }
        }

        public async Task<bool> SaveAsync(SmsTargetSaveDto dto)
        {
            await EnsureConnectionStringAsync();
            try
            {
                string szSql;
                if (dto.id.HasValue && dto.id.Value > 0)
                {
                    szSql = @"
                        UPDATE SmsNotifyTargets SET
                            PhoneNumber = @PhoneNumber,
                            Label = @Label,
                            MaxSeverity = @MaxSeverity,
                            Language = @Language,
                            IsEnabled = @IsEnabled,
                            UpdatedAt = GETDATE()
                        WHERE Id = @Id";
                }
                else
                {
                    szSql = @"
                        INSERT INTO SmsNotifyTargets
                            (PhoneNumber, Label, MaxSeverity, Language, IsEnabled)
                        VALUES
                            (@PhoneNumber, @Label, @MaxSeverity, @Language, @IsEnabled)";
                }

                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new
                {
                    Id          = dto.id ?? 0,
                    PhoneNumber = dto.phoneNumber.Trim(),
                    Label       = dto.label.Trim(),
                    MaxSeverity = dto.maxSeverity,
                    Language    = string.IsNullOrEmpty(dto.language) ? "zh-TW" : dto.language,
                    IsEnabled   = dto.isEnabled
                });

                _logger.LogInformation("儲存簡訊收訊號碼: Id={Id}, Phone={Phone}, Affected={N}",
                    dto.id, dto.phoneNumber, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存簡訊收訊號碼失敗: Phone={Phone}", dto.phoneNumber);
                return false;
            }
        }

        public async Task<bool> DeleteAsync(int nId)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = "DELETE FROM SmsNotifyTargets WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId });
                _logger.LogInformation("刪除簡訊收訊號碼: Id={Id}, Affected={N}", nId, nAffected);
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除簡訊收訊號碼失敗: Id={Id}", nId);
                return false;
            }
        }

        public async Task<bool> ToggleEnabledAsync(int nId, bool isEnabled)
        {
            await EnsureConnectionStringAsync();
            try
            {
                const string szSql = @"
                    UPDATE SmsNotifyTargets
                    SET IsEnabled = @IsEnabled, UpdatedAt = GETDATE()
                    WHERE Id = @Id";
                using var connection = new SqlConnection(_szConnectionString);
                await connection.OpenAsync();
                var nAffected = await connection.ExecuteAsync(szSql, new { Id = nId, IsEnabled = isEnabled });
                return nAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切換簡訊收訊號碼啟用狀態失敗: Id={Id}", nId);
                return false;
            }
        }
    }
}
