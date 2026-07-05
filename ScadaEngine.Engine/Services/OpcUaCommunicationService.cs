using System.Collections.Concurrent;
using Opc.Ua;
using Opc.Ua.Client;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// OPC UA 來源通訊服務 — 為每個啟用的 OpcUaCoordinator 啟動一條 Session + polling loop，
/// 週期批次 Read（依 Server MaxNodesPerRead 分塊、對齊 Device 邊界），
/// 工程值 = 原始值 × Ratio，值變化偵測後餵入既有 History / Latest / MQTT / 警報 / 計算 pipeline。
/// 支援 ReloadAsync 熱重載與 WriteNodeAsync 控制寫回。
/// </summary>
public class OpcUaCommunicationService : BackgroundService
{
    private readonly ILogger<OpcUaCommunicationService> _logger;
    private readonly OpcUaConfigLoader _loader;
    private readonly IDataRepository _dataRepository;
    private readonly MqttPublishService _mqttPublishService;
    private readonly RealtimeDataStorageService _realtimeStorage;
    private readonly HistoryDataStorageService _historyStorage;
    private readonly AlarmMonitorService _alarmMonitorService;
    private readonly CalculatedPointService _calculatedPointService;
    private readonly LicenseState _licenseState;

    /// <summary>PollingInterval 下限（避免誤設過短打爆 Server）</summary>
    private const int MIN_POLLING_INTERVAL_MS = 200;

    /// <summary>定期重新發布間隔（分鐘），需小於 Web 端 STALE 門檻（5 分鐘）— 比照 Modbus</summary>
    private const int REPUBLISH_INTERVAL_MINUTES = 3;

    /// <summary>連線失敗後的重試等待（毫秒）</summary>
    private const int RECONNECT_DELAY_MS = 5000;

    /// <summary>重新載入序列化（避免 Reload 與正在跑的 polling 半更新）</summary>
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    /// <summary>每個 Coordinator 的執行期狀態，key=CoordinatorId</summary>
    private readonly ConcurrentDictionary<int, ServerRuntime> _runtimes = new();

    /// <summary>服務取消來源（每次 Reload 重建）</summary>
    private CancellationTokenSource? _innerCts;

    public OpcUaCommunicationService(
        ILogger<OpcUaCommunicationService> logger,
        OpcUaConfigLoader loader,
        IDataRepository dataRepository,
        MqttPublishService mqttPublishService,
        RealtimeDataStorageService realtimeStorage,
        HistoryDataStorageService historyStorage,
        AlarmMonitorService alarmMonitorService,
        CalculatedPointService calculatedPointService,
        LicenseState licenseState)
    {
        _logger = logger;
        _loader = loader;
        _dataRepository = dataRepository;
        _mqttPublishService = mqttPublishService;
        _realtimeStorage = realtimeStorage;
        _historyStorage = historyStorage;
        _alarmMonitorService = alarmMonitorService;
        _calculatedPointService = calculatedPointService;
        _licenseState = licenseState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OPC UA 來源通訊服務啟動");

        try
        {
            // 等候 MQTT / DB 初始化
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            await ReloadAsync(stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OPC UA 來源通訊服務收到停止訊號");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPC UA 來源通訊服務執行時發生錯誤");
        }
        finally
        {
            _innerCts?.Cancel();
        }
    }

