using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.ScadaPage.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

/// <summary>
/// ScadaPage 累積量元件查詢 API（批次；前端 30 秒慢輪詢，與 1 秒即時值輪詢分離）
/// </summary>
[Authorize]
[ApiController]
public class AccumulationController : ControllerBase
{
    private const int MaxItems = 50;
    private static readonly HashSet<string> ValidModes = new() { "day", "month" };
    private static readonly HashSet<string> ValidKinds = new() { "meter", "integrate" };

    private readonly ILogger<AccumulationController> _logger;
    private readonly WidgetAccumulationService _accumulationService;

    public AccumulationController(
        ILogger<AccumulationController> logger,
        WidgetAccumulationService accumulationService)
    {
        _logger = logger;
        _accumulationService = accumulationService;
    }

    [HttpPost("/api/scadapage/accumulation")]
    public async Task<IActionResult> Query([FromBody] AccumulationRequestDto dto)
    {
        if (dto?.items == null || dto.items.Count == 0)
            return BadRequest(new { success = false, error = "items 不可為空" });

        // 過濾無效項 + 以 (sid, mode, kind, max) 去重（同頁多元件綁同點）
        var items = dto.items
            .Where(i => !string.IsNullOrWhiteSpace(i.szSid)
                        && ValidModes.Contains(i.szAccMode)
                        && ValidKinds.Contains(i.szAccKind))
            .GroupBy(i => $"{i.szSid}|{i.szAccMode}|{i.szAccKind}|{i.dMaxValue?.ToString() ?? ""}")
            .Select(g => g.First())
            .Take(MaxItems)
            .ToList();

        if (items.Count == 0)
            return BadRequest(new { success = false, error = "無有效查詢項" });

        try
        {
            var results = await _accumulationService.ComputeAsync(items, DateTime.Now);
            return Ok(new { success = true, results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "累積量查詢失敗");
            return StatusCode(500, new { success = false, error = "累積量計算失敗" });
        }
    }
}
