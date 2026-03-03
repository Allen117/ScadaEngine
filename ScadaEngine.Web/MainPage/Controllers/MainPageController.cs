using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.MainPage.Models;
using ScadaEngine.Engine.Data.Interfaces;
using System.Security.Claims;

namespace ScadaEngine.Web.Features.MainPage.Controllers;

/// <summary>
/// 主頁控制器 - 登入後的主要頁面，採用瘦 Controller 設計
/// </summary>
[Authorize]
public class MainPageController : Controller
{
    private readonly ILogger<MainPageController> _logger;
    private readonly IDataRepository _dataRepository;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="dataRepository">資料庫儲存庫</param>
    public MainPageController(ILogger<MainPageController> logger, IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
    }

    /// <summary>
    /// 主頁面 (GET)
    /// </summary>
    /// <returns>主頁視圖</returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var szUserName = User.Identity?.Name ?? "未知使用者";
            var szLoginTime = User.FindFirst("LoginTime")?.Value;
            
            _logger.LogInformation("使用者 {UserName} 進入主頁面", szUserName);

            // 建立視圖模型
            var viewModel = new MainPageViewModel
            {
                szUserName = szUserName,
                dtLoginTime = DateTime.TryParse(szLoginTime, out var loginTime) ? loginTime : DateTime.Now
            };

            // 檢查系統狀態
            await LoadSystemStatusAsync(viewModel.systemStatus);

            ViewData["Title"] = "SCADA 主頁面";
            
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入主頁面時發生錯誤");
            
            // 建立基本的視圖模型
            var fallbackModel = new MainPageViewModel
            {
                szUserName = User.Identity?.Name ?? "未知使用者",
                dtLoginTime = DateTime.Now
            };
            
            ViewData["Title"] = "SCADA 主頁面";
            ViewData["ErrorMessage"] = "載入系統狀態時發生錯誤，請稍後重試。";
            
            return View(fallbackModel);
        }
    }

    /// <summary>
    /// 取得系統狀態 API (AJAX 調用)
    /// </summary>
    /// <returns>系統狀態 JSON</returns>
    [HttpGet]
    public async Task<IActionResult> GetSystemStatus()
    {
        try
        {
            var systemStatus = new SystemStatusModel();
            await LoadSystemStatusAsync(systemStatus);
            
            return Json(new { 
                success = true, 
                data = systemStatus,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得系統狀態時發生錯誤");
            return Json(new { 
                success = false, 
                message = "取得系統狀態失敗",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }

    /// <summary>
    /// 載入系統狀態資訊
    /// </summary>
    /// <param name="systemStatus">系統狀態模型</param>
    /// <returns>非同步任務</returns>
    private async Task LoadSystemStatusAsync(SystemStatusModel systemStatus)
    {
        try
        {
            // 檢查資料庫連線
            systemStatus.isDatabaseConnected = await _dataRepository.TestConnectionAsync();
            
            if (systemStatus.isDatabaseConnected)
            {
                // 取得總點位數量
                systemStatus.nConnectedDevices = await _dataRepository.GetTotalTagCountAsync();
                _logger.LogDebug("已連接設備數量: {DeviceCount}", systemStatus.nConnectedDevices);
            }
            
            // TODO: 檢查 Modbus 通訊狀態
            // systemStatus.isModbusReady = await CheckModbusCommunicationAsync();
            
            // TODO: 檢查 MQTT 發布狀態  
            // systemStatus.isMqttReady = await CheckMqttPublishingAsync();
            
            _logger.LogDebug("系統狀態載入完成: 資料庫={Database}, 設備數={DeviceCount}", 
                           systemStatus.isDatabaseConnected, systemStatus.nConnectedDevices);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "載入系統狀態時發生部分錯誤");
            systemStatus.isDatabaseConnected = false;
        }
    }
}