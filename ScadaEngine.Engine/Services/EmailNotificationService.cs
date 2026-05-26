using System.Collections.Concurrent;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Email 通知服務 — 警報觸發 / 恢復時寄信給 EmailGroupRuleMap 對應的群組所有啟用收件人
/// 設計重點：
///   1. 路由：以警報 rule.Id 找出對應的群組；rule 未對應任何群組視為「全部群組都收」
///   2. Critical (severity=0) 永遠單獨送、繞過限流
///   3. 其他嚴重度走「每群組獨立」的 1 分鐘滑動視窗（預設 10 封/分鐘），超過進 buffer
///   4. 訊息文字依群組 Language 欄位透過 NotificationLocalizer 翻譯（HTML 模板）
///   5. 寄送完寫一筆通知摘要 EventLog（共用 NotifyDeliveryLogger）
///   6. _isInitialized 旗標：Engine 啟動還原舊警報時呼叫的 Notify 一律 skip
/// </summary>
public class EmailNotificationService : IDisposable
{
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly EmailGroupRepository _groupRepo;
    private readonly EmailSenderClient _senderClient;
    private readonly NotificationLocalizer _localizer;
    private readonly NotifyDeliveryLogger _deliveryLogger;

    private bool _isInitialized = false;
    private int _nRatePerMinute = 10;

    private readonly ConcurrentDictionary<int, GroupRateState> _rateStates = new();
    private readonly Timer _flushTimer;

