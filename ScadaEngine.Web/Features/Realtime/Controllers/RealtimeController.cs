using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.Realtime.Models;
using ScadaEngine.Web.Services;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Features.Realtime.Controllers;

/// <summary>
/// 即時監控主頁控制器
/// </summary>
[Authorize]
[Route("[controller]")]
public class RealtimeController : Controller
{
    private readonly ILogger<RealtimeController> _logger;
    private readonly MqttRealtimeSubscriberService _mqttService;
    private readonly IDataRepository _dataRepository;

    public RealtimeController(
        ILogger<RealtimeController> logger,
        MqttRealtimeSubscriberService mqttService,
        IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttService = mqttService ?? throw new ArgumentNullException(nameof(mqttService));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
    }

    /// <summary>
    /// 即時監控頁面
    /// </summary>
    [HttpGet("/RealTime")]
    public async Task<IActionResult> Index()
    {
        var model = new RealtimeMonitorViewModel();

        try
        {
            // DB 來源直讀 DBLatestData，確保初次渲染就拿到最新值
            await _mqttService.RefreshDbSourcesAsync();

            // 載入即時資料
            model.RealtimeDataList = _mqttService.GetAllRealtimeData();
            model.isConnectionHealthy = _mqttService.IsConnected;
            model.dtLastUpdated = model.RealtimeDataList.Any()
                ? model.RealtimeDataList.Max(x => x.dtTimestamp)
                : DateTime.Now;

            // 取得統計資訊
            var stats = _mqttService.GetDataStatistics();
            model.nTotalPoints = stats.total;
            model.nActivePoints = stats.active;

            // 載入左側 Coordinator 清單
            var coordinators = await _dataRepository.GetAllCoordinatorsAsync();
            model.CoordinatorList = [.. coordinators];

            // 載入左側 DBCoordinator 清單
            var dbCoordinators = await _dataRepository.GetAllDbCoordinatorsAsync();
            model.DbCoordinatorList = [.. dbCoordinators];

            // 載入左側 OpcUaCoordinator 清單
            var opcUaCoordinators = await _dataRepository.GetAllOpcUaCoordinatorsAsync();
            model.OpcUaCoordinatorList = [.. opcUaCoordinators];

            // 載入計算點位群組清單與 SID → GroupName 對照（供側欄分群與前端篩選）
            var calcPointsAll = (await _dataRepository.GetAllCalculatedPointsAsync())
                .Where(c => c.isEnabled).ToList();
            model.CalcPointGroups = calcPointsAll
                .Select(c => c.szGroupName)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct().OrderBy(g => g).ToList();
            model.CalcGroupMap = calcPointsAll.ToDictionary(c => c.szSID, c => c.szGroupName ?? "");

            ViewData["PageTitle"] = "SCADA 即時監控儀表板";

            _logger.LogDebug("載入即時監控首頁，總計 {TotalPoints} 個點位，活躍 {ActivePoints} 個",
                           stats.total, stats.active);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入即時監控資料時發生錯誤");
            model.szErrorMessage = "載入資料失敗，請重新整理頁面";
            model.isConnectionHealthy = false;
        }

        return View(model);
    }

    /// <summary>
    /// API: 取得最新即時資料 (AJAX 更新用)
    /// </summary>
    [HttpGet("~/api/realtime/latest")]
    public async Task<IActionResult> GetLatestData()
    {
        try
        {
            // DB 來源直讀 DBLatestData（每次 AJAX 都刷新，沿用 Web 3 秒週期）
            // 手動/自動模式快取同節奏刷新 → ScadaPage 跨分頁切換能在 ≤1 秒內同步 M badge
            await _mqttService.RefreshDbSourcesAsync();
            await _mqttService.RefreshManualAutoMapAsync();

            var realtimeDataList = _mqttService.GetAllRealtimeData();
            var stats = _mqttService.GetDataStatistics();
            var manualAutoMap = _mqttService.GetManualAutoMap();

            var response = new
            {
                success = true,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                connectionStatus = _mqttService.IsConnected,
                statistics = new
                {
                    total = stats.total,
                    active = stats.active
                },
                data = realtimeDataList.Select(item => new
                {
                    subTopic = item.szSubTopic,
                    sid = item.szSID,
                    name = item.szName,
                    value = item.hasData ? Math.Round(item.dValue, 3).ToString() : "--",
                    unit = item.szUnit,
                    quality = item.szQuality,
                    timestamp = item.hasData ? item.dtTimestamp.ToString("yyyy-MM-dd HH:mm:ss") : "--",
                    isRecent = item.isRecent,
                    cssClass = item.CssRowClass,
                    badgeClass = item.QualityBadgeClass,
                    // 控制點位才有 isAuto；非控制點位回 null（前端只在 false 時顯示 M badge）
                    isAuto = manualAutoMap.TryGetValue(item.szSID, out var bIsAuto) ? (bool?)bIsAuto : null
                }).ToArray()
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得最新即時資料時發生錯誤");
            
            return Json(new
            {
                success = false,
                error = "取得資料失敗",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }

    /// <summary>
    /// API: 按 SID 集合取得即時資料（輕量，供 LogicFlow 等畫布使用）
    /// </summary>
    [HttpPost("~/api/realtime/by-sids")]
    public async Task<IActionResult> GetDataBySids([FromBody] List<string> sids)
    {
        try
        {
            if (sids == null || sids.Count == 0)
                return Json(new { success = true, data = Array.Empty<object>() });

            // DB 來源點位不透過 MQTT 更新快取 → 含 DB SID 時直讀 DBLatestData（1 秒節流），
            // 否則 LogicFlow 等畫布只會拿到 Web 啟動時的預填快照（凍結值）
            if (sids.Any(s => s != null && s.StartsWith("DB", StringComparison.Ordinal)))
                await _mqttService.RefreshDbSourcesThrottledAsync();

            var items = _mqttService.GetRealtimeDataBySids(sids);
            return Json(new
            {
                success = true,
                data = items.Select(item => new
                {
                    sid = item.szSID,
                    value = item.hasData ? Math.Round(item.dValue, 3).ToString() : "--",
                    quality = item.szQuality
                }).ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按 SID 查詢即時資料時發生錯誤");
            return Json(new { success = false });
        }
    }

    /// <summary>
    /// API: 取得連線狀態
    /// </summary>
    [HttpGet("~/api/realtime/status")]
    public IActionResult GetConnectionStatus()
    {
        try
        {
            var stats = _mqttService.GetDataStatistics();
            
            return Json(new
            {
                success = true,
                isConnected = _mqttService.IsConnected,
                statistics = new
                {
                    total = stats.total,
                    active = stats.active
                },
                lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得連線狀態時發生錯誤");
            
            return Json(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// API: 取得特定點位歷史
    /// </summary>
    [HttpGet("api/realtime/point/{subTopic}")]
    public IActionResult GetPointData(string subTopic)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subTopic))
            {
                return BadRequest("子主題不可為空");
            }

            var allData = _mqttService.GetAllRealtimeData();
            var pointData = allData.FirstOrDefault(x => x.szSubTopic.Equals(subTopic, StringComparison.OrdinalIgnoreCase));

            if (pointData == null)
            {
                return NotFound($"找不到子主題: {subTopic}");
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    subTopic = pointData.szSubTopic,
                    sid = pointData.szSID,
                    name = pointData.szName,
                    value = pointData.dValue,
                    unit = pointData.szUnit,
                    quality = pointData.szQuality,
                    timestamp = pointData.dtTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    isRecent = pointData.isRecent
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得點位資料時發生錯誤: {SubTopic}", subTopic);
            
            return Json(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}