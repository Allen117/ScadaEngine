using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Web.Features.Realtime.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web 端未恢復警報 MQTT 訂閱服務
/// 訂閱 SCADA/Alarm/Active/+/+，維護記憶體快取供 Realtime 頁面 Panel 即時讀取
/// </summary>
public class MqttAlarmSubscriberService : BackgroundService
{
    private readonly ILogger<MqttAlarmSubscriberService> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly DatabaseConfigService _dbConfigService;
    private IMqttClient? _mqttClient;
    private bool _isConnected = false;
    private bool _disposed = false;

    /// <summary>未恢復警報快取（key = "{szSID}:{szType}"）</summary>
    private readonly ConcurrentDictionary<string, ActiveAlarmItem> _activeAlarmCache = new();

    private const string ALARM_TOPIC = "SCADA/Alarm/Active/+/+";

    public MqttAlarmSubscriberService(
        ILogger<MqttAlarmSubscriberService> logger,
        MqttConfigService mqttConfigService,
        DatabaseConfigService dbConfigService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfigService = mqttConfigService ?? throw new ArgumentNullException(nameof(mqttConfigService));
        _dbConfigService = dbConfigService ?? throw new ArgumentNullException(nameof(dbConfigService));
    }

    public bool IsConnected => _isConnected && _mqttClient?.IsConnected == true;

    /// <summary>
    /// 取得排序後的未恢復警報清單（嚴重度升序、發生時間倒序）
    /// </summary>
    public List<ActiveAlarmItem> GetActiveAlarms()
    {
        return _activeAlarmCache.Values
            .OrderBy(x => x.nSeverity)
            .ThenByDescending(x => x.dtOccurredAt)
            .ToList();
    }

