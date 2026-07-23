using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.OpcUaCoordinator.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.OpcUaCoordinator.Controllers;

/// <summary>
/// OPC UA 來源設定頁 — Server / Device / 點位全欄位動態編輯（免重啟 Engine）。
/// 存檔流程：驗證 → 回寫 OpcUaPoint/*.json → UPSERT DB → 發 MQTT reload → 同步 Web 快取。
/// </summary>
[Authorize(Roles = "Engineer")]
public class OpcUaCoordinatorController : Controller
{
    private readonly OpcUaCoordinatorService _service;
    private readonly OpcUaReloadPublisher _reloadPublisher;
    private readonly MqttRealtimeSubscriberService _mqttService;
    private readonly ILogger<OpcUaCoordinatorController> _logger;
    private readonly IStringLocalizer<OpcUaCoordinatorController> _l;

    public OpcUaCoordinatorController(
        OpcUaCoordinatorService service,
        OpcUaReloadPublisher reloadPublisher,
        MqttRealtimeSubscriberService mqttService,
        ILogger<OpcUaCoordinatorController> logger,
        IStringLocalizer<OpcUaCoordinatorController> localizer)
    {
        _service = service;
        _reloadPublisher = reloadPublisher;
        _mqttService = mqttService;
        _logger = logger;
        _l = localizer;
    }

    [HttpGet("/OpcUaCoordinator")]
    public async Task<IActionResult> Index()
    {
        var servers = await _service.GetAllAsync();
        ViewBag.ServerListJson = System.Text.Json.JsonSerializer.Serialize(servers);
        return View();
    }

    [HttpPost("/OpcUaCoordinator/SaveServer")]
    public async Task<IActionResult> SaveServer([FromBody] SaveOpcUaServerRequest request)
    {
        var (isSuccess, szMessage, nId) = await _service.SaveServerAsync(request);
        if (!isSuccess)
            return BadRequest(new { success = false, message = szMessage });

        await NotifyEngineAsync();
        return Json(new { success = true, message = szMessage, id = nId });
    }

    [HttpPost("/OpcUaCoordinator/DeleteServer")]
    public async Task<IActionResult> DeleteServer([FromBody] DeleteOpcUaServerRequest request)
    {
        var (isSuccess, szMessage) = await _service.DeleteServerAsync(request.Id);
        if (!isSuccess)
            return BadRequest(new { success = false, message = szMessage });

        await NotifyEngineAsync();
        return Json(new { success = true, message = szMessage });
    }

    [HttpPost("/OpcUaCoordinator/SavePoints")]
    public async Task<IActionResult> SavePoints([FromBody] SaveOpcUaPointsRequest request)
    {
        var (isSuccess, szMessage) = await _service.SavePointsAsync(request);
        if (!isSuccess)
            return BadRequest(new { success = false, message = szMessage });

        await NotifyEngineAsync();
        return Json(new { success = true, message = szMessage });
    }

    /// <summary>
    /// 測試讀取單一 NodeId（不落地、不影響採集）
    /// </summary>
    [HttpPost("/OpcUaCoordinator/TestRead")]
    public async Task<IActionResult> TestRead([FromBody] TestReadOpcUaRequest request)
    {
        var (isSuccess, szMessage) = await _service.TestReadAsync(request);
        return Json(new { success = isSuccess, message = szMessage });
    }

    /// <summary>
    /// 存檔後通知：發 MQTT reload 給 Engine + 同步 Web 端點位快取（新增即出現、刪除即剔除）。
    /// Reload 發布失敗只 log 不擋存檔結果 — JSON/DB 已落地，Engine 重啟後仍會載入。
    /// </summary>
    private async Task NotifyEngineAsync()
    {
        var isPublished = await _reloadPublisher.PublishReloadAsync();
        if (!isPublished)
            _logger.LogWarning("OPC UA Reload 通知發布失敗（設定已存檔，Engine 需等下次重啟或手動 reload 才生效）");

        await _mqttService.SyncOpcUaPointCacheAsync();
    }
}
