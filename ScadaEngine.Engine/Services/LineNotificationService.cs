using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Line Messaging API 推播服務 — 警報觸發 / 恢復時發送通知
/// 設計重點：
///   1. Critical (severity=0) 永遠單獨送、繞過限流
///   2. 其他嚴重度走「每群組獨立」的 1 分鐘滑動視窗（預設 10 則/分鐘），超過進 buffer
///   3. 視窗結束時若有 buffer，發送嚴重度計數摘要 + 最近 5 則明細
///   4. Line API 5xx / 網路錯誤最多重試 1 次（4xx 不重試）
///   5. 訊息文字依群組 Language 欄位透過 NotificationLocalizer 翻譯（zh-TW / en）
///   6. 寄送完寫一筆通知摘要 EventLog（共用 NotifyDeliveryLogger）
///   7. _isInitialized 旗標：Engine 啟動還原舊警報時呼叫的 Notify 一律 skip
/// </summary>
public class LineNotificationService : IDisposable
{
    private const string c_szLinePushUrl = "https://api.line.me/v2/bot/message/push";

    private readonly ILogger<LineNotificationService> _logger;
    private readonly LineNotifyTargetRepository _targetRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotificationLocalizer _localizer;
    private readonly NotifyDeliveryLogger _deliveryLogger;

    private LineSettingModel _setting = new();
    private bool _isInitialized = false;

    /// <summary>每群組各自的滑動視窗狀態</summary>
    private readonly ConcurrentDictionary<string, GroupRateState> _rateStates = new();

    /// <summary>定時掃描所有群組視窗，遇到過期且有 buffer 的就 flush 摘要</summary>
    private readonly Timer _flushTimer;

    public LineNotificationService(
        ILogger<LineNotificationService> logger,
        LineNotifyTargetRepository targetRepo,
        IHttpClientFactory httpClientFactory,
        NotificationLocalizer localizer,
        NotifyDeliveryLogger deliveryLogger)
    {
        _logger = logger;
        _targetRepo = targetRepo;
        _httpClientFactory = httpClientFactory;
        _localizer = localizer;
        _deliveryLogger = deliveryLogger;

        _flushTimer = new Timer(async _ => await FlushExpiredWindowsAsync(),
            null, Timeout.Infinite, Timeout.Infinite);
    }

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

        _flushTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _isInitialized = true;
        _logger.LogInformation("Line 通知服務初始化完成 (啟用={Enabled}, 每分鐘上限={Rate})",
            _setting.EnableNotification, _setting.RatePerMinute);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 警報觸發時呼叫 — 對所有符合「MaxSeverity >= 此警報嚴重度」的群組推播
    /// </summary>
    public async Task NotifyAsync(NotifyContext ctx)
    {
        if (!_isInitialized || !_setting.EnableNotification)
            return;

        try
        {
            var targets = await _targetRepo.GetEnabledTargetsAsync();
            if (targets.Count == 0)
            {
                await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Line,
                    NotifyDeliveryLogger.Status.NoTarget, "無啟用的 Line 群組", ctx.nRelatedEventId);
                return;
            }

            var matched = targets.Where(t => t.nMaxSeverity >= ctx.nSeverity).ToList();
            if (matched.Count == 0)
            {
                await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Line,
                    NotifyDeliveryLogger.Status.NoTarget,
                    $"無群組符合嚴重度 {ctx.nSeverity}", ctx.nRelatedEventId);
                return;
            }

            int nSuccess = 0, nFailed = 0;
            var failedLabels = new List<string>();

            foreach (var target in matched)
            {
                bool isOk;
                if (ctx.nSeverity == 0)
                {
                    var szText = FormatSingleAlert(ctx, target.szLanguage, isRecovery: false);
                    isOk = await PushWithRetryAsync(target.szGroupId, szText);
                }
                else
                {
                    isOk = await EnqueueOrSendAsync(target, ctx, isRecovery: false);
                }

                if (isOk) nSuccess++;
                else { nFailed++; failedLabels.Add(target.szLabel); }
            }

