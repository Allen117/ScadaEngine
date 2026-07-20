using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.EmsCardSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.EmsCardSetting.Controllers;

/// <summary>
/// EMS 首頁卡片顯示設定 — 開關 + 排序。
/// 卡片清單來自 EmsCardRegistry merge DB 覆寫（未入 DB 的新卡片自動列出、預設顯示排最後）。
/// 直連權限由全域 PageAccessFilter 依 ConfigurablePages 檢查。
/// </summary>
[Authorize]
[Route("[controller]")]
public class EmsCardSettingController : Controller
{
    private readonly EmsCardSettingService _service;
    private readonly ILogger<EmsCardSettingController> _logger;

    public EmsCardSettingController(EmsCardSettingService service, ILogger<EmsCardSettingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/EmsCardSetting")]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>取得生效卡片清單（含隱藏卡；順序即生效顯示順序）。卡名由前端依 resx key 翻譯。</summary>
    [HttpGet("api/cards")]
    public async Task<IActionResult> GetCards()
    {
        try
        {
            var aCards = await _service.GetEffectiveCardsAsync();
            return Ok(new
            {
                cards = aCards.Select(c => new
                {
                    cardKey    = c.Definition.szCardKey,
                    nameKey    = c.Definition.szNameResxKey,
                    gridCss    = c.Definition.szGridColumnCss,
                    icon       = c.Definition.szIconCss,
                    isVisible  = c.isVisible
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EMS 卡片設定載入失敗");
            return StatusCode(500, new { message = "load_failed" });
        }
    }

    /// <summary>儲存整份設定（陣列順序 = 顯示順序；server 端正規化 SortOrder 1..N）</summary>
    [HttpPost("api/cards")]
    public async Task<IActionResult> SaveCards([FromBody] SaveEmsCardsRequest dto)
    {
        if (dto?.cards == null || dto.cards.Count == 0)
            return BadRequest(new { message = "cards_required" });

        try
        {
            await _service.SaveAsync(dto.cards);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EMS 卡片設定儲存失敗");
            return StatusCode(500, new { message = "save_failed" });
        }
    }
}
