using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 通知寄送結果統一摘要寫入器 — Line / Email 共用
/// 一次警報觸發 + Email 送 N 群組 M 收件人 + Line 送 K 群組 → EventLog 寫 3 筆：
///   1 筆 alarm (EventType=0)，1 筆 Email 摘要 (EventType=3)，1 筆 Line 摘要 (EventType=3)
/// 個別收件人失敗的明細走 Serilog（不展開到 EventLog 避免高頻警報下表脹太快）
/// </summary>
public class NotifyDeliveryLogger
{
    public enum Channel { Email, Line }

    public enum Status : byte
    {
        AllSent = 0,
        PartialFailed = 1,
        AllFailed = 2,
        RateLimited = 3,
        NoTarget = 4,
        Disabled = 5
    }

    private readonly ILogger<NotifyDeliveryLogger> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public NotifyDeliveryLogger(ILogger<NotifyDeliveryLogger> logger, DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 寫入一筆通知摘要 EventLog（EventType=3 資訊、Severity=3 低）。
    /// </summary>
    /// <param name="szSID">關聯到的點位 SID（沿用觸發警報的 SID 便於查詢）</param>
    /// <param name="channel">通道：Email / Line</param>
    /// <param name="status">寄送狀態</param>
    /// <param name="szDetail">人類可讀摘要，例如 "群組 2 個 / 收件人 6 個，成功 5、失敗 1"</param>
    /// <param name="nRelatedEventId">關聯回觸發的 alarm EventLog.Id；非 alarm 觸發（測試寄送等）填 null</param>
    public async Task LogAsync(string szSID, Channel channel, Status status, string szDetail, long? nRelatedEventId = null)
    {
        try
        {
            await EnsureConnectionStringAsync();
            const string szSql = @"
                INSERT INTO EventLog
                    (SID, EventType, Severity, Message, OccurredAt,
                     NotifyChannel, NotifyStatus, NotifyDetail, NotifyRelatedEventId)
                VALUES
                    (@SID, 3, 3, @Message, GETDATE(),
                     @NotifyChannel, @NotifyStatus, @NotifyDetail, @NotifyRelatedEventId)";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(szSql, new
            {
                SID = string.IsNullOrEmpty(szSID) ? "_system" : szSID,
                Message = $"{channel} 通知: {szDetail}",
                NotifyChannel = channel.ToString(),
                NotifyStatus = (byte)status,
                NotifyDetail = TruncateToLength(szDetail, 500),
                NotifyRelatedEventId = nRelatedEventId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入通知摘要 EventLog 失敗: Channel={Channel}, SID={SID}", channel, szSID);
        }
    }

    private static string TruncateToLength(string szText, int nMax)
    {
        if (string.IsNullOrEmpty(szText)) return string.Empty;
        return szText.Length <= nMax ? szText : szText.Substring(0, nMax - 3) + "...";
    }

    private async Task EnsureConnectionStringAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
    }
}
