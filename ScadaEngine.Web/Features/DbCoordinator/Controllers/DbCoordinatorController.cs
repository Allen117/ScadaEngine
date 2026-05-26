using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.DbCoordinator.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.DbCoordinator.Controllers;

[Authorize]
public class DbCoordinatorController : Controller
{
    private readonly DbCoordinatorService _service;
    private readonly DbCoordinatorReloadPublisher _reloadPublisher;
    private readonly ILogger<DbCoordinatorController> _logger;
    private readonly IStringLocalizer<DbCoordinatorController> _l;

    public DbCoordinatorController(
        DbCoordinatorService service,
        DbCoordinatorReloadPublisher reloadPublisher,
        ILogger<DbCoordinatorController> logger,
        IStringLocalizer<DbCoordinatorController> localizer)
    {
        _service = service;
        _reloadPublisher = reloadPublisher;
        _logger = logger;
        _l = localizer;
    }

    [HttpGet("/DbCoordinator")]
    public async Task<IActionResult> Index()
    {
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
            message = isSuccess
                ? _l["dbcoord.api.reload_success"].Value
                : _l["dbcoord.api.reload_failure"].Value
        });
    }
}
