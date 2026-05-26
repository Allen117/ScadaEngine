using System.Collections.Concurrent;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// Email 測試寄送服務 — 給 SMTP 設定的「測試寄送」按鈕使用
    /// - 直接從 EmailSenderConfigService 取最新設定（每次都重讀，便於剛改完就測）
    /// - 同 EmailAddress 10 秒內只能發一次（後端 throttle）
    /// </summary>
    public class EmailTestSendService
    {
        private readonly ILogger<EmailTestSendService> _logger;
        private readonly EmailSenderConfigService _configService;
        private readonly ConcurrentDictionary<string, DateTime> _lastTestAt = new();

        public EmailTestSendService(ILogger<EmailTestSendService> logger, EmailSenderConfigService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        public Web.Services.TestSendResult CheckThrottle(string szEmail)
        {
            var setting = _configService.LoadFromFile();
            int nThrottle = setting.TestSendThrottleSeconds > 0 ? setting.TestSendThrottleSeconds : 10;
            if (_lastTestAt.TryGetValue(szEmail, out var dtLast))
            {
                var elapsed = DateTime.UtcNow - dtLast;
                if (elapsed.TotalSeconds < nThrottle)
                    return Web.Services.TestSendResult.Throttled((int)Math.Ceiling(nThrottle - elapsed.TotalSeconds));
            }
            return Web.Services.TestSendResult.Ok();
        }

        /// <summary>寄一封測試信給指定地址，主旨/內文依 szLanguage（'zh-TW'/'en'）切換</summary>
        public async Task<Web.Services.TestSendResult> SendTestAsync(string szEmail, string? szDisplayName,
            string szLanguage, string szGroupLabel)
        {
            var throttle = CheckThrottle(szEmail);
            if (!throttle.isSuccess) return throttle;

            var setting = _configService.LoadFromFile();
            if (string.IsNullOrWhiteSpace(setting.SmtpHost) || string.IsNullOrWhiteSpace(setting.FromAddress))
                return Web.Services.TestSendResult.Failure("Email 設定不完整（SmtpHost / FromAddress 為空）");

            _lastTestAt[szEmail] = DateTime.UtcNow;

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(setting.FromDisplayName ?? "SCADA Engine", setting.FromAddress));
                message.To.Add(new MailboxAddress(szDisplayName ?? szEmail, szEmail));

                bool isEn = szLanguage == "en";
                message.Subject = isEn ? "[Test] SCADA Notification Test" : "[測試] SCADA 通知測試";
                var szBody = isEn
                    ? $"<h3>📨 SCADA Test Message</h3><p>Group: <b>{System.Net.WebUtility.HtmlEncode(szGroupLabel)}</b></p><p>Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p><p>If you receive this, your Email notification settings are correct.</p>"
                    : $"<h3>📨 SCADA 測試訊息</h3><p>群組: <b>{System.Net.WebUtility.HtmlEncode(szGroupLabel)}</b></p><p>時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p><p>若您看到這則訊息，代表 Email 通知設定正確。</p>";
                message.Body = new TextPart("html") { Text = szBody };

                using var client = new SmtpClient();
                client.Timeout = 15000;
                var socketOpts = setting.UseSsl ? SecureSocketOptions.SslOnConnect
                    : (setting.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
                await client.ConnectAsync(setting.SmtpHost, setting.SmtpPort, socketOpts);
                if (!string.IsNullOrEmpty(setting.Username))
                    await client.AuthenticateAsync(setting.Username, setting.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Email 測試寄送成功: To={To}", szEmail);
                return Web.Services.TestSendResult.Ok();
            }
            catch (AuthenticationException ex)
            {
                _logger.LogError(ex, "Email SMTP 認證失敗");
                return Web.Services.TestSendResult.Failure($"SMTP 認證失敗：{ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email 測試寄送失敗: To={To}", szEmail);
                return Web.Services.TestSendResult.Failure($"寄送失敗：{ex.Message}");
            }
        }
    }
}
