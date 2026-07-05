using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.BillingPeriodSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.BillingPeriodSetting.Controllers;

/// <summary>
/// 月結週期設定 — 每期（YYYY-MM）自訂起訖日期，全系統月粒度報表共用。
/// api/current 與 api/range 為唯讀查詢，供用電報表等頁面顯示期別提示 / 帶入本期預設。
/// </summary>
[Authorize]
[Route("[controller]")]
public class BillingPeriodSettingController : Controller
{
    private readonly BillingPeriodService _service;
    private readonly ILogger<BillingPeriodSettingController> _logger;

    public BillingPeriodSettingController(BillingPeriodService service, ILogger<BillingPeriodSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/BillingPeriodSetting")]
    public IActionResult Index()
    {
        return View(new BillingPeriodSettingViewModel());
    }

    /// <summary>指定年份的 12 期清單（含推導預設與空窗/重疊天數）</summary>
    [HttpGet("api/list")]
    public async Task<IActionResult> GetList([FromQuery] int year)
    {
        if (year < 2000 || year > 2100)
            return BadRequest(new { message = "年份超出範圍（2000~2100）" });

        var periods = await _service.GetYearAsync(year);
        return Ok(periods.Select(p => ToDto(p.period, p.nGapDays)));
    }

    /// <summary>儲存單一期別自訂起訖（結束 ≥ 起始為硬性驗證；空窗/重疊由前端警告）</summary>
    [HttpPost("api/save")]
    public async Task<IActionResult> Save([FromBody] BillingPeriodSaveRequest dto)
    {
        try
        {
            await _service.SaveAsync(dto.year, dto.month, dto.start, dto.end);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "月結週期儲存失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }

    /// <summary>還原單一期別為推導預設（刪除自訂 row）</summary>
    [HttpPost("api/reset")]
    public async Task<IActionResult> Reset([FromBody] BillingPeriodResetRequest dto)
    {
        try
        {
            await _service.DeleteAsync(dto.year, dto.month);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "月結週期還原失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "還原失敗" });
        }
    }

    /// <summary>今天所屬期別 — 用電報表日粒度預設起訖用</summary>
    [HttpGet("api/current")]
    public async Task<IActionResult> GetCurrent()
    {
        var period = await _service.GetCurrentPeriodAsync(DateTime.Today);
        return Ok(ToDto(period, 0));
    }

    /// <summary>期別區間查詢（fromYm/toYm 格式 yyyy-MM，含頭尾）— 報表月粒度期別提示用</summary>
    [HttpGet("api/range")]
    public async Task<IActionResult> GetRange([FromQuery] string? fromYm, [FromQuery] string? toYm)
    {
        if (!DateTime.TryParse(fromYm + "-01", out var dtFrom) || !DateTime.TryParse(toYm + "-01", out var dtTo))
            return BadRequest(new { message = "fromYm/toYm 格式不正確（yyyy-MM）" });
        if (dtTo < dtFrom)
            return BadRequest(new { message = "toYm 不可早於 fromYm" });
        // 防呆：一次最多查 5 年份量
        if ((dtTo.Year - dtFrom.Year) * 12 + dtTo.Month - dtFrom.Month >= 60)
            return BadRequest(new { message = "查詢區間過大" });

        var periods = await _service.GetPeriodRangesAsync(dtFrom, dtTo);
        return Ok(periods.Select(p => ToDto(p, 0)));
    }

    private static BillingPeriodItemDto ToDto(BillingPeriodRange p, int nGapDays) => new()
    {
        year = p.nYear,
        month = p.nMonth,
        start = p.dtStart.ToString("yyyy-MM-dd"),
        end = p.dtEndInclusive.ToString("yyyy-MM-dd"),
        days = (int)(p.dtEndExclusive - p.dtStart).TotalDays,
        isCustomized = p.isCustomized,
        isNatural = p.isNaturalMonth,
        label = p.szLabel,
        gapDays = nGapDays,
    };
}