    /// <summary>
    /// 重新載入 OpcUaPoint/*.json，重建所有 Server Session + polling loops。
    /// 由 OpcUaReloadSubscriber 在收到 SCADA/Sys/OpcUaCoordinator/Reload 時呼叫。
    /// </summary>
    public async Task ReloadAsync(CancellationToken externalToken = default)
    {
        await _reloadGate.WaitAsync(externalToken);
        try
        {
            // 取消舊的所有 polling loops（loop 結束時自行關閉 Session）
            _innerCts?.Cancel();
            _innerCts?.Dispose();
            _innerCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            _runtimes.Clear();

            var loaded = await _loader.LoadAllAsync();

            foreach (var item in loaded)
            {
                if (!item.Coordinator.isMonitorEnabled)
                {
                    _logger.LogInformation("OPC UA Coordinator {Name} 未啟用監控，略過", item.Coordinator.szName);
                    continue;
                }

                if (item.Points.Count == 0)
                {
                    _logger.LogInformation("OPC UA Coordinator {Name} 無點位，略過 polling", item.Coordinator.szName);
                    continue;
                }

                var runtime = new ServerRuntime
                {
                    Coordinator = item.Coordinator,
                    Points = item.Points,
                    PointsBySid = item.Points.ToDictionary(p => p.szSID, StringComparer.Ordinal),
                    PollingIntervalMs = Math.Max(MIN_POLLING_INTERVAL_MS, item.Coordinator.nPollingInterval),
                    OperationTimeoutMs = item.Coordinator.nConnectTimeout,
                    MaxNodesPerReadFallback = item.nMaxNodesPerReadFallback
                };
                _runtimes[item.Coordinator.Id] = runtime;

                _ = Task.Run(() => RunServerLoopAsync(runtime, _innerCts.Token));
            }

            _logger.LogInformation("OPC UA 來源 Reload 完成: 啟動 {Count} 條 polling loop", _runtimes.Count);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task RunServerLoopAsync(ServerRuntime runtime, CancellationToken ct)
    {
        _logger.LogInformation("OPC UA Polling 啟動: {Name} ({Url}), 點位 {Count}, interval={Interval}ms",
            runtime.Coordinator.szName, runtime.Coordinator.szEndpointUrl,
            runtime.Points.Count, runtime.PollingIntervalMs);

        while (!ct.IsCancellationRequested)
        {
            var nDelayMs = runtime.PollingIntervalMs;
            try
            {
                // 授權失效時跳過 polling
                if (!_licenseState.IsValid)
                {
                    await Task.Delay(runtime.PollingIntervalMs, ct);
                    continue;
                }

                if (runtime.Session == null || !runtime.Session.Connected)
                {
                    await ConnectAsync(runtime, ct);
                }

                await PollOnceAsync(runtime, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OPC UA Coordinator {Name} 連線/讀取失敗（{Delay}ms 後重試）",
                    runtime.Coordinator.szName, RECONNECT_DELAY_MS);

                // 讀失敗 → 全點位 Bad（沿用最後成功值），走同一條 pipeline（品質變化只會發布一次）
                await ProcessBadCycleAsync(runtime);
                await CloseSessionAsync(runtime);
                nDelayMs = Math.Max(runtime.PollingIntervalMs, RECONNECT_DELAY_MS);
            }

            try
            {
                await Task.Delay(nDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await CloseSessionAsync(runtime);
        _logger.LogInformation("OPC UA Polling 停止: {Name}", runtime.Coordinator.szName);
    }

    /// <summary>
    /// 建立 Session、RegisterNodes、依 Server MaxNodesPerRead 分塊（對齊 Device 邊界）
    /// </summary>
    private async Task ConnectAsync(ServerRuntime runtime, CancellationToken ct)
    {
        await CloseSessionAsync(runtime);

        var c = runtime.Coordinator;
        _logger.LogInformation("OPC UA 連線中: {Name} → {Url}", c.szName, c.szEndpointUrl);

        var session = await OpcUaClientHelper.CreateSessionAsync(
            c.szEndpointUrl, c.szUsername, c.szPassword, runtime.OperationTimeoutMs,
            $"ScadaEngine_{c.szName}", ct);

        ct.ThrowIfCancellationRequested();

        // 解析 NodeId（非法 NodeId 的點位以 Bad 佔位，不擋其他點位）
        var validPoints = new List<OpcUaPointModel>();
        var nodeIds = new NodeIdCollection();
        foreach (var p in runtime.Points)
        {
            try
            {
                nodeIds.Add(NodeId.Parse(p.szTagName));
                validPoints.Add(p);
            }
            catch (Exception ex)
            {
                if (runtime.InvalidNodeLogged.Add(p.szSID))
                    _logger.LogWarning(ex, "點位 {SID} 的 NodeId 非法: {TagName}（將維持 Bad）", p.szSID, p.szTagName);
            }
        }

        // RegisterNodes：字串 NodeId 註冊為 Server 端 handle，降低每輪解析成本（斷線重連後重新註冊）
        var readIds = nodeIds;
        try
        {
            var registerResponse = await session.RegisterNodesAsync(null, nodeIds, ct);
            if (registerResponse.RegisteredNodeIds != null && registerResponse.RegisteredNodeIds.Count == nodeIds.Count)
                readIds = registerResponse.RegisteredNodeIds;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Coordinator {Name} RegisterNodes 不支援/失敗，改用原始 NodeId", c.szName);
        }

        // 讀取 Server OperationLimits.MaxNodesPerRead（讀不到用 fallback）
        var nMaxPerRead = runtime.MaxNodesPerReadFallback;
        try
        {
            var nServerLimit = session.OperationLimits?.MaxNodesPerRead ?? 0;
            if (nServerLimit > 0)
                nMaxPerRead = (int)Math.Min(nServerLimit, int.MaxValue);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Coordinator {Name} 讀取 OperationLimits 失敗，分塊改用 fallback {Fallback}",
                c.szName, runtime.MaxNodesPerReadFallback);
        }

        runtime.Session = session;
        runtime.Chunks = BuildChunks(validPoints, readIds, nMaxPerRead);

        _logger.LogInformation("OPC UA 連線成功: {Name}, 有效點位 {Valid}/{Total}, MaxNodesPerRead={Max}, 分 {Chunks} 塊",
            c.szName, validPoints.Count, runtime.Points.Count, nMaxPerRead, runtime.Chunks.Count);
    }

    /// <summary>
    /// 分塊：同一 Device 的點位盡量放同一塊（保留 Server 端快取效益），單一 Device 超過塊大小時再切
    /// </summary>
    private static List<ReadChunk> BuildChunks(List<OpcUaPointModel> points, NodeIdCollection nodeIds, int nMaxPerRead)
    {
        var chunks = new List<ReadChunk>();
        if (points.Count == 0) return chunks;
        if (nMaxPerRead < 1) nMaxPerRead = 1;

        // 依 Device 分組（保持原始順序）
        var deviceGroups = new List<List<int>>(); // 值 = points/nodeIds 的索引
        var groupByDevice = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < points.Count; i++)
        {
            var szDevice = points[i].szDeviceName ?? string.Empty;
            if (!groupByDevice.TryGetValue(szDevice, out var list))
            {
                list = new List<int>();
                groupByDevice[szDevice] = list;
                deviceGroups.Add(list);
            }
            list.Add(i);
        }

        var current = new ReadChunk();
        foreach (var group in deviceGroups)
        {
            // 整組裝不下目前塊且目前塊非空 → 先封塊（對齊 Device 邊界）
            if (current.Points.Count > 0 && current.Points.Count + group.Count > nMaxPerRead)
            {
                chunks.Add(current);
                current = new ReadChunk();
            }

            foreach (var idx in group)
            {
                if (current.Points.Count >= nMaxPerRead)
                {
                    chunks.Add(current);
                    current = new ReadChunk();
                }
                current.Points.Add(points[idx]);
                current.NodesToRead.Add(new ReadValueId
                {
                    NodeId = nodeIds[idx],
                    AttributeId = Attributes.Value
                });
            }
        }
        if (current.Points.Count > 0)
            chunks.Add(current);

        return chunks;
    }

    private async Task PollOnceAsync(ServerRuntime runtime, CancellationToken ct)
    {
        var session = runtime.Session;
        if (session == null || runtime.Chunks.Count == 0) return;

        var samples = new List<(OpcUaPointModel Point, float fEngValue, bool isGood)>();

        foreach (var chunk in runtime.Chunks)
        {
            ct.ThrowIfCancellationRequested();

            // 任一塊 Read 拋例外 → 交給外層視為連線失敗（全點位 Bad + 重連）
            var readResponse = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, chunk.NodesToRead, ct);
            var results = readResponse.Results;

            for (int i = 0; i < chunk.Points.Count && i < results.Count; i++)
            {
                var point = chunk.Points[i];
                var dv = results[i];

                var isGood = StatusCode.IsGood(dv.StatusCode)
                             && OpcUaClientHelper.TryConvertToDouble(dv.Value, out var dRaw);

                float fEngValue;
                if (isGood)
                {
                    OpcUaClientHelper.TryConvertToDouble(dv.Value, out var dRawValue);
                    fEngValue = (float)(dRawValue * point.fRatio);
                    // 快取原始值型別（控制寫回時轉換用）
                    runtime.LastRawValue[point.szSID] = dv.Value;
                }
                else
                {
                    // 讀到 Bad → 沿用最後成功值
                    fEngValue = runtime.LastSeen.TryGetValue(point.szSID, out var prev) ? prev.Value : 0f;
                }

                samples.Add((point, fEngValue, isGood));
            }
        }

        // 非法 NodeId 的點位補 Bad 佔位
        foreach (var p in runtime.Points)
        {
            if (runtime.InvalidNodeLogged.Contains(p.szSID))
                samples.Add((p, 0f, false));
        }

        runtime.HasEverReadSuccessfully = true;
        await ProcessSamplesAsync(runtime, samples);
    }

    /// <summary>
    /// 連線/讀取失敗的週期：全點位以最後成功值 + Bad quality 走同一條 pipeline。
    /// Engine 啟動後從未讀成功過 → 不寫（避免一啟動就連不上時灌 0 進歷史）。
    /// </summary>
    private async Task ProcessBadCycleAsync(ServerRuntime runtime)
    {
        if (!runtime.HasEverReadSuccessfully) return;

        try
        {
            var samples = runtime.Points
                .Select(p => (p,
                    runtime.LastSeen.TryGetValue(p.szSID, out var prev) ? prev.Value : 0f,
                    false))
                .ToList();
            await ProcessSamplesAsync(runtime, samples);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coordinator {Name} Bad 週期處理失敗", runtime.Coordinator.szName);
        }
    }

    /// <summary>
    /// 值變化偵測（含 3 分鐘定期重發）後餵入 History / Latest / MQTT / 警報 / 計算 pipeline
    /// — 與 DbCommunicationService 同構。
    /// </summary>
    private async Task ProcessSamplesAsync(ServerRuntime runtime, List<(OpcUaPointModel Point, float fEngValue, bool isGood)> samples)
    {
        if (samples.Count == 0) return;

        var dtNow = DateTime.Now;
        var historyBatch = new List<RealtimeDataModel>();
        var changedBatch = new List<RealtimeDataModel>();
        var latestBatch = new List<LatestDataModel>();

        foreach (var (point, fEngValue, isGood) in samples)
        {
            var szQuality = isGood ? "Good" : "Bad";
            var nQuality = isGood ? 1 : 0;

            // 全部樣本進 historyBatch（HistoryDataStorageService 內部分鐘 dedup 降頻）
            historyBatch.Add(BuildRealtimeData(runtime, point, fEngValue, szQuality, dtNow, isGood));

            // 變化偵測：值/品質變化，或超過定期重發間隔（防 Web 端 STALE）
            var hasChanged = true;
            if (runtime.LastSeen.TryGetValue(point.szSID, out var prev)
                && Math.Abs(prev.Value - fEngValue) < float.Epsilon
                && prev.Quality == nQuality)
            {
                var isRepublishDue =
                    !runtime.LastPublishedTime.TryGetValue(point.szSID, out var dtLastPublished)
                    || dtNow.Subtract(dtLastPublished).TotalMinutes >= REPUBLISH_INTERVAL_MINUTES;
                hasChanged = isRepublishDue;
            }

            if (!hasChanged) continue;

            runtime.LastSeen[point.szSID] = (fEngValue, nQuality);
            runtime.LastPublishedTime[point.szSID] = dtNow;

            changedBatch.Add(BuildRealtimeData(runtime, point, fEngValue, szQuality, dtNow, isGood));
            latestBatch.Add(new LatestDataModel
            {
                szSID = point.szSID,
                fValue = fEngValue,
                nQuality = nQuality,
                dtTimestamp = dtNow
            });
        }

        if (historyBatch.Count > 0)
            _historyStorage.AddRealtimeDataBatch(historyBatch);

        if (changedBatch.Count == 0) return;

        if (latestBatch.Count > 0)
            await _dataRepository.SaveLatestDataAsync(latestBatch);

        // 餵入 RealtimeStorage 的 persistent cache（給 calc / LogicFlow 讀）
        _realtimeStorage.UpdatePersistentCacheOnly(changedBatch);

        // 發布 MQTT
        foreach (var rt in changedBatch)
        {
            try
            {
                await _mqttPublishService.PublishRealtimeDataAsync(rt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OPC UA 來源 {SID} MQTT 發布失敗", rt.szSID);
            }
        }

        // 警報評估
        try
        {
            await _alarmMonitorService.EvaluateBatchAsync(changedBatch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPC UA 來源警報評估失敗");
        }

        // 計算點位（含警報二次評估）
        try
        {
            var calcResults = _calculatedPointService.CalculateAndPublish(changedBatch);
            if (calcResults.Count > 0)
                await _alarmMonitorService.EvaluateBatchAsync(calcResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPC UA 來源計算點位失敗");
        }

        _logger.LogTrace("OPC UA Coordinator {Name} 變更 {Count} 筆 / 歷史 {HCount} 筆已處理",
            runtime.Coordinator.szName, changedBatch.Count, historyBatch.Count);
    }

    private static RealtimeDataModel BuildRealtimeData(
        ServerRuntime runtime, OpcUaPointModel point, float fEngValue, string szQuality, DateTime dtNow, bool isGood)
    {
        return new RealtimeDataModel
        {
            szSID = point.szSID,
            szTagName = point.szName,
            fValue = fEngValue,
            szUnit = point.szUnit ?? string.Empty,
            szQuality = szQuality,
            szDeviceIP = "OPCUA",
            nAddress = point.nSequence,
            szCoordinatorName = runtime.Coordinator.szName,
            dtTimestamp = dtNow,
            IsReadSuccess = isGood
        };
    }

    /// <summary>
    /// 控制寫回：SCADA/Control/{SID} → OPC UA Write。
    /// AO：寫入原始值 = 輸入值 ÷ Ratio；DO：寫入 bool。唯讀點位（ControlType 空）拒絕。
    /// </summary>
    public async Task<bool> WriteNodeAsync(string szSid, double dValue)
    {
        // 在所有 runtime 中找到該 SID
        ServerRuntime? runtime = null;
        OpcUaPointModel? point = null;
        foreach (var rt in _runtimes.Values)
        {
            if (rt.PointsBySid.TryGetValue(szSid, out var p))
            {
                runtime = rt;
                point = p;
                break;
            }
        }

        if (runtime == null || point == null)
        {
            _logger.LogWarning("[OPC 控制] 找不到 SID {SID} 對應的執行中 Coordinator", szSid);
            return false;
        }

        if (string.IsNullOrEmpty(point.szControlType))
        {
            _logger.LogWarning("[OPC 控制] 點位 {SID} 為唯讀（ControlType 空），拒絕寫入", szSid);
            return false;
        }

        var session = runtime.Session;
        if (session == null || !session.Connected)
        {
            _logger.LogWarning("[OPC 控制] Coordinator {Name} 未連線，無法寫入 {SID}",
                runtime.Coordinator.szName, szSid);
            return false;
        }

        try
        {
            NodeId nodeId;
            try
            {
                nodeId = NodeId.Parse(point.szTagName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[OPC 控制] 點位 {SID} NodeId 非法: {TagName}", szSid, point.szTagName);
                return false;
            }

            object writeValue;
            if (point.szControlType == "DO")
            {
                writeValue = dValue > 0.5;
            }
            else // AO — 反算原始值 = 輸入值 ÷ Ratio，並依 Server 端現值型別轉換
            {
                var fRatio = point.fRatio == 0f ? 1.0f : point.fRatio;
                var dRaw = dValue / fRatio;

                runtime.LastRawValue.TryGetValue(szSid, out var lastRaw);
                if (lastRaw == null)
                {
                    // 沒讀過原始值 → 先讀一次拿型別
                    try
                    {
                        var dv = await OpcUaClientHelper.ReadSingleValueAsync(session, nodeId);
                        lastRaw = dv?.Value;
                    }
                    catch
                    {
                        lastRaw = null;
                    }
                }
                writeValue = OpcUaClientHelper.ConvertToServerType(lastRaw, dRaw);
            }

            var writeValues = new WriteValueCollection
            {
                new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(writeValue))
                }
            };

            var writeResponse = await session.WriteAsync(null, writeValues, CancellationToken.None);
            var results = writeResponse.Results;

            var isOk = results.Count > 0 && StatusCode.IsGood(results[0]);
            if (isOk)
            {
                _logger.LogInformation("[OPC 控制] 寫入成功: SID={SID}, 輸入值={Value}, 寫入原始值={Raw}",
                    szSid, dValue, writeValue);
            }
            else
            {
                _logger.LogWarning("[OPC 控制] Server 拒絕寫入: SID={SID}, Status={Status}",
                    szSid, results.Count > 0 ? results[0].ToString() : "N/A");
            }
            return isOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OPC 控制] 寫入失敗: SID={SID}, Value={Value}", szSid, dValue);
            return false;
        }
    }

    private async Task CloseSessionAsync(ServerRuntime runtime)
    {
        var session = runtime.Session;
        runtime.Session = null;
        runtime.Chunks = new List<ReadChunk>();
        await OpcUaClientHelper.CloseSessionSafelyAsync(session);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _innerCts?.Cancel();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 一次批次 Read 的分塊（Points 與 NodesToRead 依索引一一對應）
    /// </summary>
    private class ReadChunk
    {
        public List<OpcUaPointModel> Points { get; } = new();
        public ReadValueIdCollection NodesToRead { get; } = new();
    }

    /// <summary>
    /// 單個 OPC UA Server 的執行期狀態
    /// </summary>
    private class ServerRuntime
    {
        public OpcUaCoordinatorModel Coordinator { get; set; } = new();
        public List<OpcUaPointModel> Points { get; set; } = new();
        public Dictionary<string, OpcUaPointModel> PointsBySid { get; set; } = new();
        public int PollingIntervalMs { get; set; } = 1000;
        public int OperationTimeoutMs { get; set; } = 5000;
        public int MaxNodesPerReadFallback { get; set; } = 500;

        public ISession? Session { get; set; }
        public List<ReadChunk> Chunks { get; set; } = new();

        /// <summary>變化偵測快取：SID → (engValue, quality)</summary>
        public ConcurrentDictionary<string, (float Value, int Quality)> LastSeen { get; } = new();

        /// <summary>最後 MQTT 發布時間（3 分鐘定期重發判定）</summary>
        public ConcurrentDictionary<string, DateTime> LastPublishedTime { get; } = new();

        /// <summary>最後成功讀到的原始值（控制寫回時做型別轉換）</summary>
        public ConcurrentDictionary<string, object?> LastRawValue { get; } = new();

        /// <summary>NodeId 非法、已 log 過的 SID（每輪以 Bad 佔位）</summary>
        public HashSet<string> InvalidNodeLogged { get; } = new(StringComparer.Ordinal);

        /// <summary>Engine 啟動後是否曾成功讀取過（未成功前失敗不寫 Bad 歷史，避免灌 0）</summary>
        public bool HasEverReadSuccessfully { get; set; }
    }
}
