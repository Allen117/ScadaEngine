using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Realtime.Controllers;

/// <summary>
/// 提供未恢復警報清單給 Realtime 頁面 Active Alarm Panel
/// </summary>
[Route("Realtime")]
public class ActiveAlarmController : Controller
{
    private readonly ILogger<ActiveAlarmController> _logger;
    private readonly MqttAlarmSubscriberService _alarmSubscriber;
    private readonly AlarmMessageLocalizer _alarmLocalizer;

    public ActiveAlarmController(
        ILogger<ActiveAlarmController> logger,
        MqttAlarmSubscriberService alarmSubscriber,
        AlarmMessageLocalizer alarmLocalizer)
    {
        _logger = logger;
        _alarmSubscriber = alarmSubscriber;
        _alarmLocalizer = alarmLocalizer;
    }

    /// <summary>
    /// GET /Realtime/ActiveAlarms — 回傳排序後（嚴重度升序、時間倒序）的未恢復警報
    /// </summary>
    [HttpGet("ActiveAlarms")]
    public IActionResult GetActiveAlarms()
    {
        try
        {
            var list = _alarmSubscriber.GetActiveAlarms();
            return Json(new
            {
                success = true,
                isConnected = _alarmSubscriber.IsConnected,
                count = list.Count,
                data = list.Select(x => new
                {
                    sid = x.szSID,
                    type = x.szType,
                    severity = x.nSeverity,
                    severityLabel = x.SeverityLabel,
                    severityColor = x.SeverityColor,
                    // 依當前 culture 翻譯訊息；舊資料 messageKey 為 null 時 fallback 用 szMessage
                    message = _alarmLocalizer.Localize(x.szMessageKey, x.szMessageArgs, x.szMessage),
                    triggerValue = x.dTriggerValue,
                    thresholdValue = x.dThresholdValue,
                    occurredAt = x.dtOccurredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    occurredAtMs = new DateTimeOffset(x.dtOccurredAt).ToUnixTimeMilliseconds(),
                    isAcknowledged = x.isAcknowledged,
                    acknowledgedBy = x.szAcknowledgedBy
                }).ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得未恢復警報清單失敗");
            return Json(new { success = false, error = ex.Message });
        }
    }

    public class AcknowledgeRequest
    {
        public string sid { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

    /// <summary>
    /// POST /Realtime/AcknowledgeAlarm — 將指定警報標記為已確認，寫回 DB 並更新快取
    /// </summary>
    [Authorize]
    [HttpPost("AcknowledgeAlarm")]
    public async Task<IActionResult> AcknowledgeAlarm([FromBody] AcknowledgeRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.sid) || string.IsNullOrWhiteSpace(req.type))
            return BadRequest(new { success = false, error = "sid 與 type 不可為空" });

        var szUser = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(szUser))
            return Unauthorized(new { success = false, error = "未登入或無法取得使用者帳號" });

        var ok = await _alarmSubscriber.AcknowledgeAsync(req.sid, req.type, szUser);
        if (!ok)
            return StatusCode(409, new { success = false, error = "查無對應未確認警報或更新失敗" });

        return Json(new { success = true, acknowledgedBy = szUser });
    }
}
