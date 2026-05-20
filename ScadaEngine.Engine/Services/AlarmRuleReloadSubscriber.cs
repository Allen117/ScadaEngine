using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端規則異動 MQTT 訂閱服務
/// 訂閱 SCADA/Sys/AlarmRules/Reload — 收到後立即呼叫 AlarmMonitorService.ReloadAndReevaluateAsync
/// 讓 Web 改完規則後 Engine ~1 秒內即可同步觸發 / 恢復事件，不必等 60 秒 Timer 輪詢。
/// </summary>
public class AlarmRuleReloadSubscriber : BackgroundService
{
    private readonly ILogger<AlarmRuleReloadSubscriber> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly AlarmMonitorService _alarmMonitorService;
    private IMqttClient? _mqttClient;
    private bool _isSubscribed = false;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/AlarmRules/Reload";

    public AlarmRuleReloadSubscriber(
        ILogger<AlarmRuleReloadSubscriber> logger,
        MqttConfigService mqttConfigService,
        AlarmMonitorService alarmMonitorService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfigService = mqttConfigService ?? throw new ArgumentNullException(nameof(mqttConfigService));
        _alarmMonitorService = alarmMonitorService ?? throw new ArgumentNullException(nameof(alarmMonitorService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("警報規則 Reload 訂閱服務啟動 (Engine)");

        try
        {
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            await InitializeMqttAsync(mqttSetting.MqttConfig);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_mqttClient?.IsConnected != true)
                    {
                        _logger.LogWarning("規則 Reload 訂閱連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync(mqttSetting.MqttConfig);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "規則 Reload 訂閱監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "規則 Reload 訂閱服務執行時發生錯誤");
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

            var clientId = $"ScadaEngine_AlarmRuleReload_{Environment.ProcessId}";
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
                _logger.LogInformation("規則 Reload 訂閱連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化規則 Reload MQTT 連線時發生錯誤");
        }
    }

    private async Task SubscribeTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(TOPIC);
            _isSubscribed = true;
            _logger.LogInformation("已訂閱規則 Reload 主題: {Topic}", TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱規則 Reload 主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("規則 Reload MQTT 已連線");
        if (!_isSubscribed)
        {
            await Task.Delay(500);
            await SubscribeTopicAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isSubscribed = false;
        _logger.LogWarning("規則 Reload MQTT 連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            // payload 內含 sid 欄位（保留供未來單點 reload 優化用），目前一律全量重評
            var szTopic = e.ApplicationMessage.Topic;
            _logger.LogInformation("收到規則 Reload 通知: {Topic}", szTopic);

            await _alarmMonitorService.ReloadAndReevaluateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理規則 Reload 訊息失敗");
        }
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
            _logger.LogError(ex, "重新連線規則 Reload MQTT 時發生錯誤");
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                if (_mqttClient.IsConnected)
                    await _mqttClient.DisconnectAsync();
                _mqttClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理規則 Reload MQTT 連線時發生錯誤");
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _mqttClient?.Dispose();
            _disposed = true;
        }
        base.Dispose();
    }
}
