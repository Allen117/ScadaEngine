using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端 DB 來源 Reload MQTT 訂閱服務
/// 訂閱 SCADA/Sys/DbCoordinator/Reload — 收到後立即呼叫 DbCommunicationService.ReloadAsync
/// 仿照 AlarmRuleReloadSubscriber 模式
/// </summary>
public class DbCoordinatorReloadSubscriber : BackgroundService
{
    private readonly ILogger<DbCoordinatorReloadSubscriber> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly DbCommunicationService _dbCommunicationService;
    private IMqttClient? _mqttClient;
    private bool _isSubscribed = false;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/DbCoordinator/Reload";

    public DbCoordinatorReloadSubscriber(
        ILogger<DbCoordinatorReloadSubscriber> logger,
        MqttConfigService mqttConfigService,
        DbCommunicationService dbCommunicationService)
    {
        _logger = logger;
        _mqttConfigService = mqttConfigService;
        _dbCommunicationService = dbCommunicationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DB 來源 Reload 訂閱服務啟動 (Engine)");

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
                        _logger.LogWarning("DB 來源 Reload 訂閱連線中斷，嘗試重新連線");
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
                    _logger.LogError(ex, "DB 來源 Reload 訂閱監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DB 來源 Reload 訂閱服務執行時發生錯誤");
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

            var clientId = $"ScadaEngine_DbCoordReload_{Environment.ProcessId}";
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
                _logger.LogInformation("DB 來源 Reload 訂閱連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 DB 來源 Reload MQTT 連線時發生錯誤");
        }
    }

    private async Task SubscribeTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(TOPIC);
            _isSubscribed = true;
            _logger.LogInformation("已訂閱 DB 來源 Reload 主題: {Topic}", TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱 DB 來源 Reload 主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("DB 來源 Reload MQTT 已連線");
        if (!_isSubscribed)
        {
            await Task.Delay(500);
            await SubscribeTopicAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isSubscribed = false;
        _logger.LogWarning("DB 來源 Reload MQTT 連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            _logger.LogInformation("收到 DB 來源 Reload 通知: {Topic}", e.ApplicationMessage.Topic);
            await _dbCommunicationService.ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理 DB 來源 Reload 訊息失敗");
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
            _logger.LogError(ex, "重新連線 DB 來源 Reload MQTT 時發生錯誤");
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
            _logger.LogError(ex, "清理 DB 來源 Reload MQTT 連線時發生錯誤");
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
