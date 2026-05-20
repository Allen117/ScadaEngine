using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.DbCoordinator.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.DbCoordinator.Controllers;

[Authorize]
public class DbCoordinatorController : Controller
{
    private readonly DbCoordinatorService _service;
    private readonly DbCoordinatorReloadPublisher _reloadPublisher;
    private readonly ILogger<DbCoordinatorController> _logger;

    public DbCoordinatorController(
        DbCoordinatorService service,
        DbCoordinatorReloadPublisher reloadPublisher,
        ILogger<DbCoordinatorController> logger)
    {
        _service = service;
        _reloadPublisher = reloadPublisher;
        _logger = logger;
    }

    [HttpGet("/DbCoordinator")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "DB 來源";

        var data = await _service.GetAllAsync();
        var dto = data.Select(d => new DbCoordinatorListItemDto
        {
            id = d.Coordinator.Id,
            name = d.Coordinator.szName,
            pollingInterval = d.Coordinator.nPollingInterval,
            connectTimeout = d.Coordinator.nConnectTimeout,
            monitorEnabled = d.Coordinator.isMonitorEnabled,
            points = d.Points.Select(p => new DbPointListItemDto
            {
                sid = p.szSID,
                sequence = p.nSequence,
                name = p.szName,
                unit = p.szUnit ?? string.Empty,
                min = p.fMin,
                max = p.fMax
            }).ToList()
        }).ToList();

        ViewBag.CoordinatorListJson = System.Text.Json.JsonSerializer.Serialize(dto);
        return View();
    }

    /// <summary>
    /// 觸發 Engine 重新載入 DBPoint/*.json（使用者跑完 Excel 巨集後手動按）
    /// </summary>
    [HttpPost("/DbCoordinator/Reload")]
    public async Task<IActionResult> Reload()
    {
        var isSuccess = await _reloadPublisher.PublishReloadAsync();
        return Json(new
        {
            success = isSuccess,
            message = isSuccess ? "已通知 Engine 重新載入 JSON" : "通知失敗（請確認 MQTT broker 是否運作）"
        });
    }
}
