using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.EventLog.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.EventLog.Controllers
{
    [Authorize]
    public class EventLogController : Controller
    {
        private readonly ILogger<EventLogController> _logger;
        private readonly EventLogService _eventLogService;
        private readonly IStringLocalizer<EventLogController> _l;
        private readonly AlarmMessageLocalizer _alarmLocalizer;

        public EventLogController(
            ILogger<EventLogController> logger,
            EventLogService eventLogService,
            IStringLocalizer<EventLogController> localizer,
            AlarmMessageLocalizer alarmLocalizer)
        {
            _logger = logger;
            _eventLogService = eventLogService;
            _l = localizer;
            _alarmLocalizer = alarmLocalizer;
        }

        /// <summary>
        /// GET /EventLog — 事件記錄查詢頁面
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            var viewModel = new EventLogQueryViewModel();
            return View(viewModel);
        }

        /// <summary>
        /// GET /api/eventlog/query — AJAX 查詢事件記錄
        /// </summary>
        [HttpGet("~/api/eventlog/query")]
        public async Task<IActionResult> QueryEventLog(
            [FromQuery] string startTime,
            [FromQuery] string endTime,
            [FromQuery] int? eventType = null,
            [FromQuery] int? severity = null,
            [FromQuery] string? sid = null,
            [FromQuery] int? acknowledged = null)
        {
            if (!DateTime.TryParse(startTime, out var dtStart) ||
                !DateTime.TryParse(endTime, out var dtEnd))
            {
                return BadRequest(new { success = false, error = _l["error.invalid_time"].Value });
            }

            var events = await _eventLogService.QueryEventsAsync(
                dtStart, dtEnd, eventType, severity, sid, acknowledged);

            // 操作符是 ASCII 不需翻譯
            var szOperatorSymbols = new[] { ">", "<", ">=", "<=", "==", "!=" };

            return Ok(new
            {
                success = true,
                data = events.Select(e => new
                {
                    id             = e.nId,
                    sid            = e.szSID,
                    eventType      = e.nEventType,
                    eventTypeName  = LocalizeEventType(e.nEventType),
                    severity       = e.nSeverity,
                    severityName   = LocalizeSeverity(e.nSeverity),
                    triggerValue   = e.dTriggerValue,
                    thresholdValue = e.dThresholdValue,
                    operatorSymbol = e.nOperator.HasValue && e.nOperator.Value < szOperatorSymbols.Length
                                     ? szOperatorSymbols[e.nOperator.Value] : "",
                    // 依當前 culture 翻譯警報訊息；舊資料 messageKey 為 null 時 fallback 用 szMessage
                    message        = _alarmLocalizer.Localize(e.szMessageKey, e.szMessageArgs, e.szMessage),
                    occurredAt     = e.dtOccurredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    clearedAt      = e.dtClearedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    isCleared      = e.dtClearedAt.HasValue,
                    isAcknowledged = e.isAcknowledged,
                    acknowledgedBy = e.szAcknowledgedBy ?? "",
                    acknowledgedAt = e.dtAcknowledgedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
                }),
                total = events.Count()
            });
        }

        private string LocalizeEventType(int n)
        {
            var ls = _l[$"event.type.{n}"];
            return ls.ResourceNotFound ? _l["event.type.unknown"].Value : ls.Value;
        }

        private string LocalizeSeverity(int n)
        {
            var ls = _l[$"event.severity.{n}"];
            return ls.ResourceNotFound ? _l["event.severity.unknown"].Value : ls.Value;
        }

        /// <summary>
        /// 警報觸發 — 前端偵測到狀態轉變（正常→警報）時呼叫
        /// </summary>
        [HttpPost("~/api/eventlog/trigger")]
        public async Task<IActionResult> Trigger([FromBody] EventLogTriggerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.sid))
                return BadRequest(new { success = false, error = _l["error.sid_required"].Value });

            if (string.IsNullOrWhiteSpace(dto.message))
                return BadRequest(new { success = false, error = _l["error.message_required"].Value });

            var model = new EventLogModel
            {
                szSID           = dto.sid,
                nEventType      = (byte)dto.eventType,
                nSeverity       = (byte)dto.severity,
                dTriggerValue   = dto.triggerValue,
                dThresholdValue = dto.thresholdValue,
                nOperator       = dto.operatorType.HasValue ? (byte?)dto.operatorType.Value : null,
                szMessage       = dto.message,
                dtOccurredAt    = DateTime.Now
            };

            var isSuccess = await _eventLogService.InsertEventAsync(model);

            if (!isSuccess)
                return StatusCode(500, new { success = false, error = _l["error.insert_failed"].Value });

            _logger.LogInformation("警報觸發: SID={SID}, Message={Message}", dto.sid, dto.message);
            return Ok(new { success = true, sid = dto.sid });
        }

        /// <summary>
        /// 警報恢復 — 前端偵測到狀態轉變（警報→正常）時呼叫
        /// </summary>
        [HttpPost("~/api/eventlog/clear")]
        public async Task<IActionResult> Clear([FromBody] EventLogClearDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.sid))
                return BadRequest(new { success = false, error = _l["error.sid_required"].Value });

            var isSuccess = await _eventLogService.ClearEventAsync(dto.sid);

            _logger.LogInformation("警報恢復: SID={SID}, 結果={Result}", dto.sid, isSuccess);
            return Ok(new { success = true, sid = dto.sid, cleared = isSuccess });
        }

        /// <summary>
        /// 查詢所有未解除的警報
        /// </summary>
        [HttpGet("~/api/eventlog/active")]
        public async Task<IActionResult> GetActiveAlarms()
        {
            var alarms = await _eventLogService.GetActiveAlarmsAsync();

            return Ok(new
            {
                success = true,
                data = alarms.Select(a => new
                {
                    id             = a.nId,
                    sid            = a.szSID,
                    eventType      = a.nEventType,
                    severity       = a.nSeverity,
                    triggerValue   = a.dTriggerValue,
                    thresholdValue = a.dThresholdValue,
                    message        = a.szMessage,
                    occurredAt     = a.dtOccurredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    isAcknowledged = a.isAcknowledged
                })
            });
        }
    }
}
