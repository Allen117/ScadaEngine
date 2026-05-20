using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;
using ScadaEngine.Web.Features.AlarmSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.AlarmSetting.Controllers;

[Authorize]
[Route("[controller]")]
public class AlarmSettingController : Controller
{
    private readonly AlarmRuleService _alarmRuleService;
    private readonly LineTargetService _lineTargetService;
    private readonly LineTestSendService _lineTestSendService;
    private readonly IDataRepository _dataRepository;
    private readonly ILogger<AlarmSettingController> _logger;

    public AlarmSettingController(
        AlarmRuleService alarmRuleService,
        LineTargetService lineTargetService,
        LineTestSendService lineTestSendService,
        IDataRepository dataRepository,
        ILogger<AlarmSettingController> logger)
    {
        _alarmRuleService = alarmRuleService;
        _lineTargetService = lineTargetService;
        _lineTestSendService = lineTestSendService;
        _dataRepository = dataRepository;
        _logger = logger;
    }

    /// <summary>管理頁面</summary>
    [HttpGet("/AlarmSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "警報設定";

        var coordinatorList = (await _dataRepository.GetAllCoordinatorsAsync()).ToList();
        var pointList = (await _dataRepository.GetAllModbusPointsAsync())
            .OrderBy(p =>
            {
                var idx = p.szSID.IndexOf("-S", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 && int.TryParse(p.szSID[(idx + 2)..], out var n) ? n : int.MaxValue;
            })
            .ToList();

        // 合併計算點位
        var calcPoints = (await _dataRepository.GetAllCalculatedPointsAsync())
            .Where(c => c.isEnabled)
            .Select(c => new ModbusPointModel { szSID = c.szSID, szName = c.szName, szUnit = c.szUnit });
        pointList.AddRange(calcPoints);

        // 合併 DB 來源點位
        var dbPoints = (await _dataRepository.GetAllDbPointsAsync())
            .Select(p => new ModbusPointModel { szSID = p.szSID, szName = p.szName, szUnit = p.szUnit ?? string.Empty });
        pointList.AddRange(dbPoints);

        var rules = (await _alarmRuleService.GetAllRulesAsync()).ToList();
        var lineTargets = (await _lineTargetService.GetAllAsync()).ToList();

        // 從已發布 Designer 設計擷取 DI 點位的 ON/OFF 標籤，供前端同步顯示
        var designPages = await _dataRepository.LoadPublishedDesignAsync();
        var diLabelMap = ExtractDiLabelsFromDesigns(designPages);

        ViewBag.CoordinatorList = coordinatorList;
        ViewBag.PointList = pointList;
        ViewBag.Rules = rules;
        ViewBag.LineTargets = lineTargets;
        ViewBag.DiLabelMap = diLabelMap;

        return View();
    }

    /// <summary>
    /// 解析 Designer 已發布頁面的 widget JSON，依 SID 蒐集 DI 點位的 ON/OFF 標籤。
    /// 包含頂層 diPoint widget 與 table widget 內 szPointType=DI 的 cell。
    /// 同 SID 已被多 widget 綁定時：以「非預設 ON/OFF」優先；否則保留先寫入者。
    /// </summary>
    private Dictionary<string, DiLabelDto> ExtractDiLabelsFromDesigns(IEnumerable<ScadaDesignPageModel> pages)
    {
        var map = new Dictionary<string, DiLabelDto>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.szWidgetStateJson)) continue;

            try
            {
                using var doc = JsonDocument.Parse(page.szWidgetStateJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var ws in doc.RootElement.EnumerateArray())
                {
                    if (ws.ValueKind != JsonValueKind.Object) continue;
                    var szType = ws.TryGetProperty("szType", out var t) ? t.GetString() : null;
                    if (!ws.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object) continue;

                    if (szType == "diPoint")
                    {
                        var szSid = props.TryGetProperty("szSid", out var s) ? s.GetString() : null;
                        if (string.IsNullOrEmpty(szSid)) continue;
                        var on = props.TryGetProperty("szOnLabel", out var o) ? (o.GetString() ?? "ON") : "ON";
                        var off = props.TryGetProperty("szOffLabel", out var f) ? (f.GetString() ?? "OFF") : "OFF";
                        UpsertLabel(map, szSid, on, off);
                    }
                    else if (szType == "table")
                    {
                        if (!props.TryGetProperty("arrCells", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;

                        foreach (var row in arr.EnumerateArray())
                        {
                            if (row.ValueKind != JsonValueKind.Array) continue;
                            foreach (var cell in row.EnumerateArray())
                            {
                                if (cell.ValueKind != JsonValueKind.Object) continue;
                                var szPT = cell.TryGetProperty("szPointType", out var p1) ? p1.GetString() : null;
                                if (!string.Equals(szPT, "DI", StringComparison.OrdinalIgnoreCase)) continue;
                                var szSid = cell.TryGetProperty("szSid", out var s2) ? s2.GetString() : null;
                                if (string.IsNullOrEmpty(szSid)) continue;
                                var on = cell.TryGetProperty("szOnLabel", out var o2) ? (o2.GetString() ?? "ON") : "ON";
                                var off = cell.TryGetProperty("szOffLabel", out var f2) ? (f2.GetString() ?? "OFF") : "OFF";
                                UpsertLabel(map, szSid, on, off);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 Designer 頁面 DI 標籤失敗：PageSid={PageSid}", page.szPageSid);
            }
        }

        return map;
    }

    private static void UpsertLabel(Dictionary<string, DiLabelDto> map, string szSid, string szOn, string szOff)
    {
        var isDefault = szOn == "ON" && szOff == "OFF";
        if (!map.TryGetValue(szSid, out var existing))
        {
            map[szSid] = new DiLabelDto { onLabel = szOn, offLabel = szOff };
            return;
        }
        // 已有預設值且新值非預設 → 用更具體的覆蓋
        var existingIsDefault = existing.onLabel == "ON" && existing.offLabel == "OFF";
        if (existingIsDefault && !isDefault)
            map[szSid] = new DiLabelDto { onLabel = szOn, offLabel = szOff };
    }

    // ── Line 通知收件群組 CRUD ──

    [HttpGet("~/api/line-targets")]
    public async Task<IActionResult> GetAllLineTargets()
    {
        var targets = await _lineTargetService.GetAllAsync();
        return Ok(targets);
    }

    [HttpPost("~/api/line-targets")]
    public async Task<IActionResult> SaveLineTarget([FromBody] LineTargetSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.groupId))
            return BadRequest(new { success = false, message = "GroupID 不可為空" });
        if (string.IsNullOrWhiteSpace(dto.label))
            return BadRequest(new { success = false, message = "標籤不可為空" });
        if (dto.maxSeverity > 3)
            return BadRequest(new { success = false, message = "嚴重度上限必須在 0–3 之間" });

        var isSuccess = await _lineTargetService.SaveAsync(dto);
        if (isSuccess)
            return Ok(new { success = true, message = "已儲存" });
        return StatusCode(500, new { success = false, message = "儲存失敗" });
    }

    [HttpDelete("~/api/line-targets/{id}")]
    public async Task<IActionResult> DeleteLineTarget(int id)
    {
        var isSuccess = await _lineTargetService.DeleteAsync(id);
        if (isSuccess)
            return Ok(new { success = true, message = "已刪除" });
        return NotFound(new { success = false, message = "群組不存在" });
    }

    [HttpPost("~/api/line-targets/{id}/toggle")]
    public async Task<IActionResult> ToggleLineTarget(int id, [FromBody] LineTargetToggleDto dto)
    {
        var isSuccess = await _lineTargetService.ToggleEnabledAsync(id, dto.isEnabled);
        if (isSuccess)
            return Ok(new { success = true });
        return StatusCode(500, new { success = false, message = "切換失敗" });
    }

    [HttpPost("~/api/line-targets/{id}/test")]
    public async Task<IActionResult> TestLineTarget(int id)
    {
        var target = await _lineTargetService.GetByIdAsync(id);
        if (target == null)
            return NotFound(new { success = false, message = "群組不存在" });

        var result = await _lineTestSendService.SendTestAsync(target.szGroupId, target.szLabel);
        if (result.isSuccess)
            return Ok(new { success = true, message = result.szMessage });

        if (result.isThrottled)
        {
            Response.Headers["Retry-After"] = result.nRetryAfterSeconds.ToString();
            return StatusCode(429, new
            {
                success = false,
                throttled = true,
                retryAfter = result.nRetryAfterSeconds,
                message = result.szMessage
            });
        }

        return StatusCode(502, new { success = false, message = result.szMessage });
    }

    /// <summary>取得所有規則（AJAX）</summary>
    [HttpGet("~/api/alarm-rules")]
    public async Task<IActionResult> GetAllRules()
    {
        var rules = await _alarmRuleService.GetAllRulesAsync();
        return Ok(rules);
    }

    /// <summary>新增或更新規則</summary>
    [HttpPost("~/api/alarm-rules")]
    public async Task<IActionResult> SaveRule([FromBody] AlarmRuleSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.sid))
            return BadRequest(new { success = false, message = "SID 不可為空" });

        var isSuccess = await _alarmRuleService.SaveRuleAsync(dto);
        if (isSuccess)
            return Ok(new { success = true, message = "警報規則已儲存" });

        return StatusCode(500, new { success = false, message = "儲存失敗" });
    }

    /// <summary>刪除規則</summary>
    [HttpDelete("~/api/alarm-rules/{id}")]
    public async Task<IActionResult> DeleteRule(int id)
    {
        var isSuccess = await _alarmRuleService.DeleteRuleAsync(id);
        if (isSuccess)
            return Ok(new { success = true, message = "已刪除" });

        return NotFound(new { success = false, message = "規則不存在" });
    }
}
