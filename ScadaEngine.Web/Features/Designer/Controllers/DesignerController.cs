using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.Designer.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Designer.Controllers;

/// <summary>
/// 畫面設計 — 設計頁與寫入 API 為工程師模式專屬（action-level Roles="Engineer"）；
/// Points / Devices / Load 三個唯讀 API 為執行期共用（ScadaPage 載入設計、EventLog/CalcPoint/LogicFlow 點位選擇器），維持一般 [Authorize]
/// </summary>
[Authorize]
public class DesignerController : Controller
{
    private readonly IDataRepository _repository;
    private readonly ILogger<DesignerController> _logger;
    private readonly IStringLocalizer<DesignerController> _l;
    private readonly DesignerTemplateService _templateService;
    private readonly EnergyCircuitService _circuitService;

    public DesignerController(
        IDataRepository repository,
        ILogger<DesignerController> logger,
        IStringLocalizer<DesignerController> localizer,
        DesignerTemplateService templateService,
        EnergyCircuitService circuitService)
    {
        _repository      = repository;
        _logger          = logger;
        _l               = localizer;
        _templateService = templateService;
        _circuitService  = circuitService;
    }

    [HttpGet("/Designer")]
    [Authorize(Roles = "Engineer")]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// 取得所有可綁定的點位清單（Modbus + 計算點位 + DB 來源點位，供儀錶板選擇）
    /// </summary>
    [HttpGet("/Designer/Points")]
    public async Task<IActionResult> GetPoints()
    {
        var modbusPoints   = await _repository.GetAllModbusPointsAsync();
        var calcPoints     = await _repository.GetAllCalculatedPointsAsync();
        var dbPoints       = await _repository.GetAllDbPointsAsync();
        var dbCoordinators = await _repository.GetAllDbCoordinatorsAsync();
        var dbCoordNameMap = dbCoordinators.ToDictionary(c => c.Id, c => c.szName);
        var opcUaPoints    = await _repository.GetAllOpcUaPointsAsync();
        var opcUaCoordinators = await _repository.GetAllOpcUaCoordinatorsAsync();
        var opcUaCoordNameMap = opcUaCoordinators.ToDictionary(c => c.Id, c => c.szName);

        var allPoints = modbusPoints.Select(p => new
        {
            szSid       = p.szSID,
            szName      = p.szName,
            szUnit      = p.szUnit,
            fMin        = p.fMin ?? 0f,
            fMax        = p.fMax ?? 100f,
            szGroupName = ""
        }).Concat(calcPoints.Where(c => c.isEnabled).Select(c => new
        {
            szSid       = c.szSID,
            szName      = c.szName,
            szUnit      = c.szUnit,
            fMin        = 0f,
            fMax        = 100f,
            szGroupName = c.szGroupName
        })).Concat(dbPoints.Select(p => new
        {
            szSid       = p.szSID,
            szName      = p.szName,
            szUnit      = p.szUnit ?? string.Empty,
            fMin        = p.fMin,
            fMax        = p.fMax,
            szGroupName = dbCoordNameMap.TryGetValue(p.nCoordinatorId, out var szName) ? szName : "DB"
        })).Concat(opcUaPoints.Select(p => new
        {
            szSid       = p.szSID,
            szName      = p.szName,
            szUnit      = p.szUnit ?? string.Empty,
            fMin        = p.fMin ?? 0f,
            fMax        = p.fMax ?? 100f,
            szGroupName = opcUaCoordNameMap.TryGetValue(p.nCoordinatorId, out var szCoordName)
                ? (string.IsNullOrEmpty(p.szDeviceName) ? szCoordName : $"{szCoordName}/{p.szDeviceName}")
                : "OPCUA"
        }));

        return Json(allPoints);
    }

    /// <summary>
    /// 取得所有設備（Coordinator）清單（供儀錶板兩步驟選擇第一步）
    /// </summary>
    [HttpGet("/Designer/Devices")]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _repository.GetAllCoordinatorsAsync();
        return Json(devices.Select(d => new
        {
            nId            = d.Id,
            szName         = d.szName,
            szModbusID     = d.szModbusID,
            szDeviceName   = d.szDeviceName
        }));
    }

    /// <summary>
    /// 取得所有能源迴路（含虛擬節點），供 picker「迴路」分頁組樹 + SID 反查迴路對照表共用。
    /// 五個 SID 欄（kWh/V/A/kW/PF）全部帶回，前端據此做表頭驅動整列自動帶入。
    /// </summary>
    [HttpGet("/Designer/api/circuits")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> GetCircuits()
    {
        var circuits = await _circuitService.GetAllAsync();
        return Json(circuits.Select(c => new
        {
            id             = c.nId,
            name           = c.szName,
            parentId       = c.nParentId,
            sortOrder      = c.nSortOrder,
            sid            = c.szSID,
            maxKwh         = c.dMaxKwh,
            voltageSid     = c.szVoltageSID,
            currentSid     = c.szCurrentSID,
            powerSid       = c.szPowerSID,
            powerFactorSid = c.szPowerFactorSID
        }));
    }

    /// <summary>
    /// 讀取已發布的畫面設計（供 Designer 頁面初始化時還原狀態）
    /// </summary>
    [HttpGet("/Designer/Load")]
    public async Task<IActionResult> Load()
    {
        var pages    = await _repository.LoadPublishedDesignAsync();
        var pageList = pages.ToList();
        return Json(new { hasData = pageList.Any(), pages = pageList });
    }

    /// <summary>
    /// 儲存畫面設計至資料庫
    /// </summary>
    [HttpPost("/Designer/Save")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> Save([FromBody] SaveDesignDto dto)
    {
        try
        {
            var szName = string.IsNullOrWhiteSpace(dto.szName)
                ? _l["designer.untitled_design"].Value
                : dto.szName;
            var isOk = await _repository.SaveDesignAsync(szName, dto.pages);
            return Json(new { success = isOk });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, _l["designer.save_error"].Value);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 取得列範本（分隔符 + 角色順序）
    /// </summary>
    [HttpGet("/Designer/Templates")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> GetTemplates()
    {
        var dto = await _templateService.ReadAsync();
        return Json(new { szSeparator = dto.szSeparator, arrRoles = dto.arrRoles });
    }

    /// <summary>
    /// 整批覆寫列範本（用於「套用並存為預設」）
    /// </summary>
    [HttpPost("/Designer/Templates")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> SaveTemplates([FromBody] DesignerTemplateFileDto dto)
    {
        if (dto == null || dto.arrRoles == null || dto.arrRoles.Count == 0)
        {
            return Json(new { success = false, error = _l["designer.row_template.invalid_payload"].Value });
        }
        var isOk = await _templateService.WriteAsync(dto);
        return Json(new { success = isOk });
    }
}
