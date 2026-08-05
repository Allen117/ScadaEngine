using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.GasUsageReport.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.GasUsageReport.Controllers;

[Authorize]
[Route("[controller]")]
public class GasUsageReportController : Controller
{
    private readonly GasMeterCircuitService _circuitService;
    private readonly GasUsageReportService _reportService;
    private readonly GasUsageReportExcelExporter _exporter;
    private readonly ILogger<GasUsageReportController> _logger;

    public GasUsageReportController(
        GasMeterCircuitService circuitService,
        GasUsageReportService reportService,
        GasUsageReportExcelExporter exporter,
        ILogger<GasUsageReportController> logger)
    {
        _circuitService = circuitService;
        _reportService = reportService;
        _exporter = exporter;
        _logger = logger;
    }

    [HttpGet("/GasUsageReport")]
    public IActionResult Index()
    {
        return View(new GasUsageReportViewModel());
    }

    /// <summary>取得氣表迴路樹（給左側下拉用）</summary>
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

    /// <summary>查詢報表</summary>
    [HttpPost("api/query")]
    public async Task<IActionResult> Query([FromBody] GasUsageReportRequestDto dto)
    {
        try
        {
            var result = await _reportService.GetReportWithChildrenAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用氣報表查詢失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>匯出 Excel</summary>
    [HttpPost("api/export")]
    public async Task<IActionResult> Export([FromBody] GasUsageReportRequestDto dto)
    {
        try
        {
            var result = await _reportService.GetReportWithChildrenAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            var szOperator = User.Identity?.Name ?? "anonymous";
            var bytes = _exporter.Export(result, szOperator);
            var szFileName = $"GasUsageReport_{result.szCircuitName}_{result.szGranularity}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", szFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用氣報表匯出失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }
}
