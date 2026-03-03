using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Services;

/// <summary>
/// SCADA 系統監控服務 - Web專用
/// 提供對Engine核心服務的狀態監控，不重複實作通訊邏輯
/// </summary>
public class ScadaMonitorService
{
    private readonly ILogger<ScadaMonitorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataRepository _dataRepository;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="serviceProvider">服務提供者</param>
    /// <param name="dataRepository">資料存取介面</param>
    public ScadaMonitorService(
        ILogger<ScadaMonitorService> logger,
        IServiceProvider serviceProvider,
        IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
    }

    /// <summary>
    /// 取得系統整體狀態 (用於儀表板顯示)
    /// </summary>
    /// <returns>系統狀態資訊</returns>
    public async Task<SystemStatusModel> GetSystemStatusAsync()
    {
        try
        {
            var status = new SystemStatusModel
            {
                dtLastUpdated = DateTime.Now,
                isSystemRunning = true // 如果能呼叫此方法，表示系統在運行
            };

            // 檢查資料庫狀態
            status.isDatabaseConnected = await _dataRepository.TestConnectionAsync();

            // 檢查MQTT控制服務狀態 (如果Engine在同一進程中運行)
            try
            {
                var mqttControlService = _serviceProvider.GetService<MqttControlSubscribeService>();
                status.isMqttConnected = mqttControlService?.IsConnected ?? false;
                status.nControlCommandCount = mqttControlService?.ControlCommandCount ?? 0;
            }
            catch
            {
                // Engine可能在不同進程，透過資料庫或API查詢狀態
                status.isMqttConnected = false;
            }

            // 從資料庫查詢最新資料狀態
            var latestDataInfo = await GetLatestDataInfoAsync();
            status.nTotalTagCount = latestDataInfo.nTotalTags;
            status.dtLastDataUpdate = latestDataInfo.dtLastUpdate;

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得系統狀態時發生錯誤");
            return new SystemStatusModel
            {
                isSystemRunning = false,
                szErrorMessage = ex.Message,
                dtLastUpdated = DateTime.Now
            };
        }
    }

