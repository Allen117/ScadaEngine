using System.Text.Json;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// EmailSetting.json 讀寫服務 — Web UI 編輯 SMTP 設定時直接寫 JSON 檔
    /// 與 Engine 共用同一份 JSON（透過相對路徑 ../ScadaEngine.Engine/Setting/）
    /// 密碼空字串時保留既有密碼（避免每次儲存都要重輸入）
    /// </summary>
    public class EmailSenderConfigService
    {
        private readonly ILogger<EmailSenderConfigService> _logger;
        private readonly object _fileLock = new();

        public EmailSenderConfigService(ILogger<EmailSenderConfigService> logger)
        {
            _logger = logger;
        }

        /// <summary>讀取設定（不回傳密碼明文，只回傳 hasPassword 旗標）</summary>
        public EmailSenderConfigDto Read()
        {
            var setting = LoadFromFile();
            return new EmailSenderConfigDto
            {
                enableNotification = setting.EnableNotification,
                smtpHost = setting.SmtpHost,
                smtpPort = setting.SmtpPort,
                useSsl = setting.UseSsl,
                useStartTls = setting.UseStartTls,
                username = setting.Username,
                password = string.Empty,
                fromAddress = setting.FromAddress,
                fromDisplayName = setting.FromDisplayName,
                ratePerMinute = setting.RatePerMinute,
                testSendThrottleSeconds = setting.TestSendThrottleSeconds,
                hasPassword = !string.IsNullOrEmpty(setting.Password)
            };
        }

        /// <summary>寫入設定。dto.password 為空字串 → 保留檔案內既有密碼。</summary>
        public bool Save(EmailSenderConfigDto dto)
        {
            try
            {
                lock (_fileLock)
                {
                    var existing = LoadFromFile();
                    var szNewPassword = string.IsNullOrEmpty(dto.password) ? existing.Password : dto.password;

                    var setting = new EmailSettingModel
                    {
                        EnableNotification = dto.enableNotification,
                        SmtpHost = (dto.smtpHost ?? string.Empty).Trim(),
                        SmtpPort = dto.smtpPort > 0 ? dto.smtpPort : 587,
                        UseSsl = dto.useSsl,
                        UseStartTls = dto.useStartTls,
                        Username = (dto.username ?? string.Empty).Trim(),
                        Password = szNewPassword ?? string.Empty,
                        FromAddress = (dto.fromAddress ?? string.Empty).Trim(),
                        FromDisplayName = (dto.fromDisplayName ?? "SCADA Engine").Trim(),
                        RatePerMinute = dto.ratePerMinute > 0 ? dto.ratePerMinute : 10,
                        TestSendThrottleSeconds = dto.testSendThrottleSeconds > 0 ? dto.testSendThrottleSeconds : 10
                    };

                    var szPath = ResolvePath();
                    var szJson = JsonSerializer.Serialize(setting, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(szPath, szJson);
                    _logger.LogInformation("已更新 EmailSetting.json: {Path}", szPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 EmailSetting.json 失敗");
                return false;
            }
        }

        /// <summary>給其他 Web 服務取用完整設定（含密碼）— 不對外暴露</summary>
        public EmailSettingModel LoadFromFile()
        {
            try
            {
                lock (_fileLock)
                {
                    var szPath = ResolvePath();
                    if (!File.Exists(szPath))
                    {
                        _logger.LogWarning("找不到 EmailSetting.json，使用預設值: {Path}", szPath);
                        return new EmailSettingModel { EnableNotification = false };
                    }
                    var szJson = File.ReadAllText(szPath);
                    var setting = JsonSerializer.Deserialize<EmailSettingModel>(
                        szJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return setting ?? new EmailSettingModel { EnableNotification = false };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 EmailSetting.json 失敗");
                return new EmailSettingModel { EnableNotification = false };
            }
        }

        private static string ResolvePath()
        {
            // 優先使用本地 Setting/，開發時 fallback 到 Engine 專案
            var szLocal = Path.Combine(AppContext.BaseDirectory, "Setting", "EmailSetting.json");
            if (File.Exists(szLocal)) return szLocal;
            var szEngine = Path.Combine("..", "ScadaEngine.Engine", "Setting", "EmailSetting.json");
            if (File.Exists(szEngine)) return szEngine;
            // 都不存在時，預設寫到 Engine 路徑（與其他設定一致）
            return szEngine;
        }
    }
}