    /// <summary>
    /// 將指定 SID + type 的未恢復警報標記為已確認，同步寫回 EventLog 與記憶體快取
    /// 回傳 true 表示 DB 已更新到至少一筆，false 表示找不到對應未確認警報或失敗
    /// </summary>
    public async Task<bool> AcknowledgeAsync(string szSID, string szType, string szAckBy)
    {
        if (string.IsNullOrWhiteSpace(szSID) || string.IsNullOrWhiteSpace(szType) || string.IsNullOrWhiteSpace(szAckBy))
            return false;

        byte nOperator = szType switch
        {
            "high" => (byte)2,
            "low" => (byte)3,
            "di" => (byte)4,
            _ => (byte)0
        };
        if (nOperator == 0)
        {
            _logger.LogWarning("確認警報失敗，未知 type: {Type}", szType);
            return false;
        }

        try
        {
            var szConn = await _dbConfigService.GetConnectionStringAsync();
            const string szSql = @"
                UPDATE EventLog
                SET IsAcknowledged = 1,
                    AcknowledgedBy = @AckBy,
                    AcknowledgedAt = GETDATE()
                WHERE SID = @SID
                  AND Operator = @Op
                  AND EventType = 0
                  AND ClearedAt IS NULL
                  AND IsAcknowledged = 0";

            using var connection = new SqlConnection(szConn);
            await connection.OpenAsync();
            var nAffected = await connection.ExecuteAsync(szSql, new { SID = szSID, Op = nOperator, AckBy = szAckBy });

            if (nAffected <= 0)
            {
                _logger.LogInformation("確認警報無變更（可能已被確認或不存在）: SID={SID} Type={Type}", szSID, szType);
                return false;
            }

            // 同步快取
            var szKey = $"{szSID}:{szType}";
            if (_activeAlarmCache.TryGetValue(szKey, out var item))
            {
                item.isAcknowledged = true;
                item.szAcknowledgedBy = szAckBy;
            }

            _logger.LogInformation("警報已確認: SID={SID} Type={Type} By={AckBy}", szSID, szType, szAckBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "確認警報失敗: SID={SID} Type={Type}", szSID, szType);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT 警報訂閱服務啟動 (Web)");

        try
        {
            // 啟動時先從 DB 預填快取，避免 Engine 未在線時 Panel 完全沒有資料
            await PreloadFromDbAsync();

            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            await InitializeMqttAsync(mqttSetting.MqttConfig);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_mqttClient?.IsConnected != true)
                    {
                        _logger.LogWarning("MQTT 警報訂閱連線中斷，嘗試重新連線");
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
                    _logger.LogError(ex, "MQTT 警報訂閱監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 警報訂閱服務執行時發生錯誤");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task PreloadFromDbAsync()
    {
        try
        {
            var szConn = await _dbConfigService.GetConnectionStringAsync();
            if (string.IsNullOrEmpty(szConn))
            {
                _logger.LogWarning("無法取得資料庫連線字串，跳過警報快取預填");
                return;
            }

            const string szSql = @"
                SELECT SID            AS szSID,
                       Severity       AS nSeverity,
                       Operator       AS nOperator,
                       Message        AS szMessage,
                       MessageKey     AS szMessageKey,
                       MessageArgs    AS szMessageArgs,
                       TriggerValue   AS dTriggerValue,
                       ThresholdValue AS dThresholdValue,
                       OccurredAt     AS dtOccurredAt,
                       IsAcknowledged AS isAcknowledged,
                       AcknowledgedBy AS szAcknowledgedBy
                FROM EventLog
                WHERE ClearedAt IS NULL
                  AND EventType = 0";

            using var connection = new SqlConnection(szConn);
            await connection.OpenAsync();
            var rows = await connection.QueryAsync(szSql);

            int nCount = 0;
            foreach (var row in rows)
            {
                byte? nOperator = row.nOperator;
                string szType = nOperator switch
                {
                    (byte)2 => "high",
                    (byte)3 => "low",
                    (byte)4 => "di",
                    _ => "unknown"
                };

                var item = new ActiveAlarmItem
                {
                    szSID = (string)(row.szSID ?? string.Empty),
                    szType = szType,
                    nSeverity = (byte)(row.nSeverity ?? (byte)3),
                    szMessage = (string)(row.szMessage ?? string.Empty),
                    szMessageKey = row.szMessageKey as string,
                    szMessageArgs = row.szMessageArgs as string,
                    dTriggerValue = row.dTriggerValue,
                    dThresholdValue = row.dThresholdValue,
                    dtOccurredAt = (DateTime)row.dtOccurredAt,
                    isAcknowledged = (bool)(row.isAcknowledged ?? false),
                    szAcknowledgedBy = row.szAcknowledgedBy as string
                };

                _activeAlarmCache[item.CacheKey] = item;
                nCount++;
            }

            _logger.LogInformation("從 EventLog 預填 {Count} 筆未恢復警報至快取", nCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "預填警報快取失敗，將仰賴 MQTT retained 訊息");
        }
    }

    private async Task InitializeMqttAsync(object mqttConfig)
    {
        try
        {
            dynamic config = mqttConfig;
            _mqttClient = new MqttFactory().CreateMqttClient();

            var clientId = $"ScadaWeb_Alarm_{Environment.ProcessId}";
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
                _logger.LogInformation("MQTT 警報訂閱連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeAlarmTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 MQTT 警報訂閱連線時發生錯誤");
        }
    }

    private async Task SubscribeAlarmTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(ALARM_TOPIC);
            _isConnected = true;
            _logger.LogInformation("已訂閱警報主題: {Topic}", ALARM_TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱警報主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("MQTT 警報訂閱已連線");
        if (!_isConnected)
        {
            await Task.Delay(500);
            await SubscribeAlarmTopicAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isConnected = false;
        _logger.LogWarning("MQTT 警報訂閱連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var szTopic = e.ApplicationMessage.Topic;
            var payloadBytes = e.ApplicationMessage.PayloadSegment;
            var szPayload = payloadBytes.Count > 0
                ? System.Text.Encoding.UTF8.GetString(payloadBytes)
                : string.Empty;

            // Topic 結構: SCADA/Alarm/Active/{SID}/{type}
            var parts = szTopic.Split('/');
            if (parts.Length < 5)
            {
                _logger.LogWarning("警報 topic 格式錯誤: {Topic}", szTopic);
                return;
            }
            var szSID = parts[3];
            var szType = parts[4];
            var szKey = $"{szSID}:{szType}";

            // 空 payload = 警報恢復（清除 retained）
            if (string.IsNullOrEmpty(szPayload))
            {
                if (_activeAlarmCache.TryRemove(szKey, out _))
                {
                    _logger.LogDebug("警報恢復，已從快取移除: {Key}", szKey);
                }
                return;
            }

            using var doc = JsonDocument.Parse(szPayload);
            var props = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

            byte nSeverity = 3;
            if (props.TryGetValue("severity", out var sevProp) &&
                sevProp.ValueKind == JsonValueKind.Number &&
                sevProp.TryGetByte(out var sev))
            {
                nSeverity = sev;
            }

            string szMessage = props.TryGetValue("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String
                ? msgProp.GetString() ?? string.Empty
                : string.Empty;

            string? szMessageKey = props.TryGetValue("messageKey", out var mkProp) && mkProp.ValueKind == JsonValueKind.String
                ? mkProp.GetString()
                : null;

            string? szMessageArgs = props.TryGetValue("messageArgs", out var maProp) && maProp.ValueKind == JsonValueKind.String
                ? maProp.GetString()
                : null;

            double? dTrigger = null;
            if (props.TryGetValue("triggerValue", out var tvProp) && tvProp.ValueKind == JsonValueKind.Number)
                dTrigger = tvProp.GetDouble();

            double? dThreshold = null;
            if (props.TryGetValue("thresholdValue", out var thProp) && thProp.ValueKind == JsonValueKind.Number)
                dThreshold = thProp.GetDouble();

            DateTime dtOccurred = DateTime.Now;
            if (props.TryGetValue("occurredAtMs", out var msProp) &&
                msProp.ValueKind == JsonValueKind.Number &&
                msProp.TryGetInt64(out var ms))
            {
                dtOccurred = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            }
            else if (props.TryGetValue("occurredAt", out var atProp) &&
                     atProp.ValueKind == JsonValueKind.String &&
                     DateTime.TryParse(atProp.GetString(), out var dtParsed))
            {
                dtOccurred = dtParsed;
            }

            bool isAck = false;
            if (props.TryGetValue("isAcknowledged", out var ackProp))
            {
                if (ackProp.ValueKind == JsonValueKind.True) isAck = true;
                else if (ackProp.ValueKind == JsonValueKind.False) isAck = false;
            }

            string? szAckBy = null;
            if (props.TryGetValue("acknowledgedBy", out var ackByProp) && ackByProp.ValueKind == JsonValueKind.String)
                szAckBy = ackByProp.GetString();

            var item = new ActiveAlarmItem
            {
                szSID = szSID,
                szType = szType,
                nSeverity = nSeverity,
                szMessage = szMessage,
                szMessageKey = szMessageKey,
                szMessageArgs = szMessageArgs,
                dTriggerValue = dTrigger,
                dThresholdValue = dThreshold,
                dtOccurredAt = dtOccurred,
                isAcknowledged = isAck,
                szAcknowledgedBy = szAckBy
            };

            _activeAlarmCache.AddOrUpdate(szKey, item, (k, old) => item);
            _logger.LogDebug("警報快取更新: {Key} severity={Sev}", szKey, nSeverity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理警報 MQTT 訊息失敗: {Topic}", e.ApplicationMessage.Topic);
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
            _logger.LogError(ex, "重新連線 MQTT 警報訂閱時發生錯誤");
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
            _logger.LogError(ex, "清理 MQTT 警報訂閱連線時發生錯誤");
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
