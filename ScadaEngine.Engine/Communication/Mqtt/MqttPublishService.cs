using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Communication.Mqtt;

/// <summary>
/// MQTT 發布服務，負責將即時資料推送至 MQTT Broker
/// </summary>
public class MqttPublishService : IDisposable
{
    private readonly ILogger<MqttPublishService> _logger;
    private readonly MqttConfigModel _mqttConfig;
    private IMqttClient? _mqttClient;
    private bool _isConnected = false;
    private bool _disposed = false;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="mqttConfig">MQTT 配置</param>
    public MqttPublishService(ILogger<MqttPublishService> logger, MqttConfigModel mqttConfig)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfig = mqttConfig ?? throw new ArgumentNullException(nameof(mqttConfig));
    }

    /// <summary>
    /// 初始化並連線至 MQTT Broker
    /// </summary>
    /// <returns>連線成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            _mqttClient = new MqttFactory().CreateMqttClient();

            // 設定連線選項
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttConfig.szBrokerIp, _mqttConfig.nPort)
                .WithClientId(_mqttConfig.szClientId)
                .WithCleanSession(false)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithTimeout(TimeSpan.FromSeconds(10))
                .Build();

            // 設定連線事件處理
            _mqttClient.ConnectedAsync += async (e) =>
            {
                _isConnected = true;
                _logger.LogInformation("已成功連線至 MQTT Broker: {BrokerIp}:{Port}", _mqttConfig.szBrokerIp, _mqttConfig.nPort);
                await Task.CompletedTask;
            };

            _mqttClient.DisconnectedAsync += async (e) =>
            {
                _isConnected = false;
                _logger.LogWarning("與 MQTT Broker 連線中斷: {Reason}", e.Reason);
                await Task.CompletedTask;
            };

            // 執行連線
            var result = await _mqttClient.ConnectAsync(options);
            
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("MQTT 用戶端初始化成功");
                return true;
            }
            else
            {
                _logger.LogError("MQTT 連線失敗: {ResultCode}", result.ResultCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 用戶端初始化時發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 發布即時資料至 MQTT Broker
    /// </summary>
    /// <param name="realtimeData">即時資料</param>
    /// <returns>發布成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> PublishRealtimeDataAsync(RealtimeDataModel realtimeData)
    {
        if (!_isConnected || _mqttClient == null)
        {
            _logger.LogWarning("MQTT 用戶端未連線，無法發布資料");
            return false;
        }

        try
        {
            // 建構主題名稱: BaseTopic/CoordinatorName/SID
            var szTopic = $"{_mqttConfig.szBaseTopic}/{realtimeData.szCoordinatorName}/{realtimeData.szSID}";
            
            // 序列化為 JSON
            var payload = JsonSerializer.Serialize(realtimeData.ToMqttPayload(), new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = null,
                WriteIndented = false
            });

            // 建立 MQTT 訊息
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(szTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(_mqttConfig.isRetain)
                .Build();

            // 發布訊息
            var result = await _mqttClient.PublishAsync(message);
            
            if (result.ReasonCode == MqttClientPublishReasonCode.Success)
            {
                //_logger.LogDebug("成功發布資料至主題: {Topic}, 內容: {Payload}", szTopic, payload);
                return true;
            }
            else
            {
                _logger.LogError("發布資料失敗: {ReasonCode}, 主題: {Topic}", result.ReasonCode, szTopic);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布即時資料時發生錯誤: SID={SID}", realtimeData.szSID);
            return false;
        }
    }

    /// <summary>
    /// 批量發布多筆即時資料
    /// </summary>
    /// <param name="realtimeDataList">即時資料清單</param>
    /// <returns>發布成功的數量</returns>
    public async Task<int> PublishBatchRealtimeDataAsync(IEnumerable<RealtimeDataModel> realtimeDataList)
    {
        var nSuccessCount = 0;

        foreach (var data in realtimeDataList)
        {
            if (await PublishRealtimeDataAsync(data))
            {
                nSuccessCount++;
            }
        }

        _logger.LogInformation("批量發布完成: 成功={SuccessCount}, 總計={TotalCount}", nSuccessCount, realtimeDataList.Count());
        return nSuccessCount;
    }

    /// <summary>
    /// 檢查 MQTT 連線狀態
    /// </summary>
    /// <returns>已連線回傳 true，未連線回傳 false</returns>
    public bool IsConnected => _isConnected && _mqttClient?.IsConnected == true;

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _mqttClient?.DisconnectAsync().Wait(5000);
            _mqttClient?.Dispose();
            _disposed = true;
            _logger.LogInformation("MQTT 發布服務已釋放資源");
        }
    }
}