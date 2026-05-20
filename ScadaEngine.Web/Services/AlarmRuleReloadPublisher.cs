using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web 端規則異動 MQTT 通知發布者
/// 對應 Engine 的 AlarmRuleReloadSubscriber，當使用者新增 / 修改 / 刪除規則後呼叫，
/// Engine 收到後立即重載規則並對 LatestData 重評，達到「~1 秒內生效」。
///
/// 設計重點：
/// - QoS=1, Retain=false（控制訊號不殘留，避免 Engine 重啟時又跑一次最後一筆 reload）
/// - 失敗只記 log，不拋例外（DB 已成功寫入，異動不能因 MQTT 失敗回滾；60 秒 Timer 是退路）
/// - Singleton + IHostedService（啟動時連 broker、結束時 disconnect）
/// </summary>
public class AlarmRuleReloadPublisher : IHostedService, IDisposable
{
    private readonly ILogger<AlarmRuleReloadPublisher> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/AlarmRules/Reload";

    public AlarmRuleReloadPublisher(
        ILogger<AlarmRuleReloadPublisher> logger,
        MqttConfigService mqttConfigService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfigService = mqttConfigService ?? throw new ArgumentNullException(nameof(mqttConfigService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            var config = mqttSetting.MqttConfig;

            _mqttClient = new MqttFactory().CreateMqttClient();
            var clientId = $"ScadaWeb_AlarmRuleReload_{Environment.ProcessId}";
            _mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(config.szBrokerIp, config.nPort)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build();

            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

            var result = await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("規則 Reload 發布者連線成功，ClientId: {ClientId}", clientId);
            }
            else
            {
                _logger.LogWarning("規則 Reload 發布者連線失敗: {Code}", result.ResultCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動規則 Reload 發布者時發生錯誤（不影響 Web 啟動）");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_mqttClient != null && _mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止規則 Reload 發布者時發生錯誤");
        }
    }

    /// <summary>
    /// 發布規則異動通知給 Engine。
    /// </summary>
    /// <param name="szSID">異動的點位 SID（保留欄位供未來單點 reload 優化用，目前 Engine 一律全量重評）；
    /// 傳入 null 表示批次或不指定點位</param>
    public async Task PublishReloadAsync(string? szSID = null)
    {
        if (_mqttClient == null)
        {
            _logger.LogWarning("規則 Reload 發布者尚未初始化，跳過通知（DB 已寫入；60 秒 Timer 退路會接手）");
            return;
        }

        // 連線中斷時嘗試一次重連
        if (!_mqttClient.IsConnected)
        {
            try
            {
                if (_mqttOptions != null)
                {
                    _logger.LogInformation("規則 Reload 發布者連線中斷，嘗試重連");
                    await _mqttClient.ConnectAsync(_mqttOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "規則 Reload 發布者重連失敗，跳過通知（DB 已寫入；60 秒 Timer 退路會接手）");
                return;
            }
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { sid = szSID },
                new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(TOPIC)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            var result = await _mqttClient.PublishAsync(message);
            if (result.ReasonCode == MqttClientPublishReasonCode.Success)
                _logger.LogDebug("規則 Reload 通知已發布: SID={SID}", szSID ?? "(all)");
            else
                _logger.LogWarning("規則 Reload 通知發布失敗: {Code}", result.ReasonCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布規則 Reload 通知時發生錯誤（不影響規則異動主流程）");
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("規則 Reload 發布者連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _mqttClient?.Dispose();
            _disposed = true;
        }
    }
}
