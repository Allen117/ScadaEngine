using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.ShiftSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ShiftSetting.Controllers;

/// <summary>
/// 班別設定 — 定義班別（名稱 + 整點起訖、可跨日 + 適用星期），
/// 供電/水/氣用量報表「班別」粒度切分使用。整份載入整份儲存（SystemSettings JSON）。
/// </summary>
[Authorize]
[Route("[controller]")]
public class ShiftSettingController : Controller
{
    private readonly ShiftScheduleService _service;
    private readonly ILogger<ShiftSettingController> _logger;

    public ShiftSettingController(ShiftScheduleService service, ILogger<ShiftSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/ShiftSetting")]
    public IActionResult Index()
    {
        return View(new ShiftSettingViewModel());
    }

    /// <summary>整份班別設定</summary>
    [HttpGet("api/config")]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            return Ok(await _service.GetConfigAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "班別設定載入失敗");
            return StatusCode(500, new { message = "載入失敗" });
        }
    }

    /// <summary>儲存整份班別設定（整份驗證：名稱唯一 / 整點起訖 / 週時間軸不重疊）。空清單 = 清空班表。</summary>
    [HttpPost("api/config")]
    public async Task<IActionResult> SaveConfig([FromBody] ShiftScheduleConfig config)
    {
        try
        {
            await _service.SaveConfigAsync(config);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "班別設定儲存失敗");
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }
}
