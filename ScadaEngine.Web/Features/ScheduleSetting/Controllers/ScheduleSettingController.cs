using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.ScheduleSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ScheduleSetting.Controllers;

[Authorize]
[Route("[controller]")]
public class ScheduleSettingController : Controller
{
    private readonly ScheduleSettingService _scheduleService;
    private readonly ILogger<ScheduleSettingController> _logger;

    public ScheduleSettingController(
        ScheduleSettingService scheduleService,
        ILogger<ScheduleSettingController> logger)
    {
        _scheduleService = scheduleService;
        _logger = logger;
    }

    /// <summary>排程設定管理頁面</summary>
    [HttpGet("/ScheduleSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "排程設定";

        var schedules = (await _scheduleService.GetAllSchedulesAsync()).ToList();
        ViewBag.Schedules = schedules;

        return View();
    }

    /// <summary>取得所有排程（AJAX）</summary>
    [HttpGet("~/api/schedules")]
    public async Task<IActionResult> GetAll()
    {
        var schedules = await _scheduleService.GetAllSchedulesAsync();
        return Ok(schedules);
    }

    /// <summary>新增或更新排程</summary>
    [HttpPost("~/api/schedules")]
    public async Task<IActionResult> Save([FromBody] ScheduleSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.name))
            return BadRequest(new { success = false, message = "排程名稱不可為空" });
        if (string.IsNullOrWhiteSpace(dto.startTime) || string.IsNullOrWhiteSpace(dto.endTime))
            return BadRequest(new { success = false, message = "開始/結束時間不可為空" });

        // 依 RecurrenceType 驗證必填欄位
        if (dto.recurrenceType is 0 or 1 && string.IsNullOrWhiteSpace(dto.daysOfWeek))
            return BadRequest(new { success = false, message = "請至少選擇一個星期" });
        if (dto.recurrenceType is 2 or 3 && string.IsNullOrWhiteSpace(dto.daysOfMonth))
            return BadRequest(new { success = false, message = "請至少選擇一個日期" });
        if (dto.recurrenceType is 1 or 3)
        {
            if (!dto.runLength.HasValue || dto.runLength < 1)
                return BadRequest(new { success = false, message = "跑的週/月數必須 >= 1" });
            if (!dto.restLength.HasValue || dto.restLength < 1)
                return BadRequest(new { success = false, message = "休的週/月數必須 >= 1" });
            if (string.IsNullOrWhiteSpace(dto.anchorDateTime))
                return BadRequest(new { success = false, message = "請設定週期起算時間" });
        }

        // 例外日 / 加開日：格式驗證 + 交集驗證 + 正規化
        if (!TryNormalizeDateList(dto.excludeDates, out var szExcludeNorm, out var szExcludeError))
            return BadRequest(new { success = false, message = "例外日格式錯誤：" + szExcludeError });
        if (!TryNormalizeDateList(dto.includeDates, out var szIncludeNorm, out var szIncludeError))
            return BadRequest(new { success = false, message = "加開日格式錯誤：" + szIncludeError });

        if (!string.IsNullOrEmpty(szExcludeNorm) && !string.IsNullOrEmpty(szIncludeNorm))
        {
            var excludeSet = new HashSet<string>(szExcludeNorm.Split(','));
            var conflict = szIncludeNorm.Split(',').FirstOrDefault(d => excludeSet.Contains(d));
            if (conflict != null)
                return BadRequest(new { success = false, message = $"日期 {conflict} 同時出現在例外日與加開日，請擇一" });
        }

        dto.excludeDates = szExcludeNorm;
        dto.includeDates = szIncludeNorm;

        var isSuccess = await _scheduleService.SaveScheduleAsync(dto);
        if (isSuccess)
            return Ok(new { success = true, message = "排程已儲存" });

        return StatusCode(500, new { success = false, message = "儲存失敗" });
    }

    /// <summary>驗證並正規化日期清單字串：每項須為合法 yyyy-MM-dd，輸出去重 + 排序的逗號分隔字串</summary>
    private static bool TryNormalizeDateList(string? raw, out string? normalized, out string error)
    {
        normalized = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in parts)
        {
            if (!DateTime.TryParseExact(p, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            {
                error = $"\"{p}\" 不是合法日期（須為 yyyy-MM-dd）";
                return false;
            }
            set.Add(p);
        }
        normalized = set.Count == 0 ? null : string.Join(',', set);
        return true;
    }

    /// <summary>刪除排程</summary>
    [HttpDelete("~/api/schedules/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var isSuccess = await _scheduleService.DeleteScheduleAsync(id);
        if (isSuccess)
            return Ok(new { success = true, message = "已刪除" });

        return NotFound(new { success = false, message = "排程不存在" });
    }
}
