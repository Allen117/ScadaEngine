using System.Collections.Concurrent;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Services;

/// <summary>
/// 即時值快取（改寫自 Web 的 MqttRealtimeSubscriberService，去掉 Web 專屬部分）：
/// - MQTT 訂閱 SCADA/Realtime/+/+（含 retain 立即補值）為主要更新路徑
/// - 啟動 / 重掃時讀 LatestData 預填（TryAdd，不覆蓋已到的 MQTT 值）
/// - DB 來源點位不走 MQTT，改輪詢 DBLatestData（SQL 成功即 GOOD、失敗全標 BAD）
/// - 訂閱 DbCoordinator / OpcUaCoordinator 的 Reload 主題 → 觸發點位重掃（append-only）
/// </summary>
public class RealtimeCacheService : BackgroundService
{
    private const string REALTIME_TOPIC = "SCADA/Realtime/+/+";
    private const string DB_RELOAD_TOPIC = "SCADA/Sys/DbCoordinator/Reload";
    private const string OPCUA_RELOAD_TOPIC = "SCADA/Sys/OpcUaCoordinator/Reload";

    private readonly ILogger<RealtimeCacheService> _logger;
    private readonly ModbusServerSettingModel _setting;
    private readonly MqttConfigModel _mqttConfig;
    private readonly GatewayRepository _repository;
    private readonly PointCatalogService _catalog;

    private readonly ConcurrentDictionary<string, RealtimeValueModel> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _rescanLock = new(1, 1);
    private IMqttClient? _mqttClient;
    private bool _isSubscribed;

    public RealtimeCacheService(ILogger<RealtimeCacheService> logger, ModbusServerSettingModel setting,
        MqttSettingRootModel mqttSetting, GatewayRepository repository, PointCatalogService catalog)
    {
        _logger = logger;
        _setting = setting;
        _mqttConfig = mqttSetting.MqttConfig;
        _repository = repository;
        _catalog = catalog;
    }

    public bool IsConnected => _isSubscribed && _mqttClient?.IsConnected == true;

