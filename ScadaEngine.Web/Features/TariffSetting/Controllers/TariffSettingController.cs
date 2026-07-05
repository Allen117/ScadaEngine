using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.TariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.TariffSetting.Controllers;

/// <summary>
/// 電費設定 — 台電各類電價方案（累進 / 單一費率 / 時間電價）檢視與編輯。
/// 本版只做設定管理；電費計算（接主要電表）留待後續版本。
/// </summary>
[Authorize]
[Route("[controller]")]
public class TariffSettingController : Controller
{
    private readonly TariffSettingService _service;
    private readonly ILogger<TariffSettingController> _logger;

    public TariffSettingController(TariffSettingService service, ILogger<TariffSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/TariffSetting")]
    public IActionResult Index()
    {
        return View(new TariffSettingViewModel());
    }

    /// <summary>整份設定（採用方案 + 全部方案）</summary>
    [HttpGet("api/config")]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            return Ok(await _service.GetConfigAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費設定載入失敗");
            return StatusCode(500, new { message = "設定載入失敗" });
        }
    }

    /// <summary>儲存單一方案（整份驗證：級距連續 / 時段覆蓋 24h 不重疊）</summary>
    [HttpPost("api/plan")]
    public async Task<IActionResult> SavePlan([FromBody] TariffPlan plan)
    {
        try
        {
            await _service.SavePlanAsync(plan);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費設定方案儲存失敗 {PlanId}", plan.szPlanId);
            return StatusCode(500, new { message = "儲存失敗" });
        }
    }

    /// <summary>設為採用方案</summary>
    [HttpPost("api/active")]
    public async Task<IActionResult> SetActive([FromBody] SetActivePlanRequest dto)
    {
        try
        {
            await _service.SetActivePlanAsync(dto.planId);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費設定採用方案切換失敗 {PlanId}", dto.planId);
            return StatusCode(500, new { message = "切換失敗" });
        }
    }

    /// <summary>還原單一方案為台電預設，回傳還原後方案</summary>
    [HttpPost("api/reset")]
    public async Task<IActionResult> ResetPlan([FromBody] ResetPlanRequest dto)
    {
        try
        {
            var plan = await _service.ResetPlanAsync(dto.planId);
            return Ok(plan);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費設定方案還原失敗 {PlanId}", dto.planId);
            return StatusCode(500, new { message = "還原失敗" });
        }
    }
}
