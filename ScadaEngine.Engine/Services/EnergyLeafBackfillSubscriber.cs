using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端葉子層 hourly Backfill MQTT 訂閱服務。
/// 訂閱 SCADA/Sys/EnergyLeafHourly/Backfill — 收到後依 payload 跑指定 SID + 期間的歷史聚合補算。
/// payload schema: { "sid": string | null, "from": "yyyy-MM-dd", "to": "yyyy-MM-dd" }
///   sid=null → 全葉子
///   sid="xxx" → 只算該 SID（典型用於 MaxKwh 改動後重算）
/// 內部按月分批 + 每批 sleep 避免阻塞線上查詢。
/// </summary>
public class EnergyLeafBackfillSubscriber : BackgroundService
{
    private readonly ILogger<EnergyLeafBackfillSubscriber> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly EnergyLeafAggregator _aggregator;
    private readonly EnergyLeafHourlyRepository _repository;
    private readonly IConfiguration _configuration;
    private IMqttClient? _mqttClient;
    private bool _isSubscribed = false;
    private bool _disposed = false;

    public const string TOPIC = "SCADA/Sys/EnergyLeafHourly/Backfill";

    public EnergyLeafBackfillSubscriber(
        ILogger<EnergyLeafBackfillSubscriber> logger,
        MqttConfigService mqttConfigService,
        EnergyLeafAggregator aggregator,
        EnergyLeafHourlyRepository repository,
        IConfiguration configuration)
    {
        _logger = logger;
        _mqttConfigService = mqttConfigService;
        _aggregator = aggregator;
        _repository = repository;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("葉子層 Backfill 訂閱服務啟動 (Engine)");

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
                        _logger.LogWarning("葉子層 Backfill 訂閱連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync(mqttSetting.MqttConfig);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "葉子層 Backfill 訂閱監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "葉子層 Backfill 訂閱服務執行時發生錯誤");
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

            var clientId = $"ScadaEngine_EnergyLeafBackfill_{Environment.ProcessId}";
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
                _logger.LogInformation("葉子層 Backfill 訂閱連線成功，ClientId: {ClientId}", clientId);
                await Task.Delay(500);
                await SubscribeTopicAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化葉子層 Backfill MQTT 連線時發生錯誤");
        }
    }

    private async Task SubscribeTopicAsync()
    {
        try
        {
            await _mqttClient!.SubscribeAsync(TOPIC);
            _isSubscribed = true;
            _logger.LogInformation("已訂閱葉子層 Backfill 主題: {Topic}", TOPIC);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱葉子層 Backfill 主題失敗");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("葉子層 Backfill MQTT 已連線");
        if (!_isSubscribed)
        {
            await Task.Delay(500);
            await SubscribeTopicAsync();
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isSubscribed = false;
        _logger.LogWarning("葉子層 Backfill MQTT 連線中斷: {Reason}", e.Reason);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var szPayload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            _logger.LogInformation("收到葉子層 Backfill 訊號: {Payload}", szPayload);

            var req = ParsePayload(szPayload);
            if (req == null)
            {
                _logger.LogWarning("葉子層 Backfill payload 解析失敗，略過");
                return;
            }

            // fire-and-forget — backfill 可能跑數分鐘，不阻塞 MQTT callback
            _ = Task.Run(() => RunBackfillAsync(req.Value.szSid, req.Value.dtFrom, req.Value.dtTo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理葉子層 Backfill 訊息失敗");
        }
    }

    private static (string? szSid, DateTime dtFrom, DateTime dtTo)? ParsePayload(string szPayload)
    {
        if (string.IsNullOrWhiteSpace(szPayload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(szPayload);
            var root = doc.RootElement;

            string? szSid = null;
            if (root.TryGetProperty("sid", out var sidProp) && sidProp.ValueKind == JsonValueKind.String)
                szSid = sidProp.GetString();

            if (!root.TryGetProperty("from", out var fromProp) || !root.TryGetProperty("to", out var toProp))
                return null;
            if (!DateTime.TryParse(fromProp.GetString(), out var dtFrom)) return null;
            if (!DateTime.TryParse(toProp.GetString(), out var dtTo)) return null;

            // 包含 to 整天 → exclusive 結尾 = to + 1 day
            dtFrom = new DateTime(dtFrom.Year, dtFrom.Month, dtFrom.Day, 0, 0, 0);
            dtTo = new DateTime(dtTo.Year, dtTo.Month, dtTo.Day, 0, 0, 0).AddDays(1);
            return (szSid, dtFrom, dtTo);
        }
        catch
        {
            return null;
        }
    }

    private async Task RunBackfillAsync(string? szSidFilter, DateTime dtFrom, DateTime dtToExclusive)
    {
        var nMaxStalenessHours = _configuration.GetValue<int?>("EnergyAggregation:MaxStalenessHours") ?? 2;
        var nBatchSleepMs = _configuration.GetValue<int?>("EnergyAggregation:BackfillBatchSleepMs") ?? 500;

        var allLeaves = await _repository.GetAllLeafSidsWithMaxKwhAsync();
        var leaves = string.IsNullOrEmpty(szSidFilter)
            ? allLeaves
            : allLeaves.Where(l => l.szSID == szSidFilter).ToList();

        if (leaves.Count == 0)
        {
            _logger.LogWarning("葉子層 Backfill: 找不到符合條件的葉子 (sidFilter={Sid})", szSidFilter);
            return;
        }

        _logger.LogInformation(
            "葉子層 Backfill 開始：葉子 {LeafCount} 個，期間 {From:yyyy-MM-dd} ~ {To:yyyy-MM-dd}（exclusive）",
            leaves.Count, dtFrom, dtToExclusive);

        int nTotalWritten = 0, nTotalSparse = 0, nBatches = 0;

        // 按月分批：每個葉子 × 每個月為一批，跑完 sleep
        foreach (var leaf in leaves)
        {
            var dtMonth = new DateTime(dtFrom.Year, dtFrom.Month, 1);
            while (dtMonth < dtToExclusive)
            {
                var dtMonthEnd = dtMonth.AddMonths(1);
                var dtBatchStart = dtMonth < dtFrom ? dtFrom : dtMonth;
                var dtBatchEnd = dtMonthEnd > dtToExclusive ? dtToExclusive : dtMonthEnd;

                var (nWritten, nSparse) = await BackfillBatchAsync(leaf, dtBatchStart, dtBatchEnd, nMaxStalenessHours);
                nTotalWritten += nWritten;
                nTotalSparse += nSparse;
                nBatches++;

                if (nBatchSleepMs > 0)
                    await Task.Delay(nBatchSleepMs);

                dtMonth = dtMonthEnd;
            }
        }

        _logger.LogInformation(
            "葉子層 Backfill 完成：{Batches} 批，寫入 {Written} 列、sparse 跳過 {Sparse} 列",
            nBatches, nTotalWritten, nTotalSparse);
    }

    private async Task<(int nWritten, int nSparse)> BackfillBatchAsync(
        EnergyLeafHourlyRepository.LeafInfo leaf,
        DateTime dtFrom, DateTime dtToExclusive, int nMaxStalenessHours)
    {
        int nWritten = 0, nSparse = 0;
        for (var dtH = dtFrom; dtH < dtToExclusive; dtH = dtH.AddHours(1))
        {
            try
            {
                var model = await _aggregator.ComputeAsync(leaf.szSID, dtH, leaf.dMaxKwh, nMaxStalenessHours, leaf.szName);
                if (model == null) { nSparse++; continue; }
                await _repository.UpsertAsync(model);
                nWritten++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "葉子層 Backfill 失敗於 SID={SID} Hour={Hour:yyyy-MM-dd HH}", leaf.szSID, dtH);
            }
        }
        return (nWritten, nSparse);
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
            _logger.LogError(ex, "重新連線葉子層 Backfill MQTT 時發生錯誤");
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
            _logger.LogError(ex, "清理葉子層 Backfill MQTT 連線時發生錯誤");
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
