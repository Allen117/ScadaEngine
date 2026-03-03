using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Communication.Modbus.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 通訊服務管理器 - 控制通訊服務的啟動和停止
/// 只有在使用者登入後才啟動通訊服務
/// </summary>
public class CommunicationServiceManager
{
    private readonly ILogger<CommunicationServiceManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private bool _servicesStarted = false;
    private readonly object _lockObject = new object();

    // 背景服務引用
    private MqttPublishService? _mqttPublishService;
    private MqttControlSubscribeService? _mqttControlSubscribeService;
    private ModbusCollectionManager? _modbusCollectionManager;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="serviceProvider">服務提供者</param>
    public CommunicationServiceManager(
        ILogger<CommunicationServiceManager> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 啟動所有通訊服務 (登入成功後呼叫)
    /// </summary>
    /// <param name="szUserName">登入使用者名稱</param>
    /// <returns>啟動結果</returns>
    public async Task<bool> StartCommunicationServicesAsync(string szUserName)
    {
        lock (_lockObject)
        {
            if (_servicesStarted)
            {
                _logger.LogInformation("通訊服務已經啟動，跳過重複啟動");
                return true;
            }
        }

        try
        {
            _logger.LogInformation("使用者 {UserName} 登入成功，開始啟動通訊服務...", szUserName);

            // 1. 啟動 MQTT 發布服務
            await StartMqttPublishServiceAsync();

            // 2. 啟動 MQTT 控制訂閱服務
            await StartMqttControlServiceAsync();

            // 3. 啟動 Modbus 採集服務
            await StartModbusCollectionServiceAsync();

            lock (_lockObject)
            {
                _servicesStarted = true;
            }

            _logger.LogInformation("所有通訊服務啟動完成，使用者: {UserName}", szUserName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動通訊服務時發生錯誤，使用者: {UserName}", szUserName);
            await StopCommunicationServicesAsync("啟動失敗，進行清理");
            return false;
        }
    }

    /// <summary>
    /// 停止所有通訊服務 (登出時呼叫)
    /// </summary>
    /// <param name="szReason">停止原因</param>
    /// <returns>停止結果</returns>
    public async Task<bool> StopCommunicationServicesAsync(string szReason = "使用者登出")
    {
        lock (_lockObject)
        {
            if (!_servicesStarted)
            {
                _logger.LogInformation("通訊服務未啟動，無需停止");
                return true;
            }
        }

        try
        {
            _logger.LogInformation("開始停止通訊服務，原因: {Reason}", szReason);

            // 1. 停止 Modbus 採集服務
            await StopModbusCollectionServiceAsync();

            // 2. 停止 MQTT 控制訂閱服務
            await StopMqttControlServiceAsync();

            // 3. 停止 MQTT 發布服務
            await StopMqttPublishServiceAsync();

            lock (_lockObject)
            {
                _servicesStarted = false;
            }

            _logger.LogInformation("所有通訊服務停止完成");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止通訊服務時發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 檢查通訊服務狀態
    /// </summary>
    /// <returns>服務狀態資訊</returns>
    public CommunicationServiceStatus GetServiceStatus()
    {
        lock (_lockObject)
        {
            return new CommunicationServiceStatus
            {
                isServicesStarted = _servicesStarted,
                isMqttPublishConnected = _mqttPublishService?.IsConnected ?? false,
                isMqttControlConnected = _mqttControlSubscribeService?.IsConnected ?? false,
                isModbusCollecting = _modbusCollectionManager != null && _servicesStarted,
                dtLastChecked = DateTime.Now
            };
        }
    }

    #region 私有方法 - 個別服務啟動/停止

    /// <summary>
    /// 啟動 MQTT 發布服務
    /// </summary>
    private async Task StartMqttPublishServiceAsync()
    {
        try
        {
            _mqttPublishService = _serviceProvider.GetService<MqttPublishService>();
            if (_mqttPublishService != null)
            {
                var isInitialized = await _mqttPublishService.InitializeAsync();
                if (isInitialized)
                {
                    _logger.LogInformation("MQTT 發布服務啟動成功");
                }
                else
                {
                    _logger.LogWarning("MQTT 發布服務啟動失敗");
                }
            }
            else
            {
                _logger.LogWarning("無法取得 MQTT 發布服務");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動 MQTT 發布服務時發生錯誤");
            throw;
        }
    }

    /// <summary>
    /// 啟動 MQTT 控制訂閱服務
    /// </summary>
    private async Task StartMqttControlServiceAsync()
    {
        try
        {
            _mqttControlSubscribeService = _serviceProvider.GetService<MqttControlSubscribeService>();
            if (_mqttControlSubscribeService != null)
            {
                // MqttControlSubscribeService 是背景服務，會自動啟動
                // 這裡主要是取得引用以便後續管理
                _logger.LogInformation("MQTT 控制訂閱服務引用已取得");
            }
            else
            {
                _logger.LogWarning("無法取得 MQTT 控制訂閱服務");
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動 MQTT 控制服務時發生錯誤");
            throw;
        }
    }

    /// <summary>
    /// 啟動 Modbus 採集服務
    /// </summary>
    private async Task StartModbusCollectionServiceAsync()
    {
        try
        {
            _modbusCollectionManager = _serviceProvider.GetService<ModbusCollectionManager>();
            if (_modbusCollectionManager != null)
            {
                await _modbusCollectionManager.StartAsync();
                _logger.LogInformation("Modbus 採集服務啟動成功");
            }
            else
            {
                _logger.LogWarning("無法取得 Modbus 採集服務");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動 Modbus 採集服務時發生錯誤");
            throw;
        }
    }

    /// <summary>
    /// 停止 MQTT 發布服務
    /// </summary>
    private async Task StopMqttPublishServiceAsync()
    {
        try
        {
            if (_mqttPublishService != null)
            {
                _mqttPublishService.Dispose();
                _logger.LogInformation("MQTT 發布服務停止完成");
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 MQTT 發布服務時發生錯誤");
        }
    }

    /// <summary>
    /// 停止 MQTT 控制訂閱服務
    /// </summary>
    private async Task StopMqttControlServiceAsync()
    {
        try
        {
            if (_mqttControlSubscribeService != null)
            {
                // MqttControlSubscribeService 會在應用程式關閉時自動停止
                _logger.LogInformation("MQTT 控制訂閱服務標記為停止");
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 MQTT 控制服務時發生錯誤");
        }
    }

    /// <summary>
    /// 停止 Modbus 採集服務
    /// </summary>
    private async Task StopModbusCollectionServiceAsync()
    {
        try
        {
            if (_modbusCollectionManager != null)
            {
                await _modbusCollectionManager.StopAsync();
                _logger.LogInformation("Modbus 採集服務停止完成");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 Modbus 採集服務時發生錯誤");
        }
    }

    #endregion
}

/// <summary>
/// 通訊服務狀態模型
/// </summary>
public class CommunicationServiceStatus
{
    /// <summary>
    /// 服務是否已啟動
    /// </summary>
    public bool isServicesStarted { get; set; } = false;

    /// <summary>
    /// MQTT 發布是否已連線
    /// </summary>
    public bool isMqttPublishConnected { get; set; } = false;

    /// <summary>
    /// MQTT 控制是否已連線
    /// </summary>
    public bool isMqttControlConnected { get; set; } = false;

    /// <summary>
    /// Modbus 是否正在採集
    /// </summary>
    public bool isModbusCollecting { get; set; } = false;

    /// <summary>
    /// 最後檢查時間
    /// </summary>
    public DateTime dtLastChecked { get; set; }
}