    /// <summary>
    /// 取得最新的即時資料 (用於監控頁面)
    /// </summary>
    /// <param name="nLimitCount">限制筆數</param>
    /// <returns>最新即時資料清單</returns>
    public async Task<List<RealtimeDataViewModel>> GetLatestRealtimeDataAsync(int nLimitCount = 50)
    {
        try
        {
            // 透過資料庫查詢最新資料，而不是直接存取Engine服務
            var latestData = await _dataRepository.GetLatestDataAsync(nLimitCount);
            
            return latestData.Select(data => new RealtimeDataViewModel
            {
                szSID = data.szSID,
                fValue = data.fValue,
                dtTimestamp = data.dtTimestamp,
                szUnit = GetTagUnit(data.szSID), // 從配置中取得單位
                szTagName = GetTagName(data.szSID), // 從配置中取得點位名稱
                isAlarm = CheckAlarmStatus(data.fValue, data.szSID) // 檢查警報狀態
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得最新即時資料時發生錯誤");
            return new List<RealtimeDataViewModel>();
        }
    }

    /// <summary>
    /// 取得歷史資料 (用於趨勢圖)
    /// </summary>
    /// <param name="szSID">點位ID</param>
    /// <param name="dtStart">開始時間</param>
    /// <param name="dtEnd">結束時間</param>
    /// <returns>歷史資料清單</returns>
    public async Task<List<HistoryDataViewModel>> GetHistoryDataAsync(string szSID, DateTime dtStart, DateTime dtEnd)
    {
        try
        {
            // 透過資料庫查詢歷史資料
            var historyData = await _dataRepository.GetHistoryDataAsync(szSID, dtStart, dtEnd);
            
            return historyData.Select(data => new HistoryDataViewModel
            {
                szSID = data.szSID,
                fValue = data.fValue,
                dtTimestamp = data.dtTimestamp,
                szTagName = GetTagName(data.szSID)
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得歷史資料時發生錯誤: SID={SID}", szSID);
            return new List<HistoryDataViewModel>();
        }
    }

    /// <summary>
    /// 發送控制指令 (透過MQTT)
    /// </summary>
    /// <param name="szSID">點位ID</param>
    /// <param name="dValue">控制值</param>
    /// <returns>發送結果</returns>
    public async Task<bool> SendControlCommandAsync(string szSID, double dValue)
    {
        try
        {
            // 如果Engine在同一進程，可以直接使用MQTT服務
            var mqttControlService = _serviceProvider.GetService<MqttControlSubscribeService>();
            if (mqttControlService != null)
            {
                // 使用測試方法發送控制指令
                await mqttControlService.SendTestControlMessageAsync(szSID, dValue);
                
                _logger.LogInformation("透過Web介面發送控制指令: SID={SID}, Value={Value}", szSID, dValue);
                return true;
            }
            else
            {
                // Engine在不同進程，需要透過API或訊息佇列發送
                _logger.LogWarning("無法取得MQTT控制服務，可能需要透過API發送控制指令");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發送控制指令時發生錯誤: SID={SID}", szSID);
            return false;
        }
    }

    /// <summary>
    /// 私有方法：取得最新資料資訊
    /// </summary>
    /// <returns>資料統計資訊</returns>
    private async Task<DataStatisticsModel> GetLatestDataInfoAsync()
    {
        try
        {
            // 透過資料庫查詢統計資訊
            var latestTimestamp = await _dataRepository.GetLatestTimestampAsync();
            var tagCount = await _dataRepository.GetTotalTagCountAsync();
            
            return new DataStatisticsModel
            {
                nTotalTags = tagCount,
                dtLastUpdate = latestTimestamp
            };
        }
        catch
        {
            return new DataStatisticsModel();
        }
    }

    /// <summary>
    /// 私有方法：取得點位單位 (從配置檔讀取)
    /// </summary>
    /// <param name="szSID">點位ID</param>
    /// <returns>點位單位</returns>
    private string GetTagUnit(string szSID)
    {
        // TODO: 從Modbus配置檔中查詢單位
        return ""; // 暫時回傳空字串
    }

    /// <summary>
    /// 私有方法：取得點位名稱 (從配置檔讀取)
    /// </summary>
    /// <param name="szSID">點位ID</param>
    /// <returns>點位名稱</returns>
    private string GetTagName(string szSID)
    {
        // TODO: 從Modbus配置檔中查詢點位名稱
        return szSID; // 暫時回傳SID
    }

    /// <summary>
    /// 私有方法：檢查警報狀態
    /// </summary>
    /// <param name="fValue">數值</param>
    /// <param name="szSID">點位ID</param>
    /// <returns>是否為警報狀態</returns>
    private bool CheckAlarmStatus(float fValue, string szSID)
    {
        // TODO: 實作警報邏輯
        return false; // 暫時回傳false
    }
}

/// <summary>
/// 系統狀態模型 (Web專用)
/// </summary>
public class SystemStatusModel
{
    public bool isSystemRunning { get; set; } = false;
    public bool isDatabaseConnected { get; set; } = false;
    public bool isMqttConnected { get; set; } = false;
    public int nControlCommandCount { get; set; } = 0;
    public int nTotalTagCount { get; set; } = 0;
    public DateTime dtLastDataUpdate { get; set; }
    public DateTime dtLastUpdated { get; set; }
    public string szErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 即時資料顯示模型 (Web專用)
/// </summary>
public class RealtimeDataViewModel
{
    public string szSID { get; set; } = string.Empty;
    public string szTagName { get; set; } = string.Empty;
    public float fValue { get; set; }
    public string szUnit { get; set; } = string.Empty;
    public DateTime dtTimestamp { get; set; }
    public bool isAlarm { get; set; } = false;
}

/// <summary>
/// 歷史資料顯示模型 (Web專用)
/// </summary>
public class HistoryDataViewModel
{
    public string szSID { get; set; } = string.Empty;
    public string szTagName { get; set; } = string.Empty;
    public float fValue { get; set; }
    public DateTime dtTimestamp { get; set; }
}

/// <summary>
/// 資料統計模型
/// </summary>
public class DataStatisticsModel
{
    public int nTotalTags { get; set; } = 0;
    public DateTime dtLastUpdate { get; set; }
}