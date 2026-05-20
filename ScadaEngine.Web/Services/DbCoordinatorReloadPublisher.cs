using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web 端 DB 來源 Reload MQTT 通知發布者
/// 對應 Engine 的 DbCoordinatorReloadSubscriber，當使用者透過 Excel 巨集生成新 JSON 後，
/// 在 Web 「DB 來源」頁按「重新載入」即發此通知，Engine 立即重讀 DBPoint/*.json。
///
/// 設計與 AlarmRuleReloadPublisher 一致：QoS=1, Retain=false, 失敗只 log。
/// </summary>
public class DbCoordinatorReloadPublisher : IHostedService, IDisposable
{
    private readonly ILogger<DbCoordinatorReloadPublisher> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/DbCoordinator/Reload";

    public DbCoordinatorReloadPublisher(
        ILogger<DbCoordinatorReloadPublisher> logger,
        MqttConfigService mqttConfigService)
    {
        _logger = logger;
        _mqttConfigService = mqttConfigService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            var config = mqttSetting.MqttConfig;

            _mqttClient = new MqttFactory().CreateMqttClient();
            var clientId = $"ScadaWeb_DbCoordReload_{Environment.ProcessId}";
            _mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(config.szBrokerIp, config.nPort)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build();

            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

            var result = await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
                _logger.LogInformation("DB 來源 Reload 發布者連線成功，ClientId: {ClientId}", clientId);
            else
                _logger.LogWarning("DB 來源 Reload 發布者連線失敗: {Code}", result.ResultCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動 DB 來源 Reload 發布者時發生錯誤（不影響 Web 啟動）");
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
            _logger.LogError(ex, "停止 DB 來源 Reload 發布者時發生錯誤");
        }
    }

    public async Task<bool> PublishReloadAsync()
    {
        if (_mqttClient == null)
        {
            _logger.LogWarning("DB 來源 Reload 發布者尚未初始化");
            return false;
        }

        if (!_mqttClient.IsConnected)
        {
            try
            {
                if (_mqttOptions != null)
                {
                    _logger.LogInformation("DB 來源 Reload 發布者連線中斷，嘗試重連");
                    await _mqttClient.ConnectAsync(_mqttOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DB 來源 Reload 發布者重連失敗");
                return false;
            }
        }

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(TOPIC)
                .WithPayload(string.Empty)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            var result = await _mqttClient.PublishAsync(message);
            if (result.ReasonCode == MqttClientPublishReasonCode.Success)
            {
                _logger.LogInformation("DB 來源 Reload 通知已發布");
                return true;
            }
            _logger.LogWarning("DB 來源 Reload 通知發布失敗: {Code}", result.ReasonCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布 DB 來源 Reload 通知時發生錯誤");
            return false;
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("DB 來源 Reload 發布者連線中斷: {Reason}", e.Reason);
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
