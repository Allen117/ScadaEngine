using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.SqlClient;
using MimeKit;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.DailyReport.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 能源日報 Email 寄送 — SMTP 設定沿用 Engine EmailSetting.json（經 EmailSenderConfigService 讀取），
/// 逐收件人寄送（單一連線重用），結果寫 EventLog 摘要（EventType=3 + NotifyChannel/NotifyStatus/NotifyDetail，
/// 與 Engine NotifyDeliveryLogger 格式一致）。
/// 注意：不看 EmailSetting.EnableNotification（那是警報通知總開關）— 日報有自己的 IsMailEnabled。
/// </summary>
public class DailyReportMailService
{
    /// <summary>測試寄送節流（全域一顆，避免連點灌爆 SMTP）</summary>
    private static DateTime _dtLastTestSendUtc = DateTime.MinValue;
    private static readonly object _throttleLock = new();

    private readonly EmailSenderConfigService _emailConfigService;
    private readonly DailyReportHtmlBuilder _htmlBuilder;
    private readonly DatabaseConfigService _dbConfigService;
    private readonly ILogger<DailyReportMailService> _logger;
    private string? _szConnectionString;

    public DailyReportMailService(
        EmailSenderConfigService emailConfigService,
        DailyReportHtmlBuilder htmlBuilder,
        DatabaseConfigService dbConfigService,
        ILogger<DailyReportMailService> logger)
    {
        _emailConfigService = emailConfigService;
        _htmlBuilder = htmlBuilder;
        _dbConfigService = dbConfigService;
        _logger = logger;
    }

    /// <summary>
    /// 測試寄送節流檢查 — 超限回剩餘秒數，可寄回 0（並立即佔用時間窗）。
    /// </summary>
    public int CheckTestThrottle()
    {
        var setting = _emailConfigService.LoadFromFile();
        var nThrottleSeconds = Math.Max(1, setting.TestSendThrottleSeconds);
        lock (_throttleLock)
        {
            var dElapsed = (DateTime.UtcNow - _dtLastTestSendUtc).TotalSeconds;
            if (dElapsed < nThrottleSeconds)
                return (int)Math.Ceiling(nThrottleSeconds - dElapsed);
            _dtLastTestSendUtc = DateTime.UtcNow;
            return 0;
        }
    }

    /// <summary>
    /// 寄送日報給收件清單中所有啟用的收件人（逐人寄送、單一 SMTP 連線重用），
    /// 並寫一筆 EventLog 寄送結果摘要。isMailEnabled 判斷由呼叫端負責（測試寄送不受其限制）。
    /// </summary>
    public async Task<DailyReportMailResult> SendAsync(
        DailyReportData data, DailyReportSettingModel setting,
        List<DailyReportRecipientModel> recipients, bool isTest)
    {
        var result = new DailyReportMailResult();
        var szTestPrefix = isTest ? "[測試] " : "";
        var emailSetting = _emailConfigService.LoadFromFile();

        var enabledRecipients = recipients.Where(r => r.isEnabled).ToList();
        if (enabledRecipients.Count == 0)
        {
            result.szDetail = $"{szTestPrefix}無啟用的收件人";
            await LogSummaryAsync(4 /* NoTarget */, result.szDetail);
            return result;
        }

        if (string.IsNullOrWhiteSpace(emailSetting.SmtpHost) || string.IsNullOrWhiteSpace(emailSetting.FromAddress))
        {
            result.nFail = enabledRecipients.Count;
            result.szDetail = $"{szTestPrefix}SMTP 未設定（SmtpHost / FromAddress 空白）";
            await LogSummaryAsync(2 /* AllFailed */, result.szDetail);
            return result;
        }

        var szSubject = _htmlBuilder.BuildSubject(data, isTest);
        var szBody = _htmlBuilder.Build(data, setting, isTest);
        var aFailedAddresses = new List<string>();

        try
        {
            using var client = new SmtpClient();
            client.Timeout = 15000;
            var socketOpts = emailSetting.UseSsl ? SecureSocketOptions.SslOnConnect
                : (emailSetting.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
            await client.ConnectAsync(emailSetting.SmtpHost, emailSetting.SmtpPort, socketOpts);
            if (!string.IsNullOrEmpty(emailSetting.Username))
                await client.AuthenticateAsync(emailSetting.Username, emailSetting.Password);

            foreach (var recipient in enabledRecipients)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress(emailSetting.FromDisplayName ?? "SCADA Engine", emailSetting.FromAddress));
                    message.To.Add(new MailboxAddress(recipient.szDisplayName ?? recipient.szEmailAddress, recipient.szEmailAddress));
                    message.Subject = szSubject;
                    message.Body = new TextPart("html") { Text = szBody };
                    await client.SendAsync(message);
                    result.nSuccess++;
                }
                catch (Exception ex)
                {
                    result.nFail++;
                    aFailedAddresses.Add(recipient.szEmailAddress);
                    _logger.LogWarning(ex, "日報寄送失敗 To={Email}", recipient.szEmailAddress);
                }
            }
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // 連線 / 認證層級失敗 → 未寄出的全算失敗
            result.nFail = enabledRecipients.Count - result.nSuccess;
            _logger.LogError(ex, "日報 SMTP 連線失敗 Host={Host}:{Port}", emailSetting.SmtpHost, emailSetting.SmtpPort);
            result.szDetail = $"{szTestPrefix}SMTP 連線失敗: {ex.Message}";
            await LogSummaryAsync(2 /* AllFailed */, result.szDetail);
            return result;
        }

        result.szDetail = $"{szTestPrefix}日報 {data.szReportDate}，收件人 {enabledRecipients.Count} 個，成功 {result.nSuccess}、失敗 {result.nFail}"
            + (aFailedAddresses.Count > 0 ? $"（失敗：{string.Join(", ", aFailedAddresses.Take(3))}{(aFailedAddresses.Count > 3 ? "…" : "")}）" : "");
        byte nNotifyStatus = result.nFail == 0 ? (byte)0 /* AllSent */
            : result.nSuccess > 0 ? (byte)1 /* PartialFailed */ : (byte)2 /* AllFailed */;
        await LogSummaryAsync(nNotifyStatus, result.szDetail);
        return result;
    }

    /// <summary>寄送結果寫 EventLog 摘要（格式對齊 Engine NotifyDeliveryLogger：EventType=3、Severity=3）</summary>
    private async Task LogSummaryAsync(byte nNotifyStatus, string szDetail)
    {
        try
        {
            if (string.IsNullOrEmpty(_szConnectionString))
                _szConnectionString = await _dbConfigService.GetConnectionStringAsync();
            using var conn = new SqlConnection(_szConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO EventLog (SID, EventType, Severity, Message, OccurredAt, NotifyChannel, NotifyStatus, NotifyDetail)
                VALUES (@szSID, 3, 3, @szMessage, GETDATE(), 'Email', @nNotifyStatus, @szDetail)",
                new
                {
                    szSID = "DailyReport",
                    szMessage = $"Email 日報: {Truncate(szDetail, 480)}",
                    nNotifyStatus,
                    szDetail = Truncate(szDetail, 500),
                });
        }
        catch (Exception ex)
        {
            // EventLog 摘要寫入失敗不影響寄送流程
            _logger.LogWarning(ex, "日報寄送結果 EventLog 寫入失敗");
        }
    }

    private static string Truncate(string sz, int nMax) => sz.Length <= nMax ? sz : sz[..nMax];
}
