using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.ModbusCoordinator.Models;

namespace ScadaEngine.Web.Features.ModbusCoordinator.Controllers;

[Authorize]
public class ModbusCoordinatorController : Controller
{
    private readonly IDataRepository _repository;
    private readonly IStringLocalizer<ModbusCoordinatorController> _l;

    public ModbusCoordinatorController(
        IDataRepository repository,
        IStringLocalizer<ModbusCoordinatorController> localizer)
    {
        _repository = repository;
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

}
