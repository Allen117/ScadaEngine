using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.ElectricityCostReport.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ElectricityCostReport.Controllers;

/// <summary>
/// 電費報表 — 比照用電報表（迴路 × 粒度 × 起訖 → 長條圖/明細/Excel），結果為電費（元）。
/// 資料源 ElectricityCostHourly（電費計算落表），查詢核心在 ElectricityCostService。
/// </summary>
[Authorize]
[Route("[controller]")]
public class ElectricityCostReportController : Controller
{
    private readonly EnergyCircuitService _circuitService;
    private readonly ElectricityCostService _costService;
    private readonly ElectricityCostReportExcelExporter _exporter;
    private readonly ILogger<ElectricityCostReportController> _logger;

    public ElectricityCostReportController(
        EnergyCircuitService circuitService,
        ElectricityCostService costService,
        ElectricityCostReportExcelExporter exporter,
        ILogger<ElectricityCostReportController> logger)
    {
        _circuitService = circuitService;
        _costService = costService;
        _exporter = exporter;
        _logger = logger;
    }

    [HttpGet("/ElectricityCostReport")]
    public IActionResult Index()
    {
        return View(new ElectricityCostReportViewModel());
    }

    /// <summary>取得迴路樹（給左側下拉用，同用電報表）</summary>
    [HttpGet("api/circuits")]
    public async Task<IActionResult> GetCircuits()
    {
        var nodes = await _circuitService.GetAllAsync();
        return Ok(nodes.Select(n => new
        {
            id = n.nId,
            name = n.szName,
            parentId = n.nParentId,
            sortOrder = n.nSortOrder,
            sid = n.szSID
        }));
    }

    /// <summary>查詢電費報表</summary>
    [HttpPost("api/query")]
    public async Task<IActionResult> Query([FromBody] CostReportRequestDto dto)
    {
        try
        {
            var result = await _costService.GetCostReportAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費報表查詢失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>匯出 Excel（含直接子迴路多欄展開）</summary>
    [HttpPost("api/export")]
    public async Task<IActionResult> Export([FromBody] CostReportRequestDto dto)
    {
        try
        {
            var result = await _costService.GetCostReportWithChildrenAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            var szOperator = User.Identity?.Name ?? "anonymous";
            var bytes = _exporter.Export(result, szOperator);
            var szFileName = $"ElectricityCostReport_{result.circuitName}_{result.granularity}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", szFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "電費報表匯出失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }
}
