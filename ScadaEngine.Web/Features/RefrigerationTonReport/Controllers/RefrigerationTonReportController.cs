using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.RefrigerationTonReport.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.RefrigerationTonReport.Controllers;

[Authorize]
[Route("[controller]")]
public class RefrigerationTonReportController : Controller
{
    private readonly WaterCircuitService _circuitService;
    private readonly RefrigerationTonReportService _reportService;
    private readonly RefrigerationTonReportExcelExporter _exporter;
    private readonly ILogger<RefrigerationTonReportController> _logger;

    public RefrigerationTonReportController(
        WaterCircuitService circuitService,
        RefrigerationTonReportService reportService,
        RefrigerationTonReportExcelExporter exporter,
        ILogger<RefrigerationTonReportController> logger)
    {
        _circuitService = circuitService;
        _reportService = reportService;
        _exporter = exporter;
        _logger = logger;
    }

    [HttpGet("/RefrigerationTonReport")]
    public IActionResult Index()
    {
        ViewData["Title"] = "冷凍噸報表";
        return View(new RefrigerationTonReportViewModel());
    }

    /// <summary>取得水系統迴路樹（給左側下拉用）</summary>
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
    public async Task<IActionResult> Query([FromBody] RefrigerationTonReportRequestDto dto)
    {
        try
        {
            var result = await _reportService.GetReportWithChildrenAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "冷凍噸報表查詢失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>匯出 Excel</summary>
    [HttpPost("api/export")]
    public async Task<IActionResult> Export([FromBody] RefrigerationTonReportRequestDto dto)
    {
        try
        {
            var result = await _reportService.GetReportWithChildrenAsync(dto.circuitId, dto.granularity, dto.start, dto.end);
            var szOperator = User.Identity?.Name ?? "anonymous";
            var bytes = _exporter.Export(result, szOperator);
            var szFileName = $"RefrigerationTonReport_{result.szCircuitName}_{result.szGranularity}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", szFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "冷凍噸報表匯出失敗 circuitId={CircuitId}", dto.circuitId);
            return BadRequest(new { message = ex.Message });
        }
    }
}
