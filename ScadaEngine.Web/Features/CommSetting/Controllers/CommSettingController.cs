using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Features.CommSetting.Controllers;

[Authorize]
public class CommSettingController : Controller
{
    private readonly IDataRepository _repository;

    public CommSettingController(IDataRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("/CommSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "通訊設定";
        var coordinators = await _repository.GetAllCoordinatorsAsync();
        return View(coordinators.ToList());
    }

    [HttpPost("/CommSetting/UpdateDeviceName")]
    public async Task<IActionResult> UpdateDeviceName([FromBody] UpdateDeviceNameRequest request)
    {
        if (request == null || request.Id <= 0)
            return BadRequest(new { success = false, message = "參數錯誤" });

        var isSuccess = await _repository.UpdateDeviceNameAsync(request.Id, request.DeviceName ?? "");
        if (isSuccess)
            return Ok(new { success = true });

        return StatusCode(500, new { success = false, message = "更新失敗" });
    }

    public class UpdateDeviceNameRequest
    {
        public int Id { get; set; }
        public string? DeviceName { get; set; }
    }
}
