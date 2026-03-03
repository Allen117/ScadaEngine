using System.Collections.Concurrent;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Web.Features.Realtime.Models;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Services;

/// <summary>
/// MQTT 即時資料訂閱服務 (Web專用)
/// </summary>
public class MqttRealtimeSubscriberService : BackgroundService, IDisposable
{
    private readonly ILogger<MqttRealtimeSubscriberService> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly IServiceProvider _serviceProvider;
    private IMqttClient? _mqttClient;
    private bool _isConnected = false;
    private bool _disposed = false;

    // 即時資料快取 (執行緒安全)
    private readonly ConcurrentDictionary<string, RealtimeDataItemModel> _realtimeDataCache = new();

    // MQTT 即時資料主題 (格式: SCADA/Realtime/{coordinatorName}/{SID})
    private const string REALTIME_TOPIC = "SCADA/Realtime/+/+";

    // 資料更新事件
    public event Action<RealtimeDataItemModel>? DataUpdated;
    public event Action<List<RealtimeDataItemModel>>? AllDataUpdated;

    public MqttRealtimeSubscriberService(
        ILogger<MqttRealtimeSubscriberService> logger,
        MqttConfigService mqttConfigService,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfigService = mqttConfigService ?? throw new ArgumentNullException(nameof(mqttConfigService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT 即時資料訂閱服務啟動 (Web)");

        try
        {
            // 載入 MQTT 配置
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            var mqttConfig = mqttSetting.MqttConfig;

            await InitializeMqttAsync(mqttConfig);

            // 從資料庫載入全部已設定點位，預填快取（無資料佔位）
            await InitializePointCacheAsync();

            // 持續監控連線狀態
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_mqttClient?.IsConnected != true)
                    {
                        _logger.LogWarning("MQTT 即時資料連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync(mqttConfig);
                    }

                    // 定期清理過期資料
                    CleanupExpiredData();

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT 即時資料服務監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 即時資料服務執行時發生錯誤");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task InitializeMqttAsync(object mqttConfig)
    {
        try
        {
            dynamic config = mqttConfig;
            _mqttClient = new MqttFactory().CreateMqttClient();

            var clientId = $"ScadaWeb_Realtime_{Environment.ProcessId}";
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer((string)config.szBrokerIp, (int)config.nPort)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build();

            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            var result = await _mqttClient.ConnectAsync(options);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("MQTT 即時資料連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeToRealtimeTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 MQTT 即時資料連線時發生錯誤");
        }
    }

    private async Task SubscribeToRealtimeTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(REALTIME_TOPIC);
            _isConnected = true;
            _logger.LogInformation("已訂閱即時資料主題: {Topic}", REALTIME_TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱即時資料主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("MQTT 即時資料已連線");
        if (!_isConnected)
        {
            await Task.Delay(500);
            await SubscribeToRealtimeTopicAsync();
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isConnected = false;
        _logger.LogWarning("MQTT 即時資料連線中斷: {Reason}", e.Reason);
        await Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var szTopic = e.ApplicationMessage.Topic;
            var szPayload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            // 解析子主題
            var subTopic = szTopic.Replace("SCADA/Realtime/", "");

            // 解析 JSON 資料（以大小寫不分的字典處理 PascalCase / camelCase 混用情況）
            using var jsonDoc = JsonDocument.Parse(szPayload);
            var props = jsonDoc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

            // 除錯：記錄原始 JSON 資料
            _logger.LogInformation("收到MQTT原始資料: {Topic} -> {Payload}", szTopic, szPayload);

            // 解析 timestamp：優先嘗試 Unix 毫秒 (long)，次則 ISO 字串
            DateTime parsedTimestamp = DateTime.Now;
            if (props.TryGetValue("timestamp", out var tsProp))
            {
                if (tsProp.ValueKind == JsonValueKind.Number && tsProp.TryGetInt64(out var tsMs))
                    parsedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs).LocalDateTime;
                else if (tsProp.ValueKind == JsonValueKind.String &&
                         DateTime.TryParse(tsProp.GetString(), out var tsDt))
                    parsedTimestamp = tsDt;
            }

            var dataItem = new RealtimeDataItemModel
            {
                szSubTopic = subTopic,
                szSID = props.TryGetValue("sid", out var sidProp) ? sidProp.GetString() ?? "" : "",
                szName = props.TryGetValue("name", out var nameProp) ? nameProp.GetString() ?? "" : subTopic,
                dValue = props.TryGetValue("value", out var valueProp) ?
                        (valueProp.ValueKind == JsonValueKind.Number ? valueProp.GetDouble() :
                         (double.TryParse(valueProp.GetString(), out var dblVal) ? dblVal : 0.0)) : 0.0,
                szQuality = props.TryGetValue("quality", out var qualityProp) ? qualityProp.GetString() ?? "UNKNOWN" : "UNKNOWN",
                dtTimestamp = parsedTimestamp,
                szUnit = props.TryGetValue("unit", out var unitProp) ? unitProp.GetString() ?? "" : "",
                hasData = true
            };

            // 更新快取 (以 SID 為 key，與 InitializePointCacheAsync 預填的 key 格式一致)
            _realtimeDataCache.AddOrUpdate(dataItem.szSID, dataItem, (key, oldValue) => dataItem);

            // 觸發事件
            DataUpdated?.Invoke(dataItem);

            _logger.LogDebug("收到即時資料: {SubTopic} = {Value} {Unit} (品質: {Quality})", 
                           subTopic, dataItem.dValue, dataItem.szUnit, dataItem.szQuality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理即時資料訊息失敗: {Topic}", e.ApplicationMessage.Topic);
        }

        await Task.CompletedTask;
    }

    private async Task ReconnectMqttAsync(object mqttConfig)
    {
        try
        {
            if (_mqttClient != null)
            {
                await _mqttClient.DisconnectAsync();
                await Task.Delay(2000);
                await InitializeMqttAsync(mqttConfig);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新連線 MQTT 即時資料時發生錯誤");
        }
    }

    private void CleanupExpiredData()
    {
        try
        {
            var expiredKeys = _realtimeDataCache
                .Where(kvp => DateTime.Now.Subtract(kvp.Value.dtTimestamp).TotalMinutes > 5)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _realtimeDataCache.TryRemove(key, out _);
            }

            if (expiredKeys.Any())
            {
                _logger.LogDebug("清理過期資料: {Count} 筆", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理過期資料時發生錯誤");
        }
    }

    /// <summary>
    /// 從資料庫讀取所有已設定點位，預填快取（尚未收到 MQTT 資料的點位顯示為 NO_DATA）
    /// </summary>
    private async Task InitializePointCacheAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDataRepository>();
            var allPoints = await repository.GetAllModbusPointsAsync();
            var pointList = allPoints.ToList();

            foreach (var point in pointList)
            {
                var placeholder = new RealtimeDataItemModel
                {
                    szSubTopic = point.szSID,
                    szSID = point.szSID,
                    szName = point.szName,
                    szUnit = point.szUnit,
                    szQuality = "NO_DATA",
                    dValue = 0,
                    dtTimestamp = DateTime.MinValue,
                    hasData = false
                };
                // TryAdd：若 MQTT 已先到資料則不覆蓋
                _realtimeDataCache.TryAdd(point.szSID, placeholder);
            }

            _logger.LogInformation("預填點位快取完成，共 {Count} 個點位", pointList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "預填點位快取時發生錯誤，將等待 MQTT 資料自動建立快取");
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                if (_mqttClient.IsConnected)
                {
                    await _mqttClient.DisconnectAsync();
                }
                _mqttClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理 MQTT 即時資料連線時發生錯誤");
        }
    }

    /// <summary>
    /// 取得所有即時資料
    /// </summary>
    public List<RealtimeDataItemModel> GetAllRealtimeData()
    {
        return _realtimeDataCache.Values.OrderBy(x => x.szSubTopic).ToList();
    }

    /// <summary>
    /// 取得連線狀態
    /// </summary>
    public bool IsConnected => _isConnected && _mqttClient?.IsConnected == true;

    /// <summary>
    /// 取得快取統計
    /// </summary>
    public (int total, int active) GetDataStatistics()
    {
        var total = _realtimeDataCache.Count;
        var active = _realtimeDataCache.Count(kvp => kvp.Value.isRecent);
        return (total, active);
    }

    public new void Dispose()
    {
        if (!_disposed)
        {
            _mqttClient?.Dispose();
            _disposed = true;
        }
        base.Dispose();
    }
}