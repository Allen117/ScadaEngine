using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.ScadaPage.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

/// <summary>
/// ScadaPage 迴路指標元件查詢 API（批次；前端 30 秒慢輪詢，與 1 秒即時值輪詢分離）。
/// 超過 50 項時前端分批送出。
/// </summary>
[Authorize]
[ApiController]
public class CircuitMetricController : ControllerBase
{
    private const int MaxItems = 50;

    private readonly ILogger<CircuitMetricController> _logger;
    private readonly WidgetCircuitMetricService _metricService;

    public CircuitMetricController(
        ILogger<CircuitMetricController> logger,
        WidgetCircuitMetricService metricService)
    {
        _logger = logger;
        _metricService = metricService;
    }

    [HttpPost("/api/scadapage/circuit-metric")]
    public async Task<IActionResult> Query([FromBody] CircuitMetricRequestDto dto)
    {
        if (dto?.items == null || dto.items.Count == 0)
            return BadRequest(new { success = false, error = "items 不可為空" });

        // 過濾無效項 + 以 (circuitId, metric) 去重（同頁多元件綁同迴路同指標）
        var items = dto.items
            .Where(i => i.nCircuitId > 0 && WidgetCircuitMetricService.ValidMetrics.Contains(i.szMetric))
            .GroupBy(WidgetCircuitMetricCache.ResultKey)
            .Select(g => g.First())
            .Take(MaxItems)
            .ToList();

        if (items.Count == 0)
            return BadRequest(new { success = false, error = "無有效查詢項" });

        try
        {
            var results = await _metricService.ComputeAsync(items, DateTime.Now);
            return Ok(new { success = true, results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "迴路指標查詢失敗");
            return StatusCode(500, new { success = false, error = "迴路指標計算失敗" });
        }
    }
}
