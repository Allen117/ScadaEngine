using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Line Messaging API 推播服務 — 警報觸發時發送通知
/// 設計重點：
///   1. Critical (severity=0) 永遠單獨送、繞過限流
///   2. 其他嚴重度走「每群組獨立」的 1 分鐘滑動視窗（預設 10 則/分鐘），超過進 buffer
///   3. 視窗結束時若有 buffer，發送嚴重度計數摘要 + 最近 5 則明細
///   4. Line API 5xx / 網路錯誤最多重試 1 次（4xx 不重試）
///   5. _isInitialized 旗標：Engine 啟動還原舊警報時呼叫的 Notify 一律 skip
/// </summary>
public class LineNotificationService : IDisposable
{
    private const string c_szLinePushUrl = "https://api.line.me/v2/bot/message/push";

    private readonly ILogger<LineNotificationService> _logger;
    private readonly LineNotifyTargetRepository _targetRepo;
    private readonly IHttpClientFactory _httpClientFactory;

    private LineSettingModel _setting = new();
    private bool _isInitialized = false;

    /// <summary>每群組各自的滑動視窗狀態</summary>
    private readonly ConcurrentDictionary<string, GroupRateState> _rateStates = new();

    /// <summary>定時掃描所有群組視窗，遇到過期且有 buffer 的就 flush 摘要</summary>
    private readonly Timer _flushTimer;

