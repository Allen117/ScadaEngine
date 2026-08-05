using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.GasTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.GasTariffSetting.Controllers;

/// <summary>
/// 氣費設定 — 天然氣流動氣費方案（分段累進，含生效日多版本）檢視與編輯。
/// 前端整份載入整份儲存（方案增刪在前端操作，POST api/config 一次存回）。
/// </summary>
[Authorize]
[Route("[controller]")]
public class GasTariffSettingController : Controller
{
    private readonly GasTariffService _service;
    private readonly ILogger<GasTariffSettingController> _logger;

    public GasTariffSettingController(
        GasTariffService service,
        ILogger<GasTariffSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/GasTariffSetting")]
    public IActionResult Index()
    {
        return View(new GasTariffSettingViewModel());
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
            _logger.LogError(ex, "氣費設定載入失敗");
            return StatusCode(500, new { message = "設定載入失敗" });
        }
    }

    /// <summary>儲存整份設定（逐方案驗證：級距連續 / 生效日格式 / 單價非負）</summary>
    [HttpPost("api/config")]
    public async Task<IActionResult> SaveConfig([FromBody] GasTariffConfig config)
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
            _logger.LogError(ex, "氣費設定儲存失敗");
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }

    /// <summary>整份還原預設範本（空白單一級距），回傳還原後設定</summary>
    [HttpPost("api/reset")]
    public async Task<IActionResult> Reset()
    {
        try
        {
            return Ok(await _service.ResetToSeedAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣費設定還原預設失敗");
            return StatusCode(500, new { message = "還原失敗" });
        }
    }
}
