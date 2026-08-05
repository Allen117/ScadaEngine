using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.WaterTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.WaterTariffSetting.Controllers;

/// <summary>
/// 水費設定 — 台水流動水費方案（分段累進，含生效日多版本）檢視與編輯。
/// 前端整份載入整份儲存（方案增刪在前端操作，POST api/config 一次存回）。
/// </summary>
[Authorize]
[Route("[controller]")]
public class WaterTariffSettingController : Controller
{
    private readonly WaterTariffService _service;
    private readonly ILogger<WaterTariffSettingController> _logger;

    public WaterTariffSettingController(
        WaterTariffService service,
        ILogger<WaterTariffSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/WaterTariffSetting")]
    public IActionResult Index()
    {
        return View(new WaterTariffSettingViewModel());
    }

    /// <summary>整份設定（全部方案版本）</summary>
    [HttpGet("api/config")]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            return Ok(await _service.GetConfigAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "水費設定載入失敗");
            return StatusCode(500, new { message = "設定載入失敗" });
        }
    }

    /// <summary>儲存整份設定（逐方案驗證：級距連續 / 生效日格式 / 單價非負）</summary>
    [HttpPost("api/config")]
    public async Task<IActionResult> SaveConfig([FromBody] WaterTariffConfig config)
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
            _logger.LogError(ex, "水費設定儲存失敗");
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }

    /// <summary>整份還原台水預設，回傳還原後設定</summary>
    [HttpPost("api/reset")]
    public async Task<IActionResult> Reset()
    {
        try
        {
            return Ok(await _service.ResetToSeedAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "水費設定還原台水預設失敗");
            return StatusCode(500, new { message = "還原失敗" });
        }
    }
}