    public LineNotificationService(
        ILogger<LineNotificationService> logger,
        LineNotifyTargetRepository targetRepo,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _targetRepo = targetRepo;
        _httpClientFactory = httpClientFactory;

        // 計時器先不啟動，等 InitializeAsync 完成後再 Change
        _flushTimer = new Timer(async _ => await FlushExpiredWindowsAsync(),
            null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 初始化：載入設定、啟動 flush 計時器
    /// 必須在 AlarmMonitorService.InitializeAsync 完成後呼叫，避免還原舊警報時誤發
    /// </summary>
    public Task InitializeAsync(LineSettingModel setting)
    {
        _setting = setting ?? new LineSettingModel();

        if (string.IsNullOrWhiteSpace(_setting.ChannelAccessToken)
            || _setting.ChannelAccessToken == "PASTE_YOUR_LINE_CHANNEL_ACCESS_TOKEN_HERE")
        {
            _logger.LogWarning("Line ChannelAccessToken 未設定，Line 通知將不會發送（警報流程不受影響）");
            _setting.EnableNotification = false;
        }

        if (_setting.RatePerMinute <= 0)
            _setting.RatePerMinute = 10;

        // 每 5 秒檢查一次過期視窗
        _flushTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _isInitialized = true;
        _logger.LogInformation("Line 通知服務初始化完成 (啟用={Enabled}, 每分鐘上限={Rate})",
            _setting.EnableNotification, _setting.RatePerMinute);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 警報觸發時呼叫 — 對所有符合「MaxSeverity >= 此警報嚴重度」的群組推播
    /// </summary>
    public async Task NotifyAsync(byte nSeverity, string szSID, string szMessage, DateTime dtTriggeredAt)
    {
        if (!_isInitialized || !_setting.EnableNotification)
            return;

        try
        {
            var targets = await _targetRepo.GetEnabledTargetsAsync();
            if (targets.Count == 0)
                return;

            // MaxSeverity 的語意：0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收
            // 嚴重度越「緊急」數值越小，所以 target 收到該警報的條件：target.nMaxSeverity >= nSeverity
            var matched = targets.Where(t => t.nMaxSeverity >= nSeverity).ToList();
            if (matched.Count == 0)
                return;

            var msg = new NotifyMessage
            {
                nSeverity = nSeverity,
                szSID = szSID,
                szMessage = szMessage,
                dtTriggeredAt = dtTriggeredAt
            };

            foreach (var target in matched)
            {
                if (nSeverity == 0)
                {
                    // Critical：繞過限流，直接送
                    var szText = FormatSingleAlert(msg);
                    _ = PushWithRetryAsync(target.szGroupId, szText);
                }
                else
                {
                    await EnqueueOrSendAsync(target.szGroupId, msg);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Line 通知發送流程發生例外: SID={SID}", szSID);
        }
    }

    private Task EnqueueOrSendAsync(string szGroupId, NotifyMessage msg)
    {
        var state = _rateStates.GetOrAdd(szGroupId, _ => new GroupRateState());

        bool sendNow;
        bool flushSummary = false;
        List<NotifyMessage>? snapshot = null;
        DateTime windowStart;

        lock (state.Lock)
        {
            // 檢查視窗是否已過期
            if (DateTime.UtcNow - state.WindowStart >= TimeSpan.FromMinutes(1))
            {
                if (state.Buffer.Count > 0)
                {
                    snapshot = new List<NotifyMessage>(state.Buffer);
                    flushSummary = true;
                }
                state.WindowStart = DateTime.UtcNow;
                state.Count = 0;
                state.Buffer.Clear();
            }

            if (state.Count < _setting.RatePerMinute)
            {
                state.Count++;
                sendNow = true;
            }
            else
            {
                state.Buffer.Add(msg);
                sendNow = false;
            }
            windowStart = state.WindowStart;
        }

        if (flushSummary && snapshot != null)
        {
            var szSummary = FormatSummary(snapshot, windowStart);
            _ = PushWithRetryAsync(szGroupId, szSummary);
        }

        if (sendNow)
        {
            var szText = FormatSingleAlert(msg);
            _ = PushWithRetryAsync(szGroupId, szText);
        }

        return Task.CompletedTask;
    }

    private async Task FlushExpiredWindowsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            foreach (var kv in _rateStates)
            {
                var szGroupId = kv.Key;
                var state = kv.Value;

                List<NotifyMessage>? snapshot = null;
                DateTime windowStart;

                lock (state.Lock)
                {
                    if (DateTime.UtcNow - state.WindowStart < TimeSpan.FromMinutes(1))
                        continue;

                    if (state.Buffer.Count > 0)
                    {
                        snapshot = new List<NotifyMessage>(state.Buffer);
                    }
                    windowStart = state.WindowStart;
                    state.WindowStart = DateTime.UtcNow;
                    state.Count = 0;
                    state.Buffer.Clear();
                }

                if (snapshot != null)
                {
                    var szSummary = FormatSummary(snapshot, windowStart);
                    await PushWithRetryAsync(szGroupId, szSummary);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FlushExpiredWindows 發生例外");
        }
    }

    /// <summary>
    /// 對外 API：直接推播一則文字訊息（給「測試發送」按鈕使用）
    /// </summary>
    public Task<bool> PushTextAsync(string szGroupId, string szText)
    {
        if (string.IsNullOrWhiteSpace(_setting.ChannelAccessToken))
        {
            _logger.LogWarning("Line Token 未設定，無法發送測試訊息");
            return Task.FromResult(false);
        }
        return PushWithRetryAsync(szGroupId, szText);
    }

    private async Task<bool> PushWithRetryAsync(string szGroupId, string szText)
    {
        if (string.IsNullOrWhiteSpace(_setting.ChannelAccessToken))
            return false;

        for (int nAttempt = 1; nAttempt <= 2; nAttempt++)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient("Line");
                client.Timeout = TimeSpan.FromSeconds(10);

                var payload = new
                {
                    to = szGroupId,
                    messages = new[] { new { type = "text", text = szText } }
                };
                var szJson = JsonSerializer.Serialize(payload);

                using var req = new HttpRequestMessage(HttpMethod.Post, c_szLinePushUrl)
                {
                    Content = new StringContent(szJson, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _setting.ChannelAccessToken);

                using var resp = await client.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Line 推播成功: GroupId={Group}, Attempt={Attempt}", szGroupId, nAttempt);
                    return true;
                }

                var szBody = await SafeReadBodyAsync(resp);
                int nStatus = (int)resp.StatusCode;

                // 4xx 不重試
                if (nStatus >= 400 && nStatus < 500)
                {
                    _logger.LogError("Line 推播被 API 拒絕（不重試）: GroupId={Group}, Status={Status}, Body={Body}",
                        szGroupId, nStatus, szBody);
                    return false;
                }

                // 5xx 重試（若還有機會）
                _logger.LogWarning("Line 推播失敗（5xx）: GroupId={Group}, Status={Status}, Attempt={Attempt}",
                    szGroupId, nStatus, nAttempt);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Line 推播網路錯誤: GroupId={Group}, Attempt={Attempt}", szGroupId, nAttempt);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Line 推播逾時: GroupId={Group}, Attempt={Attempt}", szGroupId, nAttempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Line 推播未預期例外: GroupId={Group}", szGroupId);
                return false;
            }

            if (nAttempt == 1)
                await Task.Delay(TimeSpan.FromSeconds(1));
        }

        _logger.LogError("Line 推播最終失敗（已重試 1 次）: GroupId={Group}", szGroupId);
        return false;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp)
    {
        try { return await resp.Content.ReadAsStringAsync(); }
        catch { return "<unable to read body>"; }
    }

    // ── 訊息格式化 ──

    private static string FormatSingleAlert(NotifyMessage msg)
    {
        string szIcon = SeverityIcon(msg.nSeverity);
        string szLabel = SeverityLabel(msg.nSeverity);
        var sb = new StringBuilder();
        sb.AppendLine($"{szIcon} 警報觸發 [{szLabel}]");
        sb.AppendLine($"時間: {msg.dtTriggeredAt:yyyy-MM-dd HH:mm:ss}");
        sb.Append($"描述: {msg.szMessage}");
        return sb.ToString();
    }

    private static string FormatSummary(List<NotifyMessage> messages, DateTime dtWindowStart)
    {
        var dtWindowEnd = dtWindowStart.Add(TimeSpan.FromMinutes(1));
        int nCritical = messages.Count(m => m.nSeverity == 0);
        int nHigh = messages.Count(m => m.nSeverity == 1);
        int nMedium = messages.Count(m => m.nSeverity == 2);
        int nLow = messages.Count(m => m.nSeverity == 3);

        var sb = new StringBuilder();
        sb.AppendLine($"⚠️ 警報摘要 (近 1 分鐘共 {messages.Count} 則)");
        sb.AppendLine($"視窗: {dtWindowStart.ToLocalTime():HH:mm:ss} – {dtWindowEnd.ToLocalTime():HH:mm:ss}");
        sb.AppendLine($"緊急 {nCritical} | 高 {nHigh} | 中 {nMedium} | 低 {nLow}");
        sb.AppendLine();
        sb.AppendLine("最近 5 則：");

        var recent = messages
            .OrderByDescending(m => m.dtTriggeredAt)
            .Take(5)
            .ToList();

        foreach (var m in recent)
        {
            sb.AppendLine($"• {m.dtTriggeredAt:HH:mm:ss} [{SeverityLabel(m.nSeverity)}] {m.szMessage} ({m.szSID})");
        }
        return sb.ToString().TrimEnd();
    }

    private static string SeverityLabel(byte n) => n switch
    {
        0 => "緊急",
        1 => "高",
        2 => "中",
        3 => "低",
        _ => "未知"
    };

    private static string SeverityIcon(byte n) => n switch
    {
        0 => "🚨", // 🚨
        1 => "⚠️",  // ⚠
        2 => "🔶", // 🔶
        3 => "ℹ️",  // ℹ
        _ => "❓"          // ❓
    };

    public void Dispose()
    {
        _flushTimer?.Dispose();
    }

    // ── 內部狀態 ──

    private class GroupRateState
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
        public List<NotifyMessage> Buffer { get; } = new();
        public object Lock { get; } = new();
    }

    private class NotifyMessage
    {
        public byte nSeverity;
        public string szSID = string.Empty;
        public string szMessage = string.Empty;
        public DateTime dtTriggeredAt;
    }
}
