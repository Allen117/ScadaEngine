using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.DbCoordinator.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.DbCoordinator.Controllers;

[Authorize(Roles = "Engineer")]
public class DbCoordinatorController : Controller
{
    private readonly DbCoordinatorService _service;
    private readonly DbPointConfigFileService _pointConfigService;
    private readonly DbCoordinatorReloadPublisher _reloadPublisher;
    private readonly ILogger<DbCoordinatorController> _logger;
    private readonly IStringLocalizer<DbCoordinatorController> _l;

    public DbCoordinatorController(
        DbCoordinatorService service,
        DbPointConfigFileService pointConfigService,
        DbCoordinatorReloadPublisher reloadPublisher,
        ILogger<DbCoordinatorController> logger,
        IStringLocalizer<DbCoordinatorController> localizer)
    {
        _service = service;
        _pointConfigService = pointConfigService;
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
    /// 更新單一點位（名稱 + 單位）：回寫 DBPoint/*.json + UPSERT DBPoints，成功後發 reload MQTT 讓 Engine 熱重載
    /// </summary>
    [HttpPost("/DbCoordinator/UpdatePoint")]
    public async Task<IActionResult> UpdatePoint([FromBody] UpdatePointRequest request)
    {
        var (isSuccess, szMessage) = await _pointConfigService.UpdatePointAsync(
            request.Id, request.Sequence, request.NewName, request.NewUnit);

        var isReloadSent = false;
        if (isSuccess)
        {
            isReloadSent = await _reloadPublisher.PublishReloadAsync();
            if (!isReloadSent)
                _logger.LogWarning("點位更新成功但 reload MQTT 發布失敗: CoordinatorId={Id}, Seq={Seq}",
                    request.Id, request.Sequence);
        }

        return Json(new
        {
            success = isSuccess,
            message = isSuccess && !isReloadSent
                ? _l["dbcoord.api.rename_saved_reload_failed"].Value
                : szMessage,
            reloadSent = isReloadSent
        });
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
