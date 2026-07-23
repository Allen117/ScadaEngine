using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.ModbusCoordinator.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ModbusCoordinator.Controllers;

[Authorize(Roles = "Engineer")]
public class ModbusCoordinatorController : Controller
{
    private readonly IDataRepository _repository;
    private readonly ModbusConfigFileService _configFileService;
    private readonly ControlEventLogger _controlEventLogger;
    private readonly IStringLocalizer<ModbusCoordinatorController> _l;

    public ModbusCoordinatorController(
        IDataRepository repository,
        ModbusConfigFileService configFileService,
        ControlEventLogger controlEventLogger,
        IStringLocalizer<ModbusCoordinatorController> localizer)
    {
        _repository = repository;
        _configFileService = configFileService;
        _controlEventLogger = controlEventLogger;
        _l = localizer;
    }

    [HttpGet("/ModbusCoordinator")]
    public async Task<IActionResult> Index()
    {
        var coordinators = await _repository.GetAllCoordinatorsAsync();
        return View(coordinators.ToList());
    }

    [HttpPost("/ModbusCoordinator/UpdateDeviceName")]
    public async Task<IActionResult> UpdateDeviceName([FromBody] UpdateDeviceNameRequest request)
    {
        if (request == null || request.Id <= 0)
            return BadRequest(new { success = false, message = _l["modbuscoordinator.api.param_error"].Value });

        var isSuccess = await _repository.UpdateDeviceNameAsync(request.Id, request.DeviceName ?? "");
        if (isSuccess)
            return Ok(new { success = true });

        return StatusCode(500, new { success = false, message = _l["modbuscoordinator.api.update_failed"].Value });
    }

    /// <summary>讀取指定設備（JSON 檔）的點位清單 — 點位熱編輯用，限 Engineer</summary>
    [HttpGet("/ModbusCoordinator/Points/{name}")]
    public async Task<IActionResult> Points(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { success = false, message = _l["modbuscoordinator.api.param_error"].Value });

        var model = await _configFileService.GetPointsAsync(name);
        if (model == null)
            return NotFound(new { success = false, message = _l["modbuscoordinator.api.file_not_found"].Value });

        return Ok(new
        {
            success = true,
            coordinatorName = model.CoordinatorName,
            ip = model.IP,
            port = model.Port,
            modbusId = model.ModbusId,
            connectTimeout = model.ConnectTimeout,
            points = model.Points,
        });
    }

    /// <summary>原地更新點位欄位（數量/順序/DataType 鎖死）— 限 Engineer，成功後每個變更點位寫一筆 EventLog 稽核</summary>
    [HttpPost("/ModbusCoordinator/UpdatePoints")]
    public async Task<IActionResult> UpdatePoints([FromBody] UpdateModbusPointsRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CoordinatorName)
            || request.Points == null || request.Points.Count == 0)
        {
            return BadRequest(new { success = false, message = _l["modbuscoordinator.api.param_error"].Value });
        }

        var result = await _configFileService.UpdatePointsAsync(request.CoordinatorName, request.Points);

        if (!result.isSuccess)
        {
            return result.nError switch
            {
                ModbusPointsUpdateError.FileNotFound =>
                    NotFound(new { success = false, message = _l["modbuscoordinator.api.file_not_found"].Value }),
                ModbusPointsUpdateError.StructureChanged =>
                    BadRequest(new { success = false, message = _l["modbuscoordinator.api.structure_changed"].Value }),
                ModbusPointsUpdateError.InvalidPoint =>
                    BadRequest(new { success = false, message = $"{_l["modbuscoordinator.api.invalid_point"].Value} (#{result.nInvalidRow}: {result.szInvalidReason})" }),
                _ =>
                    StatusCode(500, new { success = false, message = _l["modbuscoordinator.api.save_failed"].Value }),
            };
        }

        // EventLog 稽核 — SID 依 Engine 規則：{Id*65536 + 首個ModbusId*256 + 1}-S{index+1}
        if (result.changes.Count > 0)
        {
            var coordinator = (await _repository.GetAllCoordinatorsAsync())
                .FirstOrDefault(c => string.Equals(c.szName, request.CoordinatorName, StringComparison.OrdinalIgnoreCase));

            if (coordinator != null)
            {
                var szFirstModbusId = coordinator.szModbusID
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                var nFirstModbusId = int.TryParse(szFirstModbusId, out var nId) ? nId : 1;
                var szUsername = User.Identity?.Name ?? "anonymous";

                foreach (var change in result.changes)
                {
                    var szSid = $"{coordinator.Id * 65536 + nFirstModbusId * 256 + 1}-S{change.nTagIndex + 1}";
                    await _controlEventLogger.LogPointConfigChangedAsync(szSid, change.szPointName, change.szSummary, szUsername);
                }
            }
        }

        return Ok(new { success = true, changedCount = result.changes.Count });
    }
}
