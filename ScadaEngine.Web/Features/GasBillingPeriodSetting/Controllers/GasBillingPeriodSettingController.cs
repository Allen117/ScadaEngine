using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.GasBillingPeriodSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.GasBillingPeriodSetting.Controllers;

/// <summary>
/// 氣費月結週期設定 — 每期（YYYY-MM）自訂起訖日期，用氣/氣費月粒度報表共用。
/// 與電費（<c>/BillingPeriodSetting</c>）、水費（<c>/WaterBillingPeriodSetting</c>）各自獨立設定，互不影響。
///
/// ⚠️ 比電/水兩版多兩支 API：<c>api/skip</c>（刪除此期，日數併入相鄰期）與 <c>api/unskip</c>（復原），
///    用來組出供氣事業常見的**兩月一期**（刪掉 2/4/6/8/10/12 月即得 6 期）。
/// api/current 與 api/range 為唯讀查詢，供用氣報表等頁面顯示期別提示 / 帶入本期預設。
/// </summary>
[Authorize]
[Route("[controller]")]
public class GasBillingPeriodSettingController : Controller
{
    private readonly GasBillingPeriodService _service;
    private readonly ILogger<GasBillingPeriodSettingController> _logger;

    public GasBillingPeriodSettingController(
        GasBillingPeriodService service, ILogger<GasBillingPeriodSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/GasBillingPeriodSetting")]
    public IActionResult Index()
    {
        return View(new GasBillingPeriodSettingViewModel());
    }

    /// <summary>指定年份「實際存在」的期別清單（含推導預設與空窗/重疊天數）+ 已刪除清單</summary>
    [HttpGet("api/list")]
    public async Task<IActionResult> GetList([FromQuery] int year)
    {
        if (year < 2000 || year > 2100)
            return BadRequest(new { message = "年份超出範圍（2000~2100）" });

        var (periods, skipped) = await _service.GetYearAsync(year);
        return Ok(new GasBillingPeriodListDto
        {
            periods = periods.Select(p => ToDto(p.period, p.nGapDays)).ToList(),
            skipped = skipped.Select(p => ToDto(p, 0)).ToList(),
        });
    }

    /// <summary>儲存單一期別自訂起訖（結束 ≥ 起始為硬性驗證；空窗/重疊由前端警告）</summary>
    [HttpPost("api/save")]
    public async Task<IActionResult> Save([FromBody] GasBillingPeriodSaveRequest dto)
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
            _logger.LogError(ex, "氣費月結週期儲存失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }

    /// <summary>還原單一期別為推導預設（刪除自訂 row，不影響已刪除狀態）</summary>
    [HttpPost("api/reset")]
    public async Task<IActionResult> Reset([FromBody] GasBillingPeriodTargetRequest dto)
    {
        try
        {
            await _service.DeleteAsync(dto.year, dto.month);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣費月結週期還原失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "還原失敗" });
        }
    }

    /// <summary>刪除此期 — 該期消失，日數併入前一期（該年第一期則由下一期向前吸收）</summary>
    [HttpPost("api/skip")]
    public async Task<IActionResult> Skip([FromBody] GasBillingPeriodTargetRequest dto)
    {
        try
        {
            await _service.SkipAsync(dto.year, dto.month);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣費月結週期刪除失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "刪除失敗" });
        }
    }

    /// <summary>復原此期 — 拆回原狀，鄰期自動收回被吸收的日數</summary>
    [HttpPost("api/unskip")]
    public async Task<IActionResult> Unskip([FromBody] GasBillingPeriodTargetRequest dto)
    {
        try
        {
            await _service.UnskipAsync(dto.year, dto.month);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣費月結週期復原失敗 {Year}-{Month}", dto.year, dto.month);
            return StatusCode(500, new { message = "復原失敗" });
        }
    }

    /// <summary>今天所屬期別 — 用氣報表日粒度預設起訖用</summary>
    [HttpGet("api/current")]
    public async Task<IActionResult> GetCurrent()
    {
        var period = await _service.GetCurrentPeriodAsync(DateTime.Today);
        return Ok(ToDto(period, 0));
    }

    /// <summary>期別區間查詢（fromYm/toYm 格式 yyyy-MM，含頭尾）— 報表月粒度期別提示用；已刪除期別不列</summary>
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

    private static GasBillingPeriodItemDto ToDto(BillingPeriodRange p, int nGapDays) => new()
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
