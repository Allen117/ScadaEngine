using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using System.Collections.Concurrent;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Services;

/// <summary>
/// Modbus 採集管理器，負責管理多個設備的獨立執行緒採集作業
/// </summary>
public class ModbusCollectionManager : IDisposable
{
    private readonly ModbusConfigService _configService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModbusCollectionManager> _logger;
    private readonly MqttPublishService? _mqttPublishService;
    
    private readonly ConcurrentDictionary<string, ModbusCommunicationService> _communicationServices = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _collectionTokens = new();
    private readonly ConcurrentDictionary<string, Task> _collectionTasks = new();
    
    private FileSystemWatcher? _configFileWatcher;
    private bool _isDisposed = false;

    /// <summary>
    /// 採集週期 (毫秒)
    /// </summary>
    public int CollectionIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 資料採集事件，當有新資料產生時觸發
    /// </summary>
    public event EventHandler<RealtimeDataCollectedEventArgs>? DataCollected;

    /// <summary>
    /// 設備狀態變更事件
    /// </summary>
    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    public ModbusCollectionManager(
        ModbusConfigService configService,
        IServiceProvider serviceProvider,
        ILogger<ModbusCollectionManager> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 嘗試取得 MQTT 發布服務 (可選)
        _mqttPublishService = _serviceProvider.GetService<MqttPublishService>();
    }

