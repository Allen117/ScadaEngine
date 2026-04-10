using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public EventLogController(ILogger<EventLogController> logger, EventLogService eventLogService)
        {
            _logger = logger;
            _eventLogService = eventLogService;
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
                return BadRequest(new { success = false, error = "時間格式不正確" });
            }

            var events = await _eventLogService.QueryEventsAsync(
                dtStart, dtEnd, eventType, severity, sid, acknowledged);

            var szEventTypeNames = new[] { "警報", "故障", "警告", "資訊", "系統" };
            var szSeverityNames = new[] { "緊急", "高", "中", "低" };
            var szOperatorSymbols = new[] { ">", "<", ">=", "<=", "==", "!=" };

            return Ok(new
            {
                success = true,
                data = events.Select(e => new
                {
                    id             = e.nId,
                    sid            = e.szSID,
                    eventType      = e.nEventType,
                    eventTypeName  = e.nEventType < szEventTypeNames.Length ? szEventTypeNames[e.nEventType] : "未知",
                    severity       = e.nSeverity,
                    severityName   = e.nSeverity < szSeverityNames.Length ? szSeverityNames[e.nSeverity] : "未知",
                    triggerValue   = e.dTriggerValue,
                    thresholdValue = e.dThresholdValue,
                    operatorSymbol = e.nOperator.HasValue && e.nOperator.Value < szOperatorSymbols.Length
                                     ? szOperatorSymbols[e.nOperator.Value] : "",
                    message        = e.szMessage,
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

        /// <summary>
        /// 警報觸發 — 前端偵測到狀態轉變（正常→警報）時呼叫
        /// </summary>
        [HttpPost("~/api/eventlog/trigger")]
        public async Task<IActionResult> Trigger([FromBody] EventLogTriggerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.sid))
                return BadRequest(new { success = false, error = "SID 不可為空" });

            if (string.IsNullOrWhiteSpace(dto.message))
                return BadRequest(new { success = false, error = "Message 不可為空" });

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
                return StatusCode(500, new { success = false, error = "寫入事件記錄失敗" });

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
                return BadRequest(new { success = false, error = "SID 不可為空" });

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
