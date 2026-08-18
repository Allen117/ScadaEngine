using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web 端簡訊命令 MQTT 發布者
/// 序列埠簡訊盒是 Engine 獨占資源，Web 無法直接開 port，
/// 測試發送 / 重掃 port 一律發 MQTT 命令到 SCADA/Sys/Sms/Cmd 由 Engine 代為執行，
/// 執行結果由 Engine 寫 EventLog 並更新 SCADA/Sys/Sms/Status（retained）。
/// 連線模式比照 AlarmRuleReloadPublisher（QoS=1、Retain=false、失敗只記 log）。
/// </summary>
public class SmsCommandPublisher : IHostedService, IDisposable
{
    private readonly ILogger<SmsCommandPublisher> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/Sms/Cmd";

    public SmsCommandPublisher(
        ILogger<SmsCommandPublisher> logger,
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
            var clientId = $"ScadaWeb_SmsCmd_{Environment.ProcessId}";
            _mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(config.szBrokerIp, config.nPort)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build();

            var result = await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
                _logger.LogInformation("簡訊命令發布者連線成功，ClientId: {ClientId}", clientId);
            else
                _logger.LogWarning("簡訊命令發布者連線失敗: {Code}", result.ResultCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動簡訊命令發布者時發生錯誤（不影響 Web 啟動）");
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
            _logger.LogError(ex, "停止簡訊命令發布者時發生錯誤");
        }
    }

    /// <summary>發布測試發送命令（Engine 收到後實際發簡訊）</summary>
    public Task<bool> PublishTestAsync(string szPhoneNumber, string szLabel, string szLanguage) =>
        PublishAsync(new { action = "test", phoneNumber = szPhoneNumber, label = szLabel, language = szLanguage });

    /// <summary>發布重掃 COM port 命令</summary>
    public Task<bool> PublishRescanAsync() =>
        PublishAsync(new { action = "rescan" });

    private async Task<bool> PublishAsync(object payloadObj)
    {
        if (_mqttClient == null)
        {
            _logger.LogWarning("簡訊命令發布者尚未初始化，無法送出命令");
            return false;
        }

        if (!_mqttClient.IsConnected)
        {
            try
            {
                if (_mqttOptions != null)
                    await _mqttClient.ConnectAsync(_mqttOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "簡訊命令發布者重連失敗，命令未送出");
                return false;
            }
        }

        try
        {
            var payload = JsonSerializer.Serialize(payloadObj,
                new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(TOPIC)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            var result = await _mqttClient.PublishAsync(message);
            if (result.ReasonCode == MqttClientPublishReasonCode.Success)
            {
                _logger.LogDebug("簡訊命令已發布: {Payload}", payload);
                return true;
            }
            _logger.LogWarning("簡訊命令發布失敗: {Code}", result.ReasonCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布簡訊命令時發生錯誤");
            return false;
        }
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
