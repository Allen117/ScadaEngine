using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.EnergyDeclaration.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.EnergyDeclaration.Controllers;

[Authorize]
[Route("[controller]")]
public class EnergyDeclarationController : Controller
{
    private readonly EnergyDeclarationService _declarationService;
    private readonly EnergyCircuitService _energyCircuitService;
    private readonly WaterCircuitService _waterCircuitService;
    private readonly EnergyDeclarationExcelExporter _exporter;
    private readonly ILogger<EnergyDeclarationController> _logger;

    public EnergyDeclarationController(
        EnergyDeclarationService declarationService,
        EnergyCircuitService energyCircuitService,
        WaterCircuitService waterCircuitService,
        EnergyDeclarationExcelExporter exporter,
        ILogger<EnergyDeclarationController> logger)
    {
        _declarationService = declarationService;
        _energyCircuitService = energyCircuitService;
        _waterCircuitService = waterCircuitService;
        _exporter = exporter;
        _logger = logger;
    }

    [HttpGet("/EnergyDeclaration")]
    public IActionResult Index()
    {
        ViewData["Title"] = "能源申報";
        return View(new EnergyDeclarationViewModel());
    }

    // ---------- 申報報表設定 CRUD ----------

    /// <summary>取得所有申報報表設定（迴路名稱由前端以迴路清單解析）</summary>
    [HttpGet("api/reports")]
    public async Task<IActionResult> GetReports()
    {
        var reports = await _declarationService.GetAllAsync();
        return Ok(reports.Select(r => new
        {
            id = r.nId,
            name = r.szName,
            energyCircuitId = r.nEnergyCircuitId,
            waterCircuitId = r.nWaterCircuitId,
            sortOrder = r.nSortOrder,
            description = r.szDescription
        }));
    }

    [HttpPost("api/reports")]
    public async Task<IActionResult> CreateReport([FromBody] SaveDeclarationReportDto dto)
    {
        var szError = await ValidateAsync(dto);
        if (szError != null) return BadRequest(new { message = szError });

        var nId = await _declarationService.CreateAsync(new EnergyDeclarationModel
        {
            szName = dto.name.Trim(),
            nEnergyCircuitId = dto.energyCircuitId,
            nWaterCircuitId = dto.waterCircuitId,
            szDescription = string.IsNullOrWhiteSpace(dto.description) ? null : dto.description.Trim()
        });
        return Ok(new { id = nId });
    }

    [HttpPut("api/reports/{nId:int}")]
    public async Task<IActionResult> UpdateReport(int nId, [FromBody] SaveDeclarationReportDto dto)
    {
        var szError = await ValidateAsync(dto);
        if (szError != null) return BadRequest(new { message = szError });

        var isOk = await _declarationService.UpdateAsync(
            nId, dto.name.Trim(), dto.energyCircuitId, dto.waterCircuitId,
            string.IsNullOrWhiteSpace(dto.description) ? null : dto.description.Trim());
        return isOk ? Ok() : NotFound();
    }

    [HttpDelete("api/reports/{nId:int}")]
    public async Task<IActionResult> DeleteReport(int nId)
    {
        var isOk = await _declarationService.DeleteAsync(nId);
        return isOk ? Ok() : NotFound();
    }

    /// <summary>後端驗證：名稱必填、兩個迴路必填且必須存在（成對）</summary>
    private async Task<string?> ValidateAsync(SaveDeclarationReportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.name))
            return "報表名稱不可為空";
        if (dto.energyCircuitId <= 0 || dto.waterCircuitId <= 0)
            return "用電迴路與冷凍噸迴路皆為必填";
        if (await _energyCircuitService.GetByIdAsync(dto.energyCircuitId) == null)
            return $"用電迴路 Id={dto.energyCircuitId} 不存在";
        if (await _waterCircuitService.GetByIdAsync(dto.waterCircuitId) == null)
            return $"水系統迴路 Id={dto.waterCircuitId} 不存在";
        return null;
    }

    // ---------- 迴路清單（設定彈窗下拉 + 名稱解析用） ----------

    /// <summary>用電迴路樹（扁平清單，前端組縮排）</summary>
    [HttpGet("api/circuits")]
    public async Task<IActionResult> GetEnergyCircuits()
    {
        var nodes = await _energyCircuitService.GetAllAsync();
        return Ok(nodes.Select(n => new
        {
            id = n.nId,
            name = n.szName,
            parentId = n.nParentId,
            sortOrder = n.nSortOrder,
            sid = n.szSID
        }));
    }

    /// <summary>水系統迴路樹（扁平清單，前端組縮排）</summary>
    [HttpGet("api/watercircuits")]
    public async Task<IActionResult> GetWaterCircuits()
    {
        var nodes = await _waterCircuitService.GetAllAsync();
        return Ok(nodes.Select(n => new
        {
            id = n.nId,
            name = n.szName,
            parentId = n.nParentId,
            sortOrder = n.nSortOrder,
            sid = n.szSID
        }));
    }

    // ---------- 查詢 / 匯出 ----------

    [HttpPost("api/query")]
    public async Task<IActionResult> Query([FromBody] EnergyDeclarationQueryDto dto)
    {
        try
        {
            var result = await _declarationService.GetDeclarationReportAsync(dto.reportId, dto.year);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "能源申報查詢失敗 reportId={ReportId} year={Year}", dto.reportId, dto.year);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/export")]
    public async Task<IActionResult> Export([FromBody] EnergyDeclarationQueryDto dto)
    {
        try
        {
            var result = await _declarationService.GetDeclarationReportAsync(dto.reportId, dto.year);
            var szOperator = User.Identity?.Name ?? "anonymous";
            var bytes = _exporter.Export(result, szOperator);
            var szFileName = $"EnergyDeclaration_{result.szReportName}_{result.nYear}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", szFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "能源申報匯出失敗 reportId={ReportId} year={Year}", dto.reportId, dto.year);
            return BadRequest(new { message = ex.Message });
        }
    }
}