    public bool TryGetValue(string szSid, out RealtimeValueModel item)
    {
        var found = _cache.TryGetValue(szSid, out var value);
        item = value!;
        return found;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("即時資料快取服務啟動（MQTT + LatestData 預填 + DBLatestData 輪詢）");

        // 首次掃描：失敗不擋啟動（Modbus server 先照持久化 AddressMap 服務），monitor 迴圈會重試
        await TryRescanAsync("啟動");

        await InitializeMqttAsync();

        var dtLastDbPoll = DateTime.MinValue;
        var dtLastScanRetry = DateTime.Now;
        var dtLastReconnectCheck = DateTime.Now;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.Now - dtLastReconnectCheck >= TimeSpan.FromSeconds(30))
                {
                    dtLastReconnectCheck = DateTime.Now;
                    if (_mqttClient?.IsConnected != true)
                    {
                        _logger.LogWarning("MQTT 連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync();
                    }
                }

                if (!_catalog.HasScannedOk && DateTime.Now - dtLastScanRetry >= TimeSpan.FromSeconds(60))
                {
                    dtLastScanRetry = DateTime.Now;
                    await TryRescanAsync("重試");
                }

                if (DateTime.Now - dtLastDbPoll >= TimeSpan.FromSeconds(Math.Max(1, _setting.nDbPollSeconds)))
                {
                    dtLastDbPoll = DateTime.Now;
                    await RefreshDbSourcesAsync();
                }

                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "快取服務監控迴圈發生錯誤");
                await Task.Delay(5000, stoppingToken);
            }
        }

        await CleanupAsync();
    }

    /// <summary>重掃點位（web「重新掃描點位」按鈕 / Reload MQTT / 啟動時共用）</summary>
    public async Task<(int nAdded, int nTotal)> RescanAsync()
    {
        await _rescanLock.WaitAsync();
        try
        {
            var result = await _catalog.ScanAsync();
            await PrefillFromLatestDataAsync();
            await RefreshDbSourcesAsync();
            return result;
        }
        finally
        {
            _rescanLock.Release();
        }
    }

    private async Task TryRescanAsync(string szReason)
    {
        try
        {
            await RescanAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "點位掃描失敗（{Reason}），沿用既有 AddressMap，稍後重試", szReason);
        }
    }

    /// <summary>非 DB 來源點位以 LatestData 預填（TryAdd — MQTT 已到的值不覆蓋）</summary>
    private async Task PrefillFromLatestDataAsync()
    {
        var latestMap = await _repository.GetLatestDataMapAsync();
        int nFilled = 0;
        foreach (var entry in _catalog.GetSnapshot())
        {
            if (entry.szSource == nameof(PointSource.Db)) continue;
            if (!latestMap.TryGetValue(entry.szSID, out var latest)) continue;

            var item = new RealtimeValueModel
            {
                dValue = latest.fValue,
                szQuality = latest.nQuality == 1 ? "GOOD" : "BAD",
                dtTimestamp = latest.dtTimestamp,
                hasData = true,
            };
            if (_cache.TryAdd(entry.szSID, item)) nFilled++;
        }
        _logger.LogInformation("LatestData 預填完成：{Count} 點", nFilled);
    }

    /// <summary>DB 來源點位輪詢 DBLatestData；SQL 失敗時全部標 BAD（與 Web 同規則）</summary>
    private async Task RefreshDbSourcesAsync()
    {
        var dbEntries = _catalog.GetSnapshot().Where(e => e.szSource == nameof(PointSource.Db)).ToList();
        if (dbEntries.Count == 0) return;

        bool isSqlOk;
        Dictionary<string, Common.Data.Models.LatestDataModel> dbLatestMap;
        try
        {
            dbLatestMap = await _repository.GetDbLatestDataMapAsync();
            isSqlOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "讀取 DBLatestData 失敗，DB 來源點位全數標記 BAD");
            dbLatestMap = new Dictionary<string, Common.Data.Models.LatestDataModel>();
            isSqlOk = false;
        }

        foreach (var entry in dbEntries)
        {
            var hasRow = dbLatestMap.TryGetValue(entry.szSID, out var row);
            _cache[entry.szSID] = new RealtimeValueModel
            {
                dValue = hasRow ? row!.fValue : 0,
                szQuality = isSqlOk ? "GOOD" : "BAD",
                dtTimestamp = hasRow ? row!.dtTimestamp : DateTime.MinValue,
                hasData = isSqlOk && hasRow,
                isFreshBypass = isSqlOk,
            };
        }
    }

    // ────────────────────── MQTT ──────────────────────

    private async Task InitializeMqttAsync()
    {
        try
        {
            _mqttClient = new MqttFactory().CreateMqttClient();

            var szClientId = $"{_mqttConfig.szClientId}_{Environment.ProcessId}";
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttConfig.szBrokerIp, _mqttConfig.nPort)
                .WithClientId(szClientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build();

            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            var result = await _mqttClient.ConnectAsync(options);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("MQTT 連線成功，ClientId: {ClientId}", szClientId);
                await Task.Delay(500);
                await SubscribeTopicsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 MQTT 連線失敗（monitor 迴圈將重試）");
        }
    }

    private async Task SubscribeTopicsAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(REALTIME_TOPIC);
            await _mqttClient!.SubscribeAsync(DB_RELOAD_TOPIC);
            await _mqttClient!.SubscribeAsync(OPCUA_RELOAD_TOPIC);
            _isSubscribed = true;
            _logger.LogInformation("已訂閱主題: {Realtime}, {DbReload}, {OpcReload}",
                REALTIME_TOPIC, DB_RELOAD_TOPIC, OPCUA_RELOAD_TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱 MQTT 主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        if (!_isSubscribed)
        {
            await Task.Delay(500);
            await SubscribeTopicsAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isSubscribed = false;
        _logger.LogWarning("MQTT 連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var szTopic = e.ApplicationMessage.Topic;

            // 點位異動 Reload 通知 → 重掃（append-only，位址不動）
            if (szTopic == DB_RELOAD_TOPIC || szTopic == OPCUA_RELOAD_TOPIC)
            {
                _logger.LogInformation("收到 Reload 通知 ({Topic})，重新掃描點位", szTopic);
                await TryRescanAsync("Reload 通知");
                return;
            }

            var szPayload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            // 大小寫不分解析（broker 上可能同時存在 PascalCase / camelCase 舊 retained 訊息）
            using var jsonDoc = JsonDocument.Parse(szPayload);
            var props = jsonDoc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

            var szSid = props.TryGetValue("sid", out var sidProp) ? sidProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(szSid))
            {
                // 舊 retained 訊息缺 sid → 以 topic 末段補
                var nLastSlash = szTopic.LastIndexOf('/');
                szSid = nLastSlash >= 0 ? szTopic[(nLastSlash + 1)..] : szTopic;
            }

            // DB 來源點位不透過 MQTT 更新（走 DBLatestData 輪詢，避免雙路徑互覆寫）
            if (szSid.StartsWith("DB", StringComparison.Ordinal)) return;

            DateTime dtTimestamp = DateTime.Now;
            if (props.TryGetValue("timestamp", out var tsProp))
            {
                if (tsProp.ValueKind == JsonValueKind.Number && tsProp.TryGetInt64(out var tsMs))
                    dtTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs).LocalDateTime;
                else if (tsProp.ValueKind == JsonValueKind.String &&
                         DateTime.TryParse(tsProp.GetString(), out var tsDt))
                    dtTimestamp = tsDt;
            }

            double dValue = 0;
            if (props.TryGetValue("value", out var valueProp))
            {
                dValue = valueProp.ValueKind == JsonValueKind.Number ? valueProp.GetDouble() :
                    (double.TryParse(valueProp.GetString(), out var dblVal) ? dblVal : 0.0);
            }

            var szQuality = props.TryGetValue("quality", out var qualityProp)
                ? (qualityProp.GetString() ?? "UNKNOWN").ToUpperInvariant()
                : "UNKNOWN";

            _cache[szSid] = new RealtimeValueModel
            {
                dValue = dValue,
                szQuality = szQuality,
                dtTimestamp = dtTimestamp,
                hasData = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "處理 MQTT 訊息失敗: {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task ReconnectMqttAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                await _mqttClient.DisconnectAsync();
                await Task.Delay(2000);
                await InitializeMqttAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 重新連線失敗");
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                if (_mqttClient.IsConnected) await _mqttClient.DisconnectAsync();
                _mqttClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理 MQTT 連線時發生錯誤");
        }
    }
}
