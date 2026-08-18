using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 簡訊盒 MQTT 橋接 — 序列埠是 Engine 獨占資源，Web 無法直接操作，
/// 所有 Web 端動作（測試發送、重掃 port）都經 MQTT 命令由 Engine 代為執行：
///   訂閱 SCADA/Sys/Sms/Cmd（非 retained）：{"action":"test","phoneNumber":"..","label":"..","language":".."}
///                                          {"action":"rescan"}
///   發布 SCADA/Sys/Sms/Status（retained）：簡訊盒連線/訊號/SIM/每日額度 + 最近一次測試結果
/// 狀態於 StatusChanged 事件即時發布，另每 60 秒定期發布（更新額度計數）
/// </summary>
public class SmsMqttBridge : BackgroundService
{
    public const string TOPIC_CMD = "SCADA/Sys/Sms/Cmd";
    public const string TOPIC_STATUS = "SCADA/Sys/Sms/Status";

    private readonly ILogger<SmsMqttBridge> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly MqttPublishService _mqttPublishService;
    private readonly SmsNotificationService _smsService;
    private readonly ISmsTransport _transport;

    private IMqttClient? _mqttClient;
    private bool _isSubscribed = false;
    private bool _disposed = false;

    /// <summary>最近一次測試發送結果（放進 Status payload 供 Web 呈現）</summary>
    private object? _lastTestResult;

    public SmsMqttBridge(
        ILogger<SmsMqttBridge> logger,
        MqttConfigService mqttConfigService,
        MqttPublishService mqttPublishService,
        SmsNotificationService smsService,
        ISmsTransport transport)
    {
        _logger = logger;
        _mqttConfigService = mqttConfigService;
        _mqttPublishService = mqttPublishService;
        _smsService = smsService;
        _transport = transport;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("簡訊盒 MQTT 橋接服務啟動");

        _transport.StatusChanged += OnTransportStatusChanged;

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
                        _logger.LogWarning("簡訊 Cmd 訂閱連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync(mqttSetting.MqttConfig);
                    }

                    await PublishStatusAsync();
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "簡訊 MQTT 橋接監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "簡訊 MQTT 橋接服務執行時發生錯誤");
        }
        finally
        {
            _transport.StatusChanged -= OnTransportStatusChanged;
            await CleanupAsync();
        }
    }

    private void OnTransportStatusChanged(SmsModemStatus status)
    {
        _ = PublishStatusAsync();
    }

    /// <summary>組合並發布狀態（retained，Web 隨時可取得最新快照）</summary>
    private async Task PublishStatusAsync()
    {
        try
        {
            var status = _transport.GetStatus();
            var (nSentToday, nDailyQuota) = _smsService.GetQuotaState();

            var payload = JsonSerializer.Serialize(new
            {
                enabled = _smsService.IsEnabled,
                connected = status.isConnected,
                port = status.szPort,
                baudRate = status.nBaudRate,
                csq = status.nSignalCsq,
                simStatus = status.szSimStatus,
                lastError = status.szLastError,
                sentToday = nSentToday,
                dailyQuota = nDailyQuota,
                lastTest = _lastTestResult,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }, new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false });

            await _mqttPublishService.PublishRawJsonAsync(TOPIC_STATUS, payload, isRetain: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布簡訊盒狀態失敗");
        }
    }

    // ── 命令處理 ──

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var szPayload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            if (string.IsNullOrWhiteSpace(szPayload)) return;

            using var doc = JsonDocument.Parse(szPayload);
            var szAction = doc.RootElement.TryGetProperty("action", out var actionProp)
                ? actionProp.GetString() : null;

            _logger.LogInformation("收到簡訊命令: {Action}", szAction);

            switch (szAction)
            {
                case "test":
                    await HandleTestAsync(doc.RootElement);
                    break;
                case "rescan":
                    await _transport.RescanAsync();
                    await PublishStatusAsync();
                    break;
                default:
                    _logger.LogWarning("未知的簡訊命令: {Action}", szAction);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理簡訊命令失敗");
        }
    }

    private async Task HandleTestAsync(JsonElement root)
    {
        var szPhone = root.TryGetProperty("phoneNumber", out var p) ? p.GetString() ?? "" : "";
        var szLabel = root.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
        var szLanguage = root.TryGetProperty("language", out var g) ? g.GetString() ?? "zh-TW" : "zh-TW";

        if (string.IsNullOrWhiteSpace(szPhone))
        {
            _logger.LogWarning("簡訊測試命令缺少 phoneNumber");
            return;
        }

        var result = await _smsService.SendTestAsync(szPhone, szLabel, szLanguage);
        _lastTestResult = new
        {
            phoneNumber = szPhone,
            label = szLabel,
            success = result.isSuccess,
            error = result.szError,
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        await PublishStatusAsync();
    }

    // ── MQTT 連線（比照 AlarmRuleReloadSubscriber）──

    private async Task InitializeMqttAsync(object mqttConfig)
    {
        try
        {
            dynamic config = mqttConfig;
            _mqttClient = new MqttFactory().CreateMqttClient();

            var clientId = $"ScadaEngine_SmsBridge_{Environment.ProcessId}";
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
                _logger.LogInformation("簡訊 Cmd 訂閱連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化簡訊 Cmd MQTT 連線時發生錯誤");
        }
    }

    private async Task SubscribeTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(TOPIC_CMD);
            _isSubscribed = true;
            _logger.LogInformation("已訂閱簡訊命令主題: {Topic}", TOPIC_CMD);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱簡訊命令主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        if (!_isSubscribed)
        {
            await Task.Delay(500);
            await SubscribeTopicAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isSubscribed = false;
        _logger.LogWarning("簡訊 Cmd MQTT 連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
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
            _logger.LogError(ex, "重新連線簡訊 Cmd MQTT 時發生錯誤");
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
            _logger.LogError(ex, "清理簡訊 Cmd MQTT 連線時發生錯誤");
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
