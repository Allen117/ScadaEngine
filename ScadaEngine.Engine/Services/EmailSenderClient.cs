using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// MailKit SMTP 寄送封裝 — 對每個收件人獨立寄一封信
/// 失敗重試 1 次（網路 / 5xx 類），4xx-class 不重試
/// </summary>
public class EmailSenderClient
{
    private readonly ILogger<EmailSenderClient> _logger;
    private EmailSettingModel _setting = new();
    private bool _isInitialized = false;

    public EmailSenderClient(ILogger<EmailSenderClient> logger)
    {
        _logger = logger;
    }

    public void Initialize(EmailSettingModel setting)
    {
        _setting = setting ?? new EmailSettingModel();
        _isInitialized = true;

        if (!_setting.EnableNotification)
        {
            _logger.LogInformation("Email 通知未啟用（設定檔 EnableNotification=false）");
            return;
        }
        if (string.IsNullOrWhiteSpace(_setting.SmtpHost) || string.IsNullOrWhiteSpace(_setting.FromAddress))
        {
            _logger.LogWarning("Email 設定不完整（SmtpHost / FromAddress 為空），通知功能將停用");
            _setting.EnableNotification = false;
        }
    }

    public bool IsEnabled => _isInitialized && _setting.EnableNotification;
    public EmailSettingModel Setting => _setting;

    /// <summary>
    /// 寄送一封 HTML 信給單一收件人，回傳是否成功
    /// </summary>
    public async Task<bool> SendAsync(string szToEmail, string? szToDisplayName,
        string szSubject, string szHtmlBody, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return false;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_setting.FromDisplayName ?? "SCADA Engine", _setting.FromAddress));
        message.To.Add(new MailboxAddress(szToDisplayName ?? szToEmail, szToEmail));
        message.Subject = szSubject;
        message.Body = new TextPart("html") { Text = szHtmlBody };

        for (int nAttempt = 1; nAttempt <= 2; nAttempt++)
        {
            try
            {
                using var client = new SmtpClient();
                client.Timeout = 15000; // 15 秒
                var socketOptions = _setting.UseSsl ? SecureSocketOptions.SslOnConnect
                    : (_setting.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

                await client.ConnectAsync(_setting.SmtpHost, _setting.SmtpPort, socketOptions, cancellationToken);
                if (!string.IsNullOrEmpty(_setting.Username))
                    await client.AuthenticateAsync(_setting.Username, _setting.Password, cancellationToken);

                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);

                _logger.LogDebug("Email 寄送成功: To={To}, Subject={Subject}, Attempt={Attempt}",
                    szToEmail, szSubject, nAttempt);
                return true;
            }
            catch (AuthenticationException ex)
            {
                _logger.LogError(ex, "Email SMTP 認證失敗（不重試）: To={To}", szToEmail);
                return false;
            }
            catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500)
            {
                // 4xx 類錯誤不重試（permanent failure，例：mailbox 不存在）
                _logger.LogError(ex, "Email 被 SMTP 拒絕（不重試）: To={To}, Status={Status}",
                    szToEmail, ex.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email 寄送失敗: To={To}, Attempt={Attempt}", szToEmail, nAttempt);
                if (nAttempt == 1) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        _logger.LogError("Email 寄送最終失敗（已重試 1 次）: To={To}, Subject={Subject}", szToEmail, szSubject);
        return false;
    }
}
