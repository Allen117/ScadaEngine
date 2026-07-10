using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.HolidaySetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.HolidaySetting.Controllers;

/// <summary>
/// 國定假日設定 — 12 個月年曆點選標註國定假日/離峰日。
/// 標註日在 TOU 計價時以 sun_offday（週日及離峰日）費率落段；
/// 編修只影響之後的自動計價，歷史區間需至電費設定頁執行重新計算。
/// </summary>
[Authorize]
[Route("[controller]")]
public class HolidaySettingController : Controller
{
    private readonly HolidayService _service;
    private readonly ILogger<HolidaySettingController> _logger;

    public HolidaySettingController(HolidayService service, ILogger<HolidaySettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/HolidaySetting")]
    public IActionResult Index()
    {
        return View(new HolidaySettingViewModel());
    }

    /// <summary>取得指定年度所有標註日期（yyyy-MM-dd 陣列）</summary>
    [HttpGet("api/holidays")]
    public async Task<IActionResult> GetHolidays([FromQuery] int? year)
    {
        if (year == null || year < 2000 || year > 2100)
            return BadRequest(new { message = "year 必須為 2000–2100" });

        try
        {
            var dates = await _service.GetYearAsync(year.Value);
            return Ok(new { year, dates = dates.Select(d => d.ToString("yyyy-MM-dd")) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "國定假日載入失敗 {Year}", year);
            return StatusCode(500, new { message = "載入失敗" });
        }
    }

    /// <summary>整年批次覆蓋儲存（傳入集合即該年全部標註日，未列入者取消）</summary>
    [HttpPost("api/holidays")]
    public async Task<IActionResult> SaveHolidays([FromBody] SaveHolidaysRequest dto)
    {
        if (dto.year < 2000 || dto.year > 2100)
            return BadRequest(new { message = "year 必須為 2000–2100" });

        var dates = new List<DateTime>(dto.dates.Count);
        foreach (var szDate in dto.dates)
        {
            if (!DateTime.TryParseExact(szDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                return BadRequest(new { message = $"日期格式不正確：{szDate}" });
            dates.Add(dt);
        }

        try
        {
            await _service.SaveYearAsync(dto.year, dates);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "國定假日儲存失敗 {Year}", dto.year);
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }
}
