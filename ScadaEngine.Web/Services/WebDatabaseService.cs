using ScadaEngine.Common.Data.Services;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web專案專用的資料庫管理服務
/// 委派核心功能給Engine專案的服務，僅處理Web特定需求
/// </summary>
public class WebDatabaseService
{
    private readonly ILogger<WebDatabaseService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly DatabaseInitializationService _initService;
    private readonly IDataRepository _dataRepository;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="configService">資料庫配置服務</param>
    /// <param name="initService">資料庫初始化服務</param>
    /// <param name="dataRepository">資料存取介面</param>
    public WebDatabaseService(
        ILogger<WebDatabaseService> logger,
        DatabaseConfigService configService,
        DatabaseInitializationService initService,
        IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _initService = initService ?? throw new ArgumentNullException(nameof(initService));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
    }

    /// <summary>
    /// 檢查資料庫連線狀態 (用於Web介面狀態顯示)
    /// </summary>
    /// <returns>連線狀態資訊</returns>
    public async Task<DatabaseStatusModel> GetDatabaseStatusAsync()
    {
        try
        {
            // 重用Engine的服務來檢查連線
            var isConnected = await _dataRepository.TestConnectionAsync();
            var config = await _configService.LoadConfigAsync();
            
            return new DatabaseStatusModel
            {
                isConnected = isConnected,
                szServerAddress = config.szDatabaseAddress,
                szDatabaseName = config.szDataBaseName,
                dtLastChecked = DateTime.Now,
                szStatusMessage = isConnected ? "資料庫連線正常" : "資料庫連線失敗"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "檢查資料庫狀態時發生錯誤");
            return new DatabaseStatusModel
            {
                isConnected = false,
                szStatusMessage = $"檢查失敗: {ex.Message}",
                dtLastChecked = DateTime.Now
            };
        }
    }

    /// <summary>
    /// 初始化資料庫 (用於Web管理介面)
    /// </summary>
    /// <returns>初始化結果</returns>
    public async Task<bool> InitializeDatabaseAsync()
    {
        try
        {
            _logger.LogInformation("Web介面觸發資料庫初始化");
            
            // 委派給Engine專案的初始化服務
            var result = await _initService.InitializeDatabaseSchemaAsync();
            
            if (result)
            {
                _logger.LogInformation("透過Web介面成功初始化資料庫");
            }
            else
            {
                _logger.LogWarning("透過Web介面初始化資料庫失敗");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web介面資料庫初始化過程發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 測試資料庫連線 (用於設定頁面)
    /// </summary>
    /// <param name="szConnectionString">測試用連線字串</param>
    /// <returns>連線測試結果</returns>
    public async Task<bool> TestConnectionAsync(string szConnectionString)
    {
        try
        {
            // 這裡可以實作測試特定連線字串的邏輯
            // 暫時委派給現有的服務
            return await _dataRepository.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "測試資料庫連線時發生錯誤");
            return false;
        }
    }
}

/// <summary>
/// 資料庫狀態模型 (Web專用)
/// </summary>
public class DatabaseStatusModel
{
    /// <summary>
    /// 是否已連線
    /// </summary>
    public bool isConnected { get; set; } = false;

    /// <summary>
    /// 伺服器位址
    /// </summary>
    public string szServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public string szDatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// 最後檢查時間
    /// </summary>
    public DateTime dtLastChecked { get; set; }

    /// <summary>
    /// 狀態訊息
    /// </summary>
    public string szStatusMessage { get; set; } = string.Empty;
}