    /// <summary>
    /// 啟動 Modbus 採集服務
    /// </summary>
    public async Task StartAsync()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ModbusCollectionManager));

        try
        {
            _logger.LogInformation("啟動 Modbus 採集管理器");

            // 載入所有設備配置
            var deviceConfigs = await _configService.LoadAllDeviceConfigsAsync();

            if (deviceConfigs.Count == 0)
            {
                _logger.LogWarning("未找到有效的 Modbus 設備配置，採集服務將以空載模式執行");
                return;
            }

            // 為每個設備建立獨立的採集執行緒
            foreach (var config in deviceConfigs)
            {
                StartDeviceCollection(config);
            }

            // 啟動配置檔案監控
            StartConfigFileWatcher();

            _logger.LogInformation("Modbus 採集管理器啟動完成，共管理 {DeviceCount} 個設備", deviceConfigs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動 Modbus 採集管理器時發生錯誤");
            throw;
        }
    }

    /// <summary>
    /// 啟動單個設備的採集執行緒
    /// </summary>
    /// <param name="deviceConfig">設備配置</param>
    private void StartDeviceCollection(ModbusDeviceConfigModel deviceConfig)
    {
        var szDeviceKey = GetDeviceKey(deviceConfig);

        try
        {
            // 建立通訊服務
            var logger = _serviceProvider.GetRequiredService<ILoggerFactory>()
                                        .CreateLogger<ModbusCommunicationService>();
            var communicationService = new ModbusCommunicationService(deviceConfig, logger, _mqttPublishService);

            _communicationServices[szDeviceKey] = communicationService;

            // 建立採集任務的取消令牌
            var cancellationTokenSource = new CancellationTokenSource();
            _collectionTokens[szDeviceKey] = cancellationTokenSource;

            // 啟動採集任務
            var collectionTask = RunDeviceCollectionLoopAsync(communicationService, cancellationTokenSource.Token);
            _collectionTasks[szDeviceKey] = collectionTask;

            _logger.LogInformation("啟動設備採集執行緒: {DeviceIP}:{DevicePort}", deviceConfig.szIP, deviceConfig.nPort);

            // 觸發設備狀態事件
            OnDeviceStatusChanged(szDeviceKey, "Started", communicationService.GetDeviceStatus());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動設備 {DeviceIP} 採集執行緒失敗", deviceConfig.szIP);
        }
    }

    /// <summary>
    /// 執行設備採集迴圈
    /// </summary>
    /// <param name="communicationService">通訊服務</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task RunDeviceCollectionLoopAsync(ModbusCommunicationService communicationService, CancellationToken cancellationToken)
    {
        var szDeviceKey = GetDeviceKey(communicationService.DeviceIP, communicationService.DevicePort);

        _logger.LogDebug("開始設備採集迴圈: {DeviceIP}:{DevicePort}", communicationService.DeviceIP, communicationService.DevicePort);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 檢查連線狀態
                    if (!communicationService.IsConnected)
                    {
                        if (communicationService.ShouldReconnect())
                        {
                            _logger.LogInformation("嘗試重新連線設備: {DeviceIP}", communicationService.DeviceIP);
                            
                            var isConnected = await communicationService.ConnectAsync();
                            
                            OnDeviceStatusChanged(szDeviceKey, 
                                isConnected ? "Connected" : "Disconnected", 
                                communicationService.GetDeviceStatus());
                        }
                    }

                    // 執行資料採集（即使未連線也要嘗試，以產生 Bad Quality 資料）
                    var realtimeDataList = await communicationService.ReadAllTagsAsync();

                    if (realtimeDataList.Count > 0)
                    {
                        // 觸發資料採集事件
                        OnDataCollected(szDeviceKey, realtimeDataList);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，跳出迴圈
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "設備採集發生錯誤: {DeviceIP}", communicationService.DeviceIP);
                    
                    OnDeviceStatusChanged(szDeviceKey, "Error", new { 
                        Error = ex.Message, 
                        Status = communicationService.GetDeviceStatus() 
                    });
                }

                // 等待下一次採集週期
                try
                {
                    await Task.Delay(CollectionIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // 斷開連線
            await communicationService.DisconnectAsync();
            OnDeviceStatusChanged(szDeviceKey, "Stopped", communicationService.GetDeviceStatus());
            
            _logger.LogDebug("設備採集迴圈結束: {DeviceIP}:{DevicePort}", communicationService.DeviceIP, communicationService.DevicePort);
        }
    }

    /// <summary>
    /// 啟動配置檔案監控
    /// </summary>
    private void StartConfigFileWatcher()
    {
        try
        {
            _configFileWatcher = _configService.CreateConfigFileWatcher(async szChangedFilePath =>
            {
                _logger.LogInformation("配置檔案變更，重新載入: {FilePath}", szChangedFilePath);
                
                try
                {
                    await ReloadDeviceConfigAsync(szChangedFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新載入配置檔案失敗: {FilePath}", szChangedFilePath);
                }
            });

            _logger.LogInformation("配置檔案監控已啟動");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動配置檔案監控失敗");
        }
    }

    /// <summary>
    /// 重新載入設備配置
    /// </summary>
    /// <param name="szConfigFilePath">配置檔案路徑</param>
    private async Task ReloadDeviceConfigAsync(string szConfigFilePath)
    {
        try
        {
            var newConfig = await _configService.ReloadDeviceConfigAsync(szConfigFilePath);
            
            if (newConfig == null)
            {
                _logger.LogWarning("重新載入的配置檔案無效: {FilePath}", szConfigFilePath);
                return;
            }

            var szDeviceKey = GetDeviceKey(newConfig);

            // 停止舊的採集任務
            await StopDeviceCollectionAsync(szDeviceKey);

            // 啟動新的採集任務
            StartDeviceCollection(newConfig);

            _logger.LogInformation("設備配置重新載入完成: {DeviceIP}", newConfig.szIP);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新載入設備配置時發生錯誤");
        }
    }

    /// <summary>
    /// 停止指定設備的採集
    /// </summary>
    /// <param name="szDeviceKey">設備鍵值</param>
    private async Task StopDeviceCollectionAsync(string szDeviceKey)
    {
        try
        {
            // 取消採集任務
            if (_collectionTokens.TryRemove(szDeviceKey, out var tokenSource))
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            // 等待採集任務結束
            if (_collectionTasks.TryRemove(szDeviceKey, out var task))
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略例外
                }
            }

            // 釋放通訊服務
            if (_communicationServices.TryRemove(szDeviceKey, out var commService))
            {
                commService.Dispose();
            }

            _logger.LogInformation("停止設備採集: {DeviceKey}", szDeviceKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止設備採集時發生錯誤: {DeviceKey}", szDeviceKey);
        }
    }

    /// <summary>
    /// 停止所有採集服務
    /// </summary>
    public async Task StopAsync()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("停止 Modbus 採集管理器");

        try
        {
            // 停止配置檔案監控
            _configFileWatcher?.Dispose();
            _configFileWatcher = null;

            // 停止所有設備採集
            var stopTasks = _communicationServices.Keys.Select(StopDeviceCollectionAsync);
            await Task.WhenAll(stopTasks);

            _logger.LogInformation("Modbus 採集管理器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 Modbus 採集管理器時發生錯誤");
        }
    }

    /// <summary>
    /// 取得所有設備狀態
    /// </summary>
    /// <returns>設備狀態字典</returns>
    public Dictionary<string, object> GetAllDeviceStatus()
    {
        var statusDict = new Dictionary<string, object>();

        foreach (var kvp in _communicationServices)
        {
            try
            {
                statusDict[kvp.Key] = kvp.Value.GetDeviceStatus();
            }
            catch (Exception ex)
            {
                statusDict[kvp.Key] = new { Error = ex.Message };
            }
        }

        return statusDict;
    }

    /// <summary>
    /// 產生設備唯一鍵值
    /// </summary>
    /// <param name="deviceConfig">設備配置</param>
    /// <returns>設備鍵值</returns>
    private static string GetDeviceKey(ModbusDeviceConfigModel deviceConfig)
    {
        return GetDeviceKey(deviceConfig.szIP, deviceConfig.nPort);
    }

    /// <summary>
    /// 產生設備唯一鍵值
    /// </summary>
    /// <param name="szIP">IP 地址</param>
    /// <param name="nPort">埠號</param>
    /// <returns>設備鍵值</returns>
    private static string GetDeviceKey(string szIP, int nPort)
    {
        return $"{szIP}:{nPort}";
    }

    /// <summary>
    /// 觸發資料採集事件
    /// </summary>
    /// <param name="szDeviceKey">設備鍵值</param>
    /// <param name="realtimeDataList">即時資料清單</param>
    private void OnDataCollected(string szDeviceKey, List<RealtimeDataModel> realtimeDataList)
    {
        try
        {
            DataCollected?.Invoke(this, new RealtimeDataCollectedEventArgs(szDeviceKey, realtimeDataList));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "觸發資料採集事件時發生錯誤");
        }
    }

    /// <summary>
    /// 觸發設備狀態變更事件
    /// </summary>
    /// <param name="szDeviceKey">設備鍵值</param>
    /// <param name="szStatus">狀態描述</param>
    /// <param name="statusData">狀態資料</param>
    private void OnDeviceStatusChanged(string szDeviceKey, string szStatus, object statusData)
    {
        try
        {
            DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(szDeviceKey, szStatus, statusData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "觸發設備狀態變更事件時發生錯誤");
        }
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            Task.Run(async () =>
            {
                await StopAsync();
            }).Wait(TimeSpan.FromSeconds(10));
        }
    }
}

/// <summary>
/// 即時資料採集事件參數
/// </summary>
public class RealtimeDataCollectedEventArgs : EventArgs
{
    public string DeviceKey { get; }
    public List<RealtimeDataModel> RealtimeDataList { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public RealtimeDataCollectedEventArgs(string deviceKey, List<RealtimeDataModel> realtimeDataList)
    {
        DeviceKey = deviceKey;
        RealtimeDataList = realtimeDataList;
    }
}

/// <summary>
/// 設備狀態變更事件參數
/// </summary>
public class DeviceStatusChangedEventArgs : EventArgs
{
    public string DeviceKey { get; }
    public string Status { get; }
    public object StatusData { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public DeviceStatusChangedEventArgs(string deviceKey, string status, object statusData)
    {
        DeviceKey = deviceKey;
        Status = status;
        StatusData = statusData;
    }
}