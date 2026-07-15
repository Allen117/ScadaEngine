using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.EnergyBaseline.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.EnergyBaseline.Controllers;

/// <summary>
/// ISO 50001 能源基準 — 基線回歸建模（P1）+ EnPI/節能量報告（P2）+ SEU 鑑別（P3）。
/// </summary>
[Authorize]
[Route("[controller]")]
public class EnergyBaselineController : Controller
{
    private readonly EnergyBaselineService _baselineService;
    private readonly EnPIReportService _enpiService;
    private readonly EnPIReportExcelExporter _exporter;
    private readonly EnergyCircuitService _circuitService;
    private readonly IDataRepository _repository;
    private readonly CalcPointService _calcPointService;
    private readonly ILogger<EnergyBaselineController> _logger;

    public EnergyBaselineController(
        EnergyBaselineService baselineService,
        EnPIReportService enpiService,
        EnPIReportExcelExporter exporter,
        EnergyCircuitService circuitService,
        IDataRepository repository,
        CalcPointService calcPointService,
        ILogger<EnergyBaselineController> logger)
    {
        _baselineService = baselineService;
        _enpiService = enpiService;
        _exporter = exporter;
        _circuitService = circuitService;
        _repository = repository;
        _calcPointService = calcPointService;
        _logger = logger;
    }

    [HttpGet("/EnergyBaseline")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/EnergyBaseline"))
            return Redirect("/EMS");
        return View(new EnergyBaselineViewModel());
    }

    // ---------- 基礎資料（picker 用） ----------

    /// <summary>全來源點位清單（Modbus + 計算 + DB + OPC UA），同 /CalcPoint/Points 慣例</summary>
    [HttpGet("api/points")]
    public async Task<IActionResult> GetPoints()
    {
        var modbusPoints = await _repository.GetAllModbusPointsAsync();
        var calcPoints = await _calcPointService.GetAllAsync();
        var dbPoints = await _repository.GetAllDbPointsAsync();
        var opcUaPoints = await _repository.GetAllOpcUaPointsAsync();

        var allPoints = modbusPoints.Select(p => new
        {
            szSid = p.szSID, szName = p.szName, szUnit = p.szUnit, szType = "Modbus"
        }).Concat(calcPoints.Select(p => new
        {
            szSid = p.szSID, szName = p.szName, szUnit = p.szUnit, szType = "Calculated"
        })).Concat(dbPoints.Select(p => new
        {
            szSid = p.szSID, szName = p.szName, szUnit = p.szUnit ?? string.Empty, szType = "DB"
        })).Concat(opcUaPoints.Select(p => new
        {
            szSid = p.szSID, szName = p.szName, szUnit = p.szUnit ?? string.Empty, szType = "OpcUa"
        }));
        return Json(allPoints);
    }

    /// <summary>迴路清單（平坦，前端組樹/下拉）</summary>
    [HttpGet("api/circuits")]
    public async Task<IActionResult> GetCircuits()
    {
        var circuits = await _circuitService.GetAllAsync();
        return Ok(circuits.Select(c => new
        {
            id = c.nId,
            name = c.szName,
            parentId = c.nParentId,
            sid = c.szSID,
        }));
    }

    // ---------- 模型 CRUD ----------

    [HttpGet("api/models")]
    public async Task<IActionResult> GetModels()
    {
        return Ok(await _baselineService.GetAllAsync());
    }

    [HttpGet("api/models/{id:int}")]
    public async Task<IActionResult> GetModel(int id)
    {
        var model = await _baselineService.GetByIdAsync(id);
        return model == null ? NotFound() : Ok(model);
    }

    /// <summary>新增/更新模型定義（id null = 新增）。更新僅限草稿，會清空既有回歸結果。</summary>
    [HttpPost("api/models")]
    public async Task<IActionResult> SaveModel([FromBody] BaselineSaveDto dto)
    {
        try
        {
            var model = ToModel(dto);
            if (dto.id == null)
            {
                var nId = await _baselineService.CreateAsync(model);
                return Ok(new { id = nId });
            }
            model.nId = dto.id.Value;
            await _baselineService.UpdateAsync(model);
            return Ok(new { id = model.nId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存基線模型失敗");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("api/models/{id:int}")]
    public async Task<IActionResult> DeleteModel(int id)
    {
        await _baselineService.DeleteAsync(id);
        return Ok();
    }

    /// <summary>執行基線回歸（僅草稿）</summary>
    [HttpPost("api/models/{id:int}/run")]
    public async Task<IActionResult> RunRegression(int id)
    {
        try
        {
            return Ok(await _baselineService.RunRegressionAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基線回歸失敗 Id={Id}", id);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/models/{id:int}/freeze")]
    public async Task<IActionResult> Freeze(int id)
    {
        try
        {
            await _baselineService.FreezeAsync(id);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/models/{id:int}/unfreeze")]
    public async Task<IActionResult> Unfreeze(int id)
    {
        await _baselineService.UnfreezeAsync(id);
        return Ok();
    }

    // ---------- EnPI / 節能量報告 ----------

    [HttpPost("api/enpi/query")]
    public async Task<IActionResult> QueryEnpi([FromBody] EnPIQueryDto dto)
    {
        try
        {
            return Ok(await _enpiService.GetReportAsync(dto.baselineId, dto.start, dto.end));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnPI 報告查詢失敗 BaselineId={Id}", dto.baselineId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/enpi/export")]
    public async Task<IActionResult> ExportEnpi([FromBody] EnPIQueryDto dto)
    {
        try
        {
            var result = await _enpiService.GetReportAsync(dto.baselineId, dto.start, dto.end);
            var szOperator = User.Identity?.Name ?? "anonymous";
            var bytes = _exporter.Export(result, szOperator);
            var szFileName = $"EnPIReport_{result.szBaselineName}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", szFileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnPI 報告匯出失敗 BaselineId={Id}", dto.baselineId);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---------- SEU 重大能源使用鑑別 ----------

    [HttpPost("api/seu")]
    public async Task<IActionResult> QuerySeu([FromBody] SeuQueryDto dto)
    {
        try
        {
            return Ok(await _baselineService.GetSeuAnalysisAsync(dto.start, dto.end, dto.threshold));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SEU 鑑別查詢失敗");
            return BadRequest(new { message = ex.Message });
        }
    }

    private static EnergyBaselineModel ToModel(BaselineSaveDto dto)
    {
        return new EnergyBaselineModel
        {
            szName = dto.name?.Trim() ?? string.Empty,
            szTargetType = dto.targetType,
            szTargetSID = dto.targetType == "point" ? dto.targetSid : null,
            nTargetCircuitId = dto.targetType == "circuit" ? dto.targetCircuitId : null,
            szTargetMode = dto.targetMode,
            szTargetLabel = dto.targetLabel ?? string.Empty,
            szTargetUnit = dto.targetUnit,
            szGranularity = dto.granularity,
            dtBaselineStart = dto.baselineStart,
            dtBaselineEnd = dto.baselineEnd,
            szDescription = dto.description,
            variables = dto.variables.Select(v => new EnergyBaselineVariableModel
            {
                szVarType = v.varType,
                szSourceSID = v.sourceSid,
                nSourceCircuitId = v.sourceCircuitId,
                szLabel = v.label ?? string.Empty,
                szUnit = v.unit,
            }).ToList(),
        };
    }
}