    public EmailNotificationService(
        ILogger<EmailNotificationService> logger,
        EmailGroupRepository groupRepo,
        EmailSenderClient senderClient,
        NotificationLocalizer localizer,
        NotifyDeliveryLogger deliveryLogger)
    {
        _logger = logger;
        _groupRepo = groupRepo;
        _senderClient = senderClient;
        _localizer = localizer;
        _deliveryLogger = deliveryLogger;

        _flushTimer = new Timer(async _ => await FlushExpiredWindowsAsync(),
            null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task InitializeAsync(EmailSettingModel setting)
    {
        _senderClient.Initialize(setting);
        if (setting.RatePerMinute > 0) _nRatePerMinute = setting.RatePerMinute;

        _flushTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _isInitialized = true;
        _logger.LogInformation("Email 通知服務初始化完成 (啟用={Enabled}, 每分鐘上限={Rate})",
            _senderClient.IsEnabled, _nRatePerMinute);
        return Task.CompletedTask;
    }

    /// <summary>警報觸發時呼叫</summary>
    public Task NotifyAsync(NotifyContext ctx) => NotifyInternalAsync(ctx, isRecovery: false);

    /// <summary>警報恢復時呼叫</summary>
    public Task NotifyClearedAsync(NotifyContext ctx) => NotifyInternalAsync(ctx, isRecovery: true);

    private async Task NotifyInternalAsync(NotifyContext ctx, bool isRecovery)
    {
        if (!_isInitialized || !_senderClient.IsEnabled) return;

        try
        {
            var groups = await _groupRepo.GetEnabledGroupsAsync();
            if (groups.Count == 0)
            {
                await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Email,
                    NotifyDeliveryLogger.Status.NoTarget, "無啟用的 Email 群組", ctx.nRelatedEventId);
                return;
            }

            // 路由：rule.Id 對應的群組；如果群組沒設任何規則對應，視為「全部規則都收」
            var matched = groups.Where(g =>
                g.group.nMaxSeverity >= ctx.nSeverity
                && (g.ruleIds.Count == 0 || g.ruleIds.Contains(ctx.nAlarmRuleId))
            ).ToList();

            if (matched.Count == 0)
            {
                await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Email,
                    NotifyDeliveryLogger.Status.NoTarget,
                    $"無 Email 群組符合嚴重度 {ctx.nSeverity} / RuleId={ctx.nAlarmRuleId}", ctx.nRelatedEventId);
                return;
            }

            int nTotalRecipients = 0, nSuccess = 0, nFailed = 0;
            var failedAddrs = new List<string>();

            foreach (var gc in matched)
            {
                if (gc.recipients.Count == 0) continue;

                // Critical 不走 rate limit
                if (ctx.nSeverity == 0)
                {
                    var result = await SendToGroupAsync(gc, ctx, isRecovery);
                    nTotalRecipients += result.total;
                    nSuccess += result.success;
                    nFailed += result.failed;
                    failedAddrs.AddRange(result.failedAddrs);
                }
                else
                {
                    var (sentNow, total) = EnqueueOrSendCheck(gc, ctx, isRecovery);
                    if (sentNow)
                    {
                        var result = await SendToGroupAsync(gc, ctx, isRecovery);
                        nTotalRecipients += result.total;
                        nSuccess += result.success;
                        nFailed += result.failed;
                        failedAddrs.AddRange(result.failedAddrs);
                    }
                    // 若 buffer：FlushExpiredWindowsAsync 會發送 summary，這裡不額外處理
                }
            }

            await LogSummaryAsync(ctx, matched.Count, nTotalRecipients, nSuccess, nFailed, failedAddrs, isRecovery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email 通知派送發生例外: SID={SID}", ctx.szSID);
        }
    }

    /// <summary>
    /// 直接寄一封 mail 給特定地址（給「測試寄送」按鈕使用）
    /// </summary>
    public async Task<bool> SendTestAsync(string szToEmail, string? szDisplayName, string szLanguage, string szGroupLabel)
    {
        if (!_isInitialized || !_senderClient.IsEnabled) return false;
        var subject = _localizer.Format(szLanguage, "notify.subject.test", null);
        var args = new Dictionary<string, string?>
        {
            ["label"] = szGroupLabel,
            ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        var body = _localizer.Format(szLanguage, "notify.body.test.html", args);
        return await _senderClient.SendAsync(szToEmail, szDisplayName, subject, body);
    }

    private async Task<(int total, int success, int failed, List<string> failedAddrs)> SendToGroupAsync(
        EmailGroupCache gc, NotifyContext ctx, bool isRecovery)
    {
        var szLanguage = gc.group.szLanguage;
        var szSeverity = _localizer.SeverityLabel(szLanguage, ctx.nSeverity);
        var szIcon = _localizer.SeverityIcon(szLanguage, ctx.nSeverity);
        var szColor = _localizer.SeverityColor(szLanguage, ctx.nSeverity);
        var szDescription = _localizer.Format(szLanguage, ctx.szMessageKey, ctx.args);

        var subjectArgs = new Dictionary<string, string?>
        {
            ["severity"] = szSeverity,
            ["name"] = ctx.szName
        };
        var szSubject = _localizer.Format(szLanguage,
            isRecovery ? "notify.subject.cleared" : "notify.subject.triggered",
            subjectArgs);

        var bodyArgs = new Dictionary<string, string?>
        {
            ["icon"] = szIcon,
            ["severity"] = szSeverity,
            ["color"] = szColor,
            ["time"] = ctx.dtTime.ToString("yyyy-MM-dd HH:mm:ss"),
            ["name"] = ctx.szName,
            ["message"] = szDescription
        };
        var szBody = _localizer.Format(szLanguage,
            isRecovery ? "notify.body.cleared.html" : "notify.body.triggered.html",
            bodyArgs);

        int total = gc.recipients.Count;
        int success = 0, failed = 0;
        var failedAddrs = new List<string>();

        foreach (var r in gc.recipients)
        {
            var ok = await _senderClient.SendAsync(r.szEmailAddress, r.szDisplayName, szSubject, szBody);
            if (ok) success++;
            else { failed++; failedAddrs.Add(r.szEmailAddress); }
        }

        return (total, success, failed, failedAddrs);
    }

    /// <summary>檢查群組的 rate window；若可發即回 (true, total)，否則加入 buffer 回 (false, _)</summary>
    private (bool sentNow, int total) EnqueueOrSendCheck(EmailGroupCache gc, NotifyContext ctx, bool isRecovery)
    {
        var state = _rateStates.GetOrAdd(gc.group.nId, _ => new GroupRateState());
        lock (state.Lock)
        {
            if (DateTime.UtcNow - state.WindowStart >= TimeSpan.FromMinutes(1))
            {
                state.WindowStart = DateTime.UtcNow;
                state.Count = 0;
                state.Buffer.Clear();
            }

            if (state.Count < _nRatePerMinute)
            {
                state.Count++;
                return (true, gc.recipients.Count);
            }
            else
            {
                state.Buffer.Add(new BufferedNotify
                {
                    nSeverity = ctx.nSeverity,
                    szSID = ctx.szSID,
                    szName = ctx.szName,
                    szMessageKey = ctx.szMessageKey,
                    args = new Dictionary<string, string?>(ctx.args),
                    dtTime = ctx.dtTime,
                    isRecovery = isRecovery,
                    groupId = gc.group.nId
                });
                return (false, 0);
            }
        }
    }

    private async Task FlushExpiredWindowsAsync()
    {
        if (!_isInitialized || !_senderClient.IsEnabled) return;

        try
        {
            var groups = await _groupRepo.GetEnabledGroupsAsync();
            var groupsById = groups.ToDictionary(g => g.group.nId);

            foreach (var kv in _rateStates)
            {
                List<BufferedNotify>? snapshot = null;
                DateTime windowStart;
                var state = kv.Value;
                lock (state.Lock)
                {
                    if (DateTime.UtcNow - state.WindowStart < TimeSpan.FromMinutes(1))
                        continue;
                    if (state.Buffer.Count > 0)
                        snapshot = new List<BufferedNotify>(state.Buffer);
                    windowStart = state.WindowStart;
                    state.WindowStart = DateTime.UtcNow;
                    state.Count = 0;
                    state.Buffer.Clear();
                }

                if (snapshot == null || snapshot.Count == 0) continue;
                if (!groupsById.TryGetValue(kv.Key, out var gc)) continue;

                await SendSummaryAsync(gc, snapshot, windowStart);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email FlushExpiredWindows 發生例外");
        }
    }

    private async Task SendSummaryAsync(EmailGroupCache gc, List<BufferedNotify> messages, DateTime dtWindowStart)
    {
        var szLanguage = gc.group.szLanguage;
        var dtWindowEnd = dtWindowStart.Add(TimeSpan.FromMinutes(1));

        var subjectArgs = new Dictionary<string, string?> { ["count"] = messages.Count.ToString() };
        var szSubject = _localizer.Format(szLanguage, "notify.subject.summary", subjectArgs);

        // 用 Line 摘要模板（純文字）轉成 HTML pre 顯示
        int nCritical = messages.Count(m => m.nSeverity == 0);
        int nHigh = messages.Count(m => m.nSeverity == 1);
        int nMedium = messages.Count(m => m.nSeverity == 2);
        int nLow = messages.Count(m => m.nSeverity == 3);
        var sbRecent = new System.Text.StringBuilder();
        foreach (var m in messages.OrderByDescending(m => m.dtTime).Take(5))
        {
            var szSev = _localizer.SeverityLabel(szLanguage, m.nSeverity);
            var szDesc = _localizer.Format(szLanguage, m.szMessageKey, m.args);
            sbRecent.AppendLine($"• {m.dtTime:HH:mm:ss} [{szSev}] {szDesc}");
        }
        var bodyArgs = new Dictionary<string, string?>
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
        var szBody = "<pre style='font-family:monospace;'>"
            + System.Net.WebUtility.HtmlEncode(
                _localizer.Format(szLanguage, "notify.body.summary.line", bodyArgs))
            + "</pre>";

        int nSuccess = 0, nFailed = 0;
        var failedAddrs = new List<string>();
        foreach (var r in gc.recipients)
        {
            var ok = await _senderClient.SendAsync(r.szEmailAddress, r.szDisplayName, szSubject, szBody);
            if (ok) nSuccess++;
            else { nFailed++; failedAddrs.Add(r.szEmailAddress); }
        }

        var status = (nFailed == 0) ? NotifyDeliveryLogger.Status.AllSent
                   : (nSuccess == 0) ? NotifyDeliveryLogger.Status.AllFailed
                   : NotifyDeliveryLogger.Status.PartialFailed;
        var szDetail = $"[摘要] 群組 {gc.group.szLabel}，{messages.Count} 筆合併 / 收件人 {gc.recipients.Count} 個，"
                     + $"成功 {nSuccess}、失敗 {nFailed}";
        await _deliveryLogger.LogAsync("_summary", NotifyDeliveryLogger.Channel.Email, status, szDetail);
    }

    private async Task LogSummaryAsync(NotifyContext ctx, int nGroupCount, int nTotalRecipients,
        int nSuccess, int nFailed, List<string> failedAddrs, bool isRecovery)
    {
        if (nTotalRecipients == 0)
        {
            // 全部進 buffer
            await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Email,
                NotifyDeliveryLogger.Status.RateLimited,
                $"{nGroupCount} 群組超過 rate limit，已加入合併摘要 buffer", ctx.nRelatedEventId);
            return;
        }

        var status = (nFailed == 0) ? NotifyDeliveryLogger.Status.AllSent
                   : (nSuccess == 0) ? NotifyDeliveryLogger.Status.AllFailed
                   : NotifyDeliveryLogger.Status.PartialFailed;
        var szPrefix = isRecovery ? "[恢復] " : string.Empty;
        var szDetail = nFailed == 0
            ? $"{szPrefix}群組 {nGroupCount} 個 / 收件人 {nTotalRecipients} 個，全部成功"
            : $"{szPrefix}群組 {nGroupCount} 個 / 收件人 {nTotalRecipients} 個，成功 {nSuccess}、失敗 {nFailed}"
              + $"（失敗：{string.Join(",", failedAddrs.Take(5))}{(failedAddrs.Count > 5 ? "..." : "")}）";
        await _deliveryLogger.LogAsync(ctx.szSID, NotifyDeliveryLogger.Channel.Email, status, szDetail, ctx.nRelatedEventId);
    }

    public void Dispose() => _flushTimer?.Dispose();

    private class GroupRateState
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
        public List<BufferedNotify> Buffer { get; } = new();
        public object Lock { get; } = new();
    }

    private class BufferedNotify
    {
        public byte nSeverity;
        public string szSID = string.Empty;
        public string szName = string.Empty;
        public string szMessageKey = string.Empty;
        public IDictionary<string, string?> args = new Dictionary<string, string?>();
        public DateTime dtTime;
        public bool isRecovery;
        public int groupId;
    }
}
