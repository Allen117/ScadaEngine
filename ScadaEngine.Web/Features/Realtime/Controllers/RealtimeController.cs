using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.Realtime.Models;
using ScadaEngine.Web.Services;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Features.Realtime.Controllers;

/// <summary>
/// 即時監控主頁控制器
/// </summary>
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
    public IActionResult GetLatestData()
    {
        try
        {
            var realtimeDataList = _mqttService.GetAllRealtimeData();
            var stats = _mqttService.GetDataStatistics();

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
                    badgeClass = item.QualityBadgeClass
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