            await LogSummaryAsync(ctx, matched.Count, nSuccess, nFailed, failedLabels, isRecovery: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Line 通知發送流程發生例外: SID={SID}", ctx.szSID);
        }
    }

    /// <summary>
    /// 警報恢復時呼叫 — 對符合嚴重度的群組推播恢復通知（不走 rate limit）
    /// </summary>
    public async Task NotifyClearedAsync(NotifyContext ctx)
    {
        if (!_isInitialized || !_setting.EnableNotification) return;

        try
        {
            var targets = await _targetRepo.GetEnabledTargetsAsync();
            var matched = targets.Where(t => t.nMaxSeverity >= ctx.nSeverity).ToList();
            if (matched.Count == 0)
            {
                await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Line,
                    NotifyDeliveryLogger.Status.NoTarget, "無群組符合嚴重度（恢復通知）", ctx.nRelatedEventId);
                return;
            }

            int nSuccess = 0, nFailed = 0;
            var failedLabels = new List<string>();
            foreach (var target in matched)
            {
                var szText = FormatSingleAlert(ctx, target.szLanguage, isRecovery: true);
                var isOk = await PushWithRetryAsync(target.szGroupId, szText);
                if (isOk) nSuccess++;
                else { nFailed++; failedLabels.Add(target.szLabel); }
            }

            await LogSummaryAsync(ctx, matched.Count, nSuccess, nFailed, failedLabels, isRecovery: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Line 恢復通知發送流程發生例外: SID={SID}", ctx.szSID);
        }
    }

    private async Task<bool> EnqueueOrSendAsync(LineNotifyTargetModel target, NotifyContext ctx, bool isRecovery)
    {
        var state = _rateStates.GetOrAdd(target.szGroupId, _ => new GroupRateState());

        bool sendNow;
        bool flushSummary = false;
        List<BufferedMessage>? snapshot = null;
        DateTime windowStart;

        lock (state.Lock)
        {
            if (DateTime.UtcNow - state.WindowStart >= TimeSpan.FromMinutes(1))
            {
                if (state.Buffer.Count > 0)
                {
                    snapshot = new List<BufferedMessage>(state.Buffer);
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
                state.Buffer.Add(new BufferedMessage
                {
                    nSeverity = ctx.nSeverity,
                    szSID = ctx.szSID,
                    szName = ctx.szName,
                    szMessageKey = ctx.szMessageKey,
                    args = new Dictionary<string, string?>(ctx.args),
                    dtTime = ctx.dtTime,
                    szLanguage = target.szLanguage
                });
                sendNow = false;
            }
            windowStart = state.WindowStart;
        }

        if (flushSummary && snapshot != null)
        {
            var szSummary = FormatSummary(snapshot, windowStart, target.szLanguage);
            _ = PushWithRetryAsync(target.szGroupId, szSummary);
        }

        if (sendNow)
        {
            var szText = FormatSingleAlert(ctx, target.szLanguage, isRecovery);
            return await PushWithRetryAsync(target.szGroupId, szText);
        }
        return true; // 已 buffer，視為「將會發送」
    }

    private async Task FlushExpiredWindowsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            // 需要在解鎖後查 target 取得 Language，所以分兩步：先快照
            var pendingFlushes = new List<(string GroupId, List<BufferedMessage> Snapshot, DateTime WindowStart)>();

            foreach (var kv in _rateStates)
            {
                var state = kv.Value;
                List<BufferedMessage>? snapshot = null;
                DateTime windowStart;
                lock (state.Lock)
                {
                    if (DateTime.UtcNow - state.WindowStart < TimeSpan.FromMinutes(1))
                        continue;
                    if (state.Buffer.Count > 0)
                        snapshot = new List<BufferedMessage>(state.Buffer);
                    windowStart = state.WindowStart;
                    state.WindowStart = DateTime.UtcNow;
                    state.Count = 0;
                    state.Buffer.Clear();
                }
                if (snapshot != null)
                    pendingFlushes.Add((kv.Key, snapshot, windowStart));
            }

            foreach (var (szGroupId, snapshot, windowStart) in pendingFlushes)
            {
                // 用 buffer 內第一筆的 language 作為摘要語系
                var szLanguage = snapshot[0].szLanguage;
                var szSummary = FormatSummary(snapshot, windowStart, szLanguage);
                await PushWithRetryAsync(szGroupId, szSummary);
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

                if (nStatus >= 400 && nStatus < 500)
                {
                    _logger.LogError("Line 推播被 API 拒絕（不重試）: GroupId={Group}, Status={Status}, Body={Body}",
                        szGroupId, nStatus, szBody);
                    return false;
                }

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

    // ── 訊息格式化（依群組 Language）──

    private string FormatSingleAlert(NotifyContext ctx, string szLanguage, bool isRecovery)
    {
        var szIcon = _localizer.SeverityIcon(szLanguage, ctx.nSeverity);
        var szSeverity = _localizer.SeverityLabel(szLanguage, ctx.nSeverity);
        var szDescription = _localizer.Format(szLanguage, ctx.szMessageKey, ctx.args);

        var args = new Dictionary<string, string?>
        {
            ["icon"] = szIcon,
            ["severity"] = szSeverity,
            ["time"] = ctx.dtTime.ToString("yyyy-MM-dd HH:mm:ss"),
            ["message"] = szDescription
        };

        var szKey = isRecovery ? "notify.body.cleared.line" : "notify.body.triggered.line";
        return _localizer.Format(szLanguage, szKey, args);
    }

    private string FormatSummary(List<BufferedMessage> messages, DateTime dtWindowStart, string szLanguage)
    {
        var dtWindowEnd = dtWindowStart.Add(TimeSpan.FromMinutes(1));
        int nCritical = messages.Count(m => m.nSeverity == 0);
        int nHigh = messages.Count(m => m.nSeverity == 1);
        int nMedium = messages.Count(m => m.nSeverity == 2);
        int nLow = messages.Count(m => m.nSeverity == 3);

        var sbRecent = new StringBuilder();
        var recent = messages
            .OrderByDescending(m => m.dtTime)
            .Take(5)
            .ToList();
        foreach (var m in recent)
        {
            var szSev = _localizer.SeverityLabel(szLanguage, m.nSeverity);
            var szDesc = _localizer.Format(szLanguage, m.szMessageKey, m.args);
            sbRecent.AppendLine($"• {m.dtTime:HH:mm:ss} [{szSev}] {szDesc}");
        }

        var args = new Dictionary<string, string?>
        {
            ["count"] = messages.Count.ToString(),
            ["windowStart"] = dtWindowStart.ToLocalTime().ToString("HH:mm:ss"),
            ["windowEnd"] = dtWindowEnd.ToLocalTime().ToString("HH:mm:ss"),
            ["critical"] = nCritical.ToString(),
            ["high"] = nHigh.ToString(),
            ["medium"] = nMedium.ToString(),
            ["low"] = nLow.ToString(),
            ["recent"] = sbRecent.ToString().TrimEnd()
        };
        return _localizer.Format(szLanguage, "notify.body.summary.line", args);
    }

    private async Task LogSummaryAsync(NotifyContext ctx, int nGroupCount, int nSuccess, int nFailed,
        List<string> failedLabels, bool isRecovery)
    {
        NotifyDeliveryLogger.Status status;
        if (nFailed == 0) status = NotifyDeliveryLogger.Status.AllSent;
        else if (nSuccess == 0) status = NotifyDeliveryLogger.Status.AllFailed;
        else status = NotifyDeliveryLogger.Status.PartialFailed;

        string szPrefix = isRecovery ? "[恢復] " : string.Empty;
        string szDetail = nFailed == 0
            ? $"{szPrefix}群組 {nGroupCount} 個，全部成功"
            : $"{szPrefix}群組 {nGroupCount} 個，成功 {nSuccess}、失敗 {nFailed}（失敗：{string.Join(",", failedLabels)}）";

        await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Line, status, szDetail, ctx.nRelatedEventId);
    }

    public void Dispose() => _flushTimer?.Dispose();

    // ── 內部狀態 ──

    private class GroupRateState
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
        public List<BufferedMessage> Buffer { get; } = new();
        public object Lock { get; } = new();
    }

    private class BufferedMessage
    {
        public byte nSeverity;
        public string szSID = string.Empty;
        public string szName = string.Empty;
        public string szMessageKey = string.Empty;
        public IDictionary<string, string?> args = new Dictionary<string, string?>();
        public DateTime dtTime;
        public string szLanguage = "zh-TW";
    }
}
