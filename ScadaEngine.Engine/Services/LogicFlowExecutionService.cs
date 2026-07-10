using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentModbus;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// LogicFlow 後端執行引擎：週期性評估所有啟用的邏輯流程，
/// 直接執行 Modbus 寫入，不依賴瀏覽器。
/// </summary>
public class LogicFlowExecutionService : BackgroundService
{
    private readonly ILogger<LogicFlowExecutionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModbusConfigService _modbusConfigService;
    private readonly LogicFlowRepository _logicFlowRepository;
    private readonly RealtimeDataStorageService _realtimeDataService;
    private readonly MqttPublishService _mqttPublishService;
    private readonly CSharpAlgorithmService _csharpAlgoService;
    private readonly AlarmEventLogRepository _alarmEventLogRepo;

    // 快取
    private readonly ConcurrentDictionary<int, DiagramContext> _diagrams = new();
    private Dictionary<string, (double dValue, int nQuality)> _latestCache = new();
    private Dictionary<string, (double dValue, bool isAuto)> _manualControlCache = new();
    private List<ModbusDeviceConfigModel> _deviceConfigs = new();
    private Dictionary<int, ScheduleRecord> _scheduleCache = new();
    // 歷史值快取：key = (SID, offset 分鐘)，每分鐘刷新一次（HistoryData 一分鐘一筆），主迴圈同步讀取
    private Dictionary<(string szSid, int nOffsetMinutes), (double dValue, bool isGood)> _historyCache = new();

    // 重載計時
    private DateTime _dtLastDiagramReload = DateTime.MinValue;
    private DateTime _dtLastDeviceConfigReload = DateTime.MinValue;
    private DateTime _dtLastManualControlReload = DateTime.MinValue;
    private DateTime _dtLastScheduleReload = DateTime.MinValue;
    private DateTime _dtLastHistoryReload = DateTime.MinValue;

    // 啟動保護期：服務啟動後不執行 Modbus 寫入，僅評估邏輯並填充 OutputPrevState
    private DateTime _dtStartupTime;
    private bool _isInStartupGracePeriod = true;

    // TP 狀態發布：變化偵測（避免每輪都發布）
    private string _szLastPublishedTimerState = "";

    // 評估鎖（防止 Timer 回呼與主迴圈同時評估）
    private readonly SemaphoreSlim _evalLock = new(1, 1);

    // Python 演算法 HTTP 呼叫
    private static readonly HttpClient _algoHttpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8100"),
        Timeout = TimeSpan.FromSeconds(3)
    };
    // 演算法結果快取：key = "{treeId}-{nodeId}-{inputHash}" 為輸入快取，"{treeId}-{nodeId}" 為節點最新值快取
    // value 為 (輸出 dict, status)；status 在 cache hit 時一併還原，否則狀態會在 hit 時遺失
    private readonly ConcurrentDictionary<string, AlgoCachedOutput> _algoResultCache = new();
    // 演算法首次呼叫時間戳（grace period 用，避免啟動瞬間輸出 null）
    private readonly ConcurrentDictionary<string, DateTime> _algoPendingSince = new();
    // 演算法 status 上一輪值（用於 OK ↔ 非 OK 轉換偵測，寫 EventLog）
    private readonly ConcurrentDictionary<string, AlgoStatusSnapshot> _algoLastStatus = new();
    private static readonly TimeSpan ALGO_GRACE_PERIOD = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DIAGRAM_RELOAD_INTERVAL = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DEVICE_CONFIG_RELOAD_INTERVAL = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MANUAL_CONTROL_RELOAD_INTERVAL = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SCHEDULE_RELOAD_INTERVAL = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EVAL_INTERVAL = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HISTORY_RELOAD_INTERVAL = TimeSpan.FromMinutes(1);
    // 歷史值回溯容忍窗：目標時間往前最多 5 分鐘取最近一筆 Quality=1，否則視為 Bad
    private static readonly TimeSpan HISTORY_LOOKBACK_WINDOW = TimeSpan.FromMinutes(5);

    private const string TIMER_STATE_TOPIC = "SCADA/LogicFlow/TimerState";

    public LogicFlowExecutionService(
        ILogger<LogicFlowExecutionService> logger,
        IServiceProvider serviceProvider,
        ModbusConfigService modbusConfigService,
        LogicFlowRepository logicFlowRepository,
        RealtimeDataStorageService realtimeDataService,
        MqttPublishService mqttPublishService,
        CSharpAlgorithmService csharpAlgoService,
        AlarmEventLogRepository alarmEventLogRepo)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _modbusConfigService = modbusConfigService;
        _logicFlowRepository = logicFlowRepository;
        _realtimeDataService = realtimeDataService;
        _mqttPublishService = mqttPublishService;
        _csharpAlgoService = csharpAlgoService;
        _alarmEventLogRepo = alarmEventLogRepo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogicFlow 執行服務啟動，等待其他服務初始化 (10 秒)...");
        await Task.Delay(10_000, stoppingToken);

        await ReloadDeviceConfigsAsync();
        await ReloadDiagramsAsync();

        _dtStartupTime = DateTime.Now;
        _isInStartupGracePeriod = true;
        _logger.LogWarning("LogicFlow 啟動保護期開始 ({Sec} 秒)：評估邏輯但不執行 Modbus 寫入",
            STARTUP_GRACE_PERIOD.TotalSeconds);

        _logger.LogInformation("LogicFlow 執行服務進入主迴圈");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 啟動保護期結束檢查
                if (_isInStartupGracePeriod && DateTime.Now - _dtStartupTime >= STARTUP_GRACE_PERIOD)
                {
                    _isInStartupGracePeriod = false;
                    _logger.LogWarning("LogicFlow 啟動保護期結束，恢復正常 Modbus 寫入");
                }

                if (DateTime.Now - _dtLastDeviceConfigReload >= DEVICE_CONFIG_RELOAD_INTERVAL)
                    await ReloadDeviceConfigsAsync();

                if (DateTime.Now - _dtLastDiagramReload >= DIAGRAM_RELOAD_INTERVAL)
                    await ReloadDiagramsAsync();

                if (_diagrams.Count > 0)
                {
                    await EvaluateAllDiagramsAsync();
                    await PublishTimerStatesAsync();
                }

                await Task.Delay(EVAL_INTERVAL, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LogicFlow 主迴圈發生錯誤");
                await Task.Delay(5000, stoppingToken);
            }
        }

        // 清理所有 Timer
        foreach (var ctx in _diagrams.Values)
            foreach (var nd in ctx.Nodes)
                nd.TpTimer?.Dispose();

        _logger.LogInformation("LogicFlow 執行服務已停止");
    }

    // ─── 載入 ────────────────────────────────────────────────────────────────

    private async Task ReloadDeviceConfigsAsync()
    {
        try
        {
            _deviceConfigs = await _modbusConfigService.LoadAllDeviceConfigsAsync();
            _dtLastDeviceConfigReload = DateTime.Now;
            _logger.LogDebug("LogicFlow 設備配置已載入: {Count} 台", _deviceConfigs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 重新載入設備配置失敗");
        }
    }

    private async Task ReloadDiagramsAsync()
    {
        try
        {
            var enabledNodes = await _logicFlowRepository.GetEnabledLogicNodesAsync();
            var enabledIds = new HashSet<int>();

            foreach (var (nTreeId, szName) in enabledNodes)
            {
                enabledIds.Add(nTreeId);

                var szJson = await _logicFlowRepository.GetDiagramJsonAsync(nTreeId);
                if (string.IsNullOrWhiteSpace(szJson)) continue;

                // 已載入且 JSON 未變 → 跳過
                if (_diagrams.TryGetValue(nTreeId, out var existing) && existing.RawJson == szJson)
                    continue;

                // JSON 有變化 → 清理舊 Timer，重新解析
                if (existing != null)
                {
                    foreach (var nd in existing.Nodes) nd.TpTimer?.Dispose();
                }

                var ctx = ParseDiagram(nTreeId, szName, szJson);
                if (ctx != null)
                {
                    ctx.RawJson = szJson;
                    _diagrams[nTreeId] = ctx;
                    _logger.LogInformation("LogicFlow 已{Action}邏輯: [{Id}] {Name} ({NodeCount} 節點, {EdgeCount} 邊)",
                        existing != null ? "更新" : "載入", nTreeId, szName, ctx.Nodes.Count, ctx.Edges.Count);
                }
            }

            // 移除已停用的邏輯
            foreach (var key in _diagrams.Keys.ToList())
            {
                if (!enabledIds.Contains(key))
                {
                    if (_diagrams.TryRemove(key, out var removed))
                    {
                        foreach (var nd in removed.Nodes) nd.TpTimer?.Dispose();
                        _logger.LogInformation("LogicFlow 已卸載邏輯: [{Id}]", key);
                    }
                }
            }

            _dtLastDiagramReload = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 重新載入圖形資料失敗");
        }
    }

    // ─── 評估 ────────────────────────────────────────────────────────────────

    private async Task EvaluateAllDiagramsAsync()
    {
        if (!await _evalLock.WaitAsync(0)) return; // 已在評估中，跳過
        try
        {
            // 讀取即時值和手動控制快取
            await RefreshCachesAsync();

            foreach (var ctx in _diagrams.Values)
            {
                try
                {
                    EvaluateNodes(ctx);
                    await ProcessOutputNodesAsync(ctx);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LogicFlow 評估邏輯 [{Id}] {Name} 失敗",
                        ctx.TreeId, ctx.Name);
                }
            }
        }
        finally
        {
            _evalLock.Release();
        }
    }

    private async Task RefreshCachesAsync()
    {
        try
        {
            // 即時值：直接從 Engine 記憶體讀取（零延遲，不經 DB）
            var snapshot = _realtimeDataService.GetAllLatestValues();
            _latestCache = snapshot.ToDictionary(
                kv => kv.Key,
                kv => ((double)kv.Value.fValue, kv.Value.nQuality));

            // 手動控制值：節流查詢（變動頻率低，每 3 秒刷新一次即可）
            if (DateTime.Now - _dtLastManualControlReload >= MANUAL_CONTROL_RELOAD_INTERVAL)
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
                _manualControlCache = await repo.LoadManualControlValuesAsync();

                // 確保所有輸出點位都有 ManualControlValue 記錄（不存在則 INSERT IsAuto=1）
                // 避免前端查詢 manual-values 時因缺少記錄導致外框閃爍
                var outputSids = _diagrams.Values
                    .SelectMany(ctx => ctx.Nodes)
                    .Where(nd => nd.Type == "output" && !string.IsNullOrEmpty(nd.Sid))
                    .Select(nd => nd.Sid!)
                    .Distinct();

                foreach (var sid in outputSids)
                {
                    if (!_manualControlCache.ContainsKey(sid))
                    {
                        await repo.EnsureManualControlEntryExistsAsync(sid);
                        _manualControlCache[sid] = (0, true);
                    }
                }

                _dtLastManualControlReload = DateTime.Now;
            }

            // 排程快取：每 15 秒刷新
            if (DateTime.Now - _dtLastScheduleReload >= SCHEDULE_RELOAD_INTERVAL)
            {
                var schedules = await _logicFlowRepository.GetEnabledSchedulesAsync();
                _scheduleCache = schedules.ToDictionary(s => s.Id);
                _dtLastScheduleReload = DateTime.Now;
            }

            // 歷史值快取：每分鐘刷新（HistoryData 一分鐘一筆，target 時間戳每分鐘才變，更頻繁無資訊增量）
            if (DateTime.Now - _dtLastHistoryReload >= HISTORY_RELOAD_INTERVAL)
            {
                await RefreshHistoryCacheAsync();
                _dtLastHistoryReload = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 刷新快取失敗");
        }
    }

    /// <summary>刷新歷史值快取：收集所有啟用歷史讀取節點的 (SID, offset) 組合，
    /// 每組查 HistoryData「目標時間往前 5 分鐘窗」內最近一筆 Quality=1，查無 → Bad。
    /// 主迴圈（200ms）只讀快取不打 DB；DB 每分鐘查詢次數 = 不同組合數。</summary>
    private async Task RefreshHistoryCacheAsync()
    {
        try
        {
            var combos = _diagrams.Values
                .SelectMany(ctx => ctx.Nodes)
                .Where(nd => nd.HistEnabled
                             && nd.HistOffsetMinutes.HasValue
                             && !string.IsNullOrEmpty(nd.Sid)
                             && nd.Type is "input" or "contact_no" or "contact_nc")
                .Select(nd => (Sid: nd.Sid!, Offset: nd.HistOffsetMinutes!.Value))
                .Distinct()
                .ToList();

            if (combos.Count == 0)
            {
                if (_historyCache.Count > 0) _historyCache = new();
                return;
            }

            var newCache = new Dictionary<(string, int), (double, bool)>(combos.Count);
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
            var dtNow = DateTime.Now;

            foreach (var (szSid, nOffset) in combos)
            {
                var dtTarget = dtNow.AddMinutes(-nOffset);
                var rows = await repo.GetHistoryTableDataAsync(szSid, dtTarget - HISTORY_LOOKBACK_WINDOW, dtTarget);
                var latest = rows.Where(r => r.nQuality == 1)
                    .OrderByDescending(r => r.dtTimestamp)
                    .FirstOrDefault();
                newCache[(szSid, nOffset)] = latest != null ? (latest.fValue, true) : (0, false);
            }

            _historyCache = newCache; // snapshot swap（照 _latestCache 模式，主迴圈讀取無鎖）
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 刷新歷史值快取失敗");
        }
    }

    /// <summary>讀取啟用歷史讀取節點的歷史值；快取無該組合或查無資料（Bad）→ false。</summary>
    private bool TryGetHistoryValue(FlowNode nd, out double dValue)
    {
        dValue = 0;
        if (string.IsNullOrEmpty(nd.Sid) || !nd.HistOffsetMinutes.HasValue) return false;
        if (!_historyCache.TryGetValue((nd.Sid, nd.HistOffsetMinutes.Value), out var hv) || !hv.isGood)
            return false;
        dValue = hv.dValue;
        return true;
    }

    /// <summary>input 節點是否 Bad：啟用歷史讀取 → 查歷史快取；否則查即時快取品質。</summary>
    private bool IsInputSourceBad(FlowNode srcNode)
    {
        if (srcNode.HistEnabled)
            return !TryGetHistoryValue(srcNode, out _);
        return !_latestCache.TryGetValue(srcNode.Sid!, out var lv) || lv.nQuality != 1;
    }

    /// <summary>多輪迭代求值所有節點</summary>
    private void EvaluateNodes(DiagramContext ctx)
    {
        var evalTypes = new HashSet<string> { "math", "compare", "and", "or", "not", "xor", "timer", "contact_no", "contact_nc", "algorithm", "counter" };
        var evalNodes = ctx.Nodes.Where(n => evalTypes.Contains(n.Type)).ToList();

        // 清除上一輪結果（保留 TP 狀態機屬性）
        foreach (var nd in evalNodes)
        {
            // counter 的 Result（q）跨 tick 保留，支援 q→reset 自回授；CounterValue 等運行時狀態本來就跨 tick 持續
            if (nd.Type != "counter") nd.Result = null;
            nd.AlgoResultDict = null;
            // 演算法 status 跨 tick 重置為 OK，避免上一輪 Error 殘留誤判 HasUpstreamBad
            nd.AlgoStatusCodeId = 0;
            nd.AlgoStatusCodeName = "OK";
            nd.AlgoStatusSeverity = "Info";
            nd.AlgoPerOutputStatus = null;
            nd.IsDone = false;
        }

        int nMaxRounds = evalNodes.Count + 1;
        for (int round = 0; round < nMaxRounds; round++)
        {
            bool isChanged = false;
            foreach (var nd in evalNodes)
            {
                if (nd.IsDone) continue;
                if (EvalOneNode(ctx, nd)) isChanged = true;
            }
            if (!isChanged) break;
        }
    }

    /// <summary>處理輸出節點的邊緣觸發（含重試機制）</summary>
    private async Task ProcessOutputNodesAsync(DiagramContext ctx)
    {
        foreach (var nd in ctx.Nodes)
        {
            if (nd.Type != "output" || string.IsNullOrEmpty(nd.Sid)) continue;

            var dVal = GetInputValue(ctx, nd.Id, "in");
            bool isGreen = !HasUpstreamBad(ctx, nd.Id) && dVal.HasValue;

            if (!ctx.OutputPrevState.TryGetValue(nd.Id, out var prev))
                prev = (false, null);

            // 手動模式檢查
            var isManual = _manualControlCache.TryGetValue(nd.Sid, out var ctrl) && !ctrl.isAuto;

            // 超限檢查
            double dFMin = nd.FMin;
            double dFMax = nd.FMax;
            bool isOutOfRange = isGreen && dVal.HasValue && (dVal.Value < dFMin || dVal.Value > dFMax);

            // ── 啟動保護期：不執行寫入，僅填充 OutputPrevState 作為基線 ──
            if (_isInStartupGracePeriod)
            {
                ctx.OutputPrevState[nd.Id] = (isGreen, isGreen ? dVal : null);
                continue;
            }

            // 取得此輸出節點的重試狀態
            ctx.RetryStates.TryGetValue(nd.Id, out var retryState);

            // ── 情境 A：有待重試的失敗狀態 ──
            if (retryState != null && retryState.nFailCount > 0)
            {
                // 條件不再成立（輸入消失、手動、超限）→ 放棄重試，重置
                if (!isGreen || isManual || isOutOfRange)
                {
                    _logger.LogWarning("[LogicFlow][診斷] 重試狀態被清除: {SID} | isGreen={IsGreen} dVal={DVal} isManual={IsManual} isOutOfRange={IsOutOfRange} nFailCount={N}",
                        nd.Sid, isGreen, dVal, isManual, isOutOfRange, retryState.nFailCount);
                    if (retryState.isAlarmRaised)
                        await ClearControlFailEventAsync(nd.Sid);
                    ctx.RetryStates.Remove(nd.Id);
                    ctx.OutputPrevState[nd.Id] = (isGreen, isGreen ? dVal : null);
                    continue;
                }

                // 尚未到 10 秒重試間隔 → 跳過
                if (DateTime.Now - retryState.dtLastRetry < RETRY_INTERVAL)
                {
                    // 不更新 prevState，保持邊緣觸發條件
                    continue;
                }

                // 重試寫入
                retryState.dtLastRetry = DateTime.Now;
                var isRetryOk = await ExecuteControlWriteAsync(nd.Sid, dVal!.Value);

                if (isRetryOk)
                {
                    _logger.LogInformation("[LogicFlow] 重試成功: {SID} = {Value} (第 {N} 次)",
                        nd.Sid, dVal.Value, retryState.nFailCount + 1);
                    if (retryState.isAlarmRaised)
                        await ClearControlFailEventAsync(nd.Sid);
                    ctx.RetryStates.Remove(nd.Id);
                    ctx.OutputPrevState[nd.Id] = (isGreen, dVal);
                    ctx.OutputLastWriteOk[nd.Id] = DateTime.Now;
                }
                else
                {
                    retryState.nFailCount++;
                    _logger.LogWarning("[LogicFlow] 重試失敗 ({N}/{Max}): {SID} = {Value}",
                        retryState.nFailCount, MAX_RETRY_BEFORE_ALARM, nd.Sid, dVal.Value);

                    if (retryState.nFailCount >= MAX_RETRY_BEFORE_ALARM && !retryState.isAlarmRaised)
                    {
                        await InsertControlFailEventAsync(nd.Sid, dVal.Value, ctx.Name);
                        retryState.isAlarmRaised = true;
                    }
                    // 不更新 prevState → 下次繼續重試
                }
                continue;
            }

            // ── 情境 B：正常邊緣觸發（首次寫入） ──
            if (isGreen && (!prev.isGreen || dVal != prev.dValue) && !isManual && !isOutOfRange)
            {
                _logger.LogInformation("[LogicFlow] 控制寫入觸發: {SID} = {Value} (邏輯: {Name}) | prevGreen={PrevGreen} prevVal={PrevVal}",
                    nd.Sid, dVal!.Value, ctx.Name, prev.isGreen, prev.dValue);
                var isWriteOk = await ExecuteControlWriteAsync(nd.Sid, dVal.Value);

                if (isWriteOk)
                {
                    ctx.OutputPrevState[nd.Id] = (isGreen, dVal);
                    ctx.OutputLastWriteOk[nd.Id] = DateTime.Now;
                }
                else
                {
                    // 首次失敗 → 建立重試狀態
                    ctx.RetryStates[nd.Id] = new OutputRetryState
                    {
                        nFailCount = 1,
                        dtLastRetry = DateTime.Now,
                        isAlarmRaised = false
                    };
                    _logger.LogWarning("[LogicFlow] 寫入失敗，將每 {Sec} 秒重試: {SID} = {Value}",
                        RETRY_INTERVAL.TotalSeconds, nd.Sid, dVal.Value);
                    // 不更新 prevState → 保持觸發條件
                }
            }
            else
            {
                // ── 情境 C：值未變 → 定期健康檢查（偵測目標設備離線）──
                if (isGreen && dVal.HasValue && !isManual && !isOutOfRange
                    && prev.isGreen && dVal == prev.dValue
                    && ctx.OutputLastWriteOk.TryGetValue(nd.Id, out var dtLastOk)
                    && DateTime.Now - dtLastOk >= HEALTH_CHECK_INTERVAL)
                {
                    var isCheckOk = await ExecuteControlWriteAsync(nd.Sid, dVal.Value);
                    if (isCheckOk)
                    {
                        ctx.OutputLastWriteOk[nd.Id] = DateTime.Now;
                    }
                    else
                    {
                        // 健康檢查失敗 → 進入重試流程
                        ctx.RetryStates[nd.Id] = new OutputRetryState
                        {
                            nFailCount = 1,
                            dtLastRetry = DateTime.Now,
                            isAlarmRaised = false
                        };
                        _logger.LogWarning("[LogicFlow] 健康檢查寫入失敗，目標可能離線: {SID} (邏輯: {Name})",
                            nd.Sid, ctx.Name);
                    }
                }

                ctx.OutputPrevState[nd.Id] = (isGreen, isGreen ? dVal : null);
            }
        }
    }

    /// <summary>寫入控制失敗事件到 EventLog</summary>
    private async Task InsertControlFailEventAsync(string szSid, double dValue, string szLogicName)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<AlarmEventLogRepository>();
            await repo.InsertEventAsync(new ScadaEngine.Common.Data.Models.EventLogModel
            {
                szSID = szSid,
                nEventType = 1,  // Fault
                nSeverity = 1,   // 高
                dTriggerValue = dValue,
                szMessage = $"LogicFlow 控制寫入連續失敗 {MAX_RETRY_BEFORE_ALARM} 次 (邏輯: {szLogicName})",
                dtOccurredAt = DateTime.Now
            });
            _logger.LogError("[LogicFlow] 已寫入事件告警: {SID} 連續寫入失敗 {N} 次", szSid, MAX_RETRY_BEFORE_ALARM);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LogicFlow] 寫入事件告警失敗: {SID}", szSid);
        }
    }

    /// <summary>標記控制失敗事件恢復（寫入成功時呼叫）</summary>
    private async Task ClearControlFailEventAsync(string szSid)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<AlarmEventLogRepository>();
            await repo.ClearEventAsync(szSid);
            _logger.LogInformation("[LogicFlow] 控制恢復，事件已清除: {SID}", szSid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LogicFlow] 清除事件告警失敗: {SID}", szSid);
        }
    }

    // ─── 單節點求值 ──────────────────────────────────────────────────────────

    private bool EvalOneNode(DiagramContext ctx, FlowNode nd)
    {
        switch (nd.Type)
        {
            case "math": return EvalMath(ctx, nd);
            case "compare": return EvalCompare(ctx, nd);
            case "and":
            case "or":
            case "not":
            case "xor": return EvalGate(ctx, nd);
            case "timer": return EvalTimer(ctx, nd);
            case "contact_no":
            case "contact_nc": return EvalContact(ctx, nd);
            case "algorithm": return EvalAlgorithm(ctx, nd);
            case "counter": return EvalCounter(ctx, nd);
            default: return false;
        }
    }

    /// <summary>
    /// CTU 計數器：cu 上升緣 +1，達 preset 後 cv 飽和；reset 高電位歸零，優先於 cu。
    /// reset 支援 q→reset 自回授（不等待自身上一輪完成，直接讀 nd.Result 即上一 tick 的 q）。
    /// 累加值不持久化，Engine 重啟歸零。
    /// </summary>
    private bool EvalCounter(DiagramContext ctx, FlowNode nd)
    {
        // ── 1. preset：優先輸入腳，其次節點設定 ──
        int nPreset = nd.PresetValue;
        var presetEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "preset");
        if (presetEdge != null)
        {
            var pv = GetNodeOutputValue(ctx, presetEdge.Source, presetEdge.SourcePort);
            if (pv.HasValue) nPreset = Math.Max(1, (int)pv.Value);
            else if (!IsSourceEvalDone(ctx, presetEdge.Source)) return false;
        }
        if (nPreset < 1) nPreset = 1;

        // ── 2. cu：邊緣偵測；上游 Bad 時保留 prevCu ──
        var cuEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "cu");
        double? dCuVal = null;
        bool isCuBad = false;
        if (cuEdge != null)
        {
            if (HasUpstreamBadFromSource(ctx, cuEdge.Source, cuEdge.SourcePort))
            {
                isCuBad = true;
            }
            else
            {
                var v = GetNodeOutputValue(ctx, cuEdge.Source, cuEdge.SourcePort);
                if (v.HasValue) dCuVal = v.Value;
                else if (!IsSourceEvalDone(ctx, cuEdge.Source)) return false;
            }
        }

        // ── 3. reset：自回授特例（直接讀 nd.Result 上一 tick 的 q），其餘走標準等待 ──
        var resetEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "reset");
        double? dResetVal = null;
        if (resetEdge != null)
        {
            if (resetEdge.Source == nd.Id)
            {
                // q→reset 自回授：用 nd.Result（上一 tick 的 q，因 counter 不清 Result）
                dResetVal = nd.Result;
            }
            else if (!HasUpstreamBadFromSource(ctx, resetEdge.Source, resetEdge.SourcePort))
            {
                var v = GetNodeOutputValue(ctx, resetEdge.Source, resetEdge.SourcePort);
                if (v.HasValue) dResetVal = v.Value;
                else if (!IsSourceEvalDone(ctx, resetEdge.Source)) return false;
            }
            // upstream Bad → dResetVal stays null = 忽略 reset
        }

        // ── 4. reset 優先 ──
        bool isReset = dResetVal.HasValue && dResetVal.Value != 0;
        if (isReset)
        {
            nd.CounterValue = 0;
            // 同步更新 prevCu，避免 reset 期間若 cu 仍為高，後續被誤判為新邊緣
            if (dCuVal.HasValue) nd.CounterPrevCu = dCuVal;
        }
        else if (dCuVal.HasValue && !isCuBad)
        {
            // ── 5. cu 上升緣偵測（首次載入只記錄、不偵測，避免 Engine 啟動瞬間誤計數）──
            if (nd.CounterPrevCu == null)
            {
                nd.CounterPrevCu = dCuVal;
            }
            else
            {
                bool isEdge = nd.CounterPrevCu.Value == 0 && dCuVal.Value != 0;
                if (isEdge)
                {
                    var dtNow = DateTime.Now;
                    int nMinMs = Math.Max(0, nd.CuMinIntervalMs);
                    bool isFirstEdge = nd.CounterLastEdgeAt == DateTime.MinValue;
                    if (isFirstEdge || (dtNow - nd.CounterLastEdgeAt).TotalMilliseconds >= nMinMs)
                    {
                        if (nd.CounterValue < nPreset) nd.CounterValue++;
                        nd.CounterLastEdgeAt = dtNow;
                    }
                }
                nd.CounterPrevCu = dCuVal;
            }
        }
        else if (!dCuVal.HasValue && !isCuBad && cuEdge != null)
        {
            // 上游已完成但輸出 null → 視為 0；下次有值會被當成新邊緣
            nd.CounterPrevCu = 0;
        }
        // isCuBad：保留 CounterPrevCu，等下次資料恢復

        // ── 6. 輸出 ──
        nd.Result = nd.CounterValue >= nPreset ? 1.0 : 0.0;
        nd.IsDone = true;
        return true;
    }

    /// <summary>從某來源節點 + 來源 port 往上遞迴檢查是否有 Bad input（給 counter 等需 per-port 判斷的節點用）。
    /// algorithm 節點走 per-port severity 判斷：只有該 sourcePort 的 status = Error 才視為 Bad。</summary>
    private bool HasUpstreamBadFromSource(DiagramContext ctx, int nSourceId, string szSourcePort = "out")
    {
        var srcNode = ctx.Nodes.Find(n => n.Id == nSourceId);
        if (srcNode == null) return false;
        if (srcNode.Type == "input" && !string.IsNullOrEmpty(srcNode.Sid))
        {
            if (IsInputSourceBad(srcNode))
                return true;
        }
        if (srcNode.Type == "algorithm" && IsAlgoPortBad(srcNode, szSourcePort))
            return true;
        if (srcNode.Type is "math" or "compare" or "and" or "or" or "not" or "xor" or "timer" or "contact_no" or "contact_nc" or "algorithm" or "counter")
        {
            return HasUpstreamBad(ctx, srcNode.Id);
        }
        return false;
    }

    private bool EvalMath(DiagramContext ctx, FlowNode nd)
    {
        if (HasUpstreamBad(ctx, nd.Id)) return false;
        var v = GetInputValue(ctx, nd.Id, "in");
        if (!v.HasValue) return false;

        double dResult;
        switch (nd.Operator)
        {
            case "abs":   dResult = Math.Abs(v.Value); break;
            case "sqrt":  dResult = Math.Sqrt(v.Value); break;
            case "round": dResult = Math.Round(v.Value); break;
            default:
            {
                var p = GetInputValue(ctx, nd.Id, "val");
                if (!p.HasValue) return false;
                dResult = nd.Operator switch
                {
                    "add" => v.Value + p.Value,
                    "sub" => v.Value - p.Value,
                    "mul" => v.Value * p.Value,
                    "div" => p.Value != 0 ? v.Value / p.Value : 0,
                    "mod" => p.Value != 0 ? v.Value % p.Value : 0,
                    "pow" => Math.Pow(v.Value, p.Value),
                    _ => v.Value
                };
                break;
            }
        }
        nd.Result = dResult;
        nd.IsDone = true;
        return true;
    }

    private bool EvalCompare(DiagramContext ctx, FlowNode nd)
    {
        if (HasUpstreamBad(ctx, nd.Id)) return false;
        var a = GetInputValue(ctx, nd.Id, "a");
        var b = GetInputValue(ctx, nd.Id, "b");
        if (!a.HasValue || !b.HasValue) return false;

        bool isMet = nd.Operator switch
        {
            "lt"  => a.Value < b.Value,
            "gt"  => a.Value > b.Value,
            "lte" => a.Value <= b.Value,
            "gte" => a.Value >= b.Value,
            "eq"  => Math.Abs(a.Value - b.Value) < 1e-9,
            "neq" => Math.Abs(a.Value - b.Value) >= 1e-9,
            _ => false
        };
        nd.Result = isMet ? 1 : 0;
        nd.IsDone = true;
        return true;
    }

    private bool EvalGate(DiagramContext ctx, FlowNode nd)
    {
        if (HasUpstreamBad(ctx, nd.Id)) return false;

        if (nd.Type == "not")
        {
            var v = GetInputValue(ctx, nd.Id, "in");
            if (!v.HasValue) return false;
            nd.Result = v.Value == 1 ? 0 : 1;
            nd.IsDone = true;
            return true;
        }

        var a = GetInputValue(ctx, nd.Id, "a");
        var b = GetInputValue(ctx, nd.Id, "b");
        if (!a.HasValue || !b.HasValue) return false;

        int nA = a.Value == 1 ? 1 : 0;
        int nB = b.Value == 1 ? 1 : 0;

        nd.Result = nd.Type switch
        {
            "and" => (nA == 1 && nB == 1) ? 1 : 0,
            "or"  => (nA == 1 || nB == 1) ? 1 : 0,
            "xor" => (nA != nB) ? 1 : 0,
            _ => 0
        };
        nd.IsDone = true;
        return true;
    }

    private bool EvalTimer(DiagramContext ctx, FlowNode nd)
    {
        if (nd.Operator == "ton") return EvalTimerTon(ctx, nd);
        if (nd.Operator == "tpr") return EvalTimerTpr(ctx, nd);

        // TP 脈衝：質變觸發 — 輸入值改變 → delay → hold(輸出) → 閒置等待下次質變
        var inEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "in");
        double dPassValue = nd.TpLastPassValue;

        // ── 1. 取得當前輸入值 ──
        double? dCurrentInput = null;
        if (inEdge != null)
        {
            if (HasUpstreamBad(ctx, nd.Id))
            {
                ResetTpState(nd);
                nd.IsDone = true;
                return true;
            }
            var v = GetNodeOutputValue(ctx, inEdge.Source, inEdge.SourcePort);
            if (v.HasValue)
            {
                dCurrentInput = v.Value;
                dPassValue = v.Value;
                nd.TpLastPassValue = dPassValue;
            }
            else if (!IsSourceEvalDone(ctx, inEdge.Source))
            {
                return false;  // 上游尚未完成本輪評估
            }
            // else: 上游完成但 null → dCurrentInput 保持 null
        }
        else
        {
            dCurrentInput = 1;  // 無輸入連線 → 視為常數 1
        }

        double dEffDelay = GetInputValue(ctx, nd.Id, "delay") ?? nd.TimerDelay;
        double dEffHold = GetInputValue(ctx, nd.Id, "hold") ?? nd.TimerHold;
        int nDelayMs = Math.Max((int)(dEffDelay * 1000), 500);
        int nHoldMs = Math.Max((int)(dEffHold * 1000), 500);
        var now = DateTime.Now;

        // ── 2. 質變偵測：輸入值與上次不同時觸發新週期 ──
        if (dCurrentInput.HasValue)
        {
            if (!nd.TpPrevInputValue.HasValue || nd.TpPrevInputValue.Value != dCurrentInput.Value)
            {
                // 值改變 → (重新)啟動 delay 階段
                nd.TpPrevInputValue = dCurrentInput;
                nd.TpTimer?.Dispose();
                nd.TpTimer = null;
                nd.TpPhase = "delay";
                nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
                nd.TpHasHeld = false;
                ScheduleTpTimer(ctx, nd, nDelayMs);
            }
        }
        else
        {
            // 上游輸出 null（未導通等）→ 重置 prev，讓下次有值時視為質變
            nd.TpPrevInputValue = null;
        }

        // ── 3. 閒置狀態：等待質變 ──
        if (nd.TpPhase == null)
        {
            nd.Result = null;
            nd.IsDone = true;
            return true;
        }

        // ── 4. 階段轉換 ──
        if (now >= nd.TpPhaseEnd)
        {
            if (nd.TpPhase == "delay")
            {
                nd.TpPhase = "hold";
                nd.TpPhaseEnd = now.AddMilliseconds(nHoldMs);
                nd.TpHasHeld = true;
                ScheduleTpTimer(ctx, nd, nHoldMs);
            }
            else
            {
                // hold 結束 → 回到閒置（不再自動重啟 delay）
                nd.TpPhase = null;
                nd.TpPhaseEnd = DateTime.MinValue;
                nd.Result = null;
                nd.IsDone = true;
                return true;
            }
        }

        // ── 5. 輸出：hold 階段傳遞值，delay 階段輸出 null ──
        nd.Result = nd.TpPhase == "hold" ? dPassValue : null;
        nd.IsDone = true;
        return true;
    }

    /// <summary>TON 延時開啟：輸入持續 ON 達 delay 秒後輸出 ON，輸入 OFF 立即重置</summary>
    private bool EvalTimerTon(DiagramContext ctx, FlowNode nd)
    {
        var inEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "in");
        double dInputVal = 1;
        if (inEdge != null)
        {
            if (HasUpstreamBad(ctx, nd.Id))
            {
                nd.TonPhase = null;
                nd.TonPhaseEnd = DateTime.MinValue;
                nd.IsDone = true;
                nd.Result = null;
                return true;
            }
            var v = GetNodeOutputValue(ctx, inEdge.Source, inEdge.SourcePort);
            if (!v.HasValue) return false;
            dInputVal = v.Value;
        }

        bool isInputOn = dInputVal != 0;
        double dEffDelay = GetInputValue(ctx, nd.Id, "delay") ?? nd.TimerDelay;
        int nDelayMs = Math.Max((int)(dEffDelay * 1000), 500);
        var now = DateTime.Now;

        if (!isInputOn)
        {
            nd.TonPhase = null;
            nd.TonPhaseEnd = DateTime.MinValue;
            nd.Result = 0;
            nd.IsDone = true;
            return true;
        }

        if (nd.TonPhase == null)
        {
            nd.TonPhase = "timing";
            nd.TonPhaseEnd = now.AddMilliseconds(nDelayMs);
            ScheduleTpTimer(ctx, nd, nDelayMs);
        }

        if (now >= nd.TonPhaseEnd)
        {
            nd.TonPhase = "on";
            nd.Result = dInputVal;
        }
        else
        {
            nd.Result = 0;
        }
        nd.IsDone = true;
        return true;
    }

    /// <summary>TPR 延遲導通 + 回饋重送：
    /// delay 倒數中輸出 null（值變會 debounce 重置）；
    /// 倒數結束輸出 passValue 並進入 confirmed（同時開始 settling 倒數，期間不檢查回饋）；
    /// confirmed 內若輸入值變 (passValue ≠ TprLastSentValue) 或下游回饋偏離 → 回 delay。
    /// TpPhaseEnd 雙語意：delay 階段=倒數結束時間；confirmed 階段=settling 結束時間。</summary>
    private bool EvalTimerTpr(DiagramContext ctx, FlowNode nd)
    {
        // ── 1. 取得輸入值 ──
        var inEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "in");
        double dPassValue = nd.TpLastPassValue;
        double? dCurrentInput = null;

        if (inEdge != null)
        {
            if (HasUpstreamBad(ctx, nd.Id))
            {
                ResetTpState(nd);
                nd.IsDone = true;
                return true;
            }
            var v = GetNodeOutputValue(ctx, inEdge.Source, inEdge.SourcePort);
            if (v.HasValue)
            {
                dCurrentInput = v.Value;
                dPassValue = v.Value;
                nd.TpLastPassValue = dPassValue;
            }
            else if (!IsSourceEvalDone(ctx, inEdge.Source))
            {
                return false;
            }
        }
        else
        {
            dCurrentInput = 1;
        }

        // 輸入 null → 中止循環
        if (!dCurrentInput.HasValue)
        {
            ResetTpState(nd);
            nd.IsDone = true;
            return true;
        }

        // ── 2. 回饋偵測：找下游 output 節點的即時值 ──
        if (nd.TprFeedbackSid == null)
            nd.TprFeedbackSid = FindDownstreamOutputSid(ctx, nd.Id);

        double? dFeedback = null;
        if (nd.TprFeedbackSid != null && _latestCache.TryGetValue(nd.TprFeedbackSid, out var lv))
            dFeedback = lv.dValue;

        double dEffDelay = GetInputValue(ctx, nd.Id, "delay") ?? nd.TimerDelay;
        int nDelayMs = Math.Max((int)(dEffDelay * 1000), 500);
        var now = DateTime.Now;

        // ── 3. 狀態機 ──

        // 初始：進入 delay 倒數，輸出 null
        if (nd.TpPhase == null)
        {
            nd.TpPhase = "delay";
            nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
            nd.TprPrevInput = dCurrentInput;
            ScheduleTpTimer(ctx, nd, nDelayMs);
            nd.Result = null;
            nd.IsDone = true;
            return true;
        }

        // confirmed：已輸出 passValue，視 settling 與回饋狀況決定是否回 delay
        if (nd.TpPhase == "confirmed")
        {
            // (a) 輸入值變化 → 回 delay 重啟倒數
            if (nd.TprLastSentValue.HasValue
                && Math.Abs(dPassValue - nd.TprLastSentValue.Value) >= 0.001)
            {
                nd.TpPhase = "delay";
                nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
                nd.TprPrevInput = dCurrentInput;
                ScheduleTpTimer(ctx, nd, nDelayMs);
                nd.Result = null;
                nd.IsDone = true;
                return true;
            }

            // (b) settling 中（now < TpPhaseEnd）→ 不檢查回饋，持續輸出 passValue
            if (now < nd.TpPhaseEnd)
            {
                nd.Result = dPassValue;
                nd.IsDone = true;
                return true;
            }

            // (c) settling 已過 → 檢查回饋
            bool isFeedbackMatch = dFeedback.HasValue && Math.Abs(dFeedback.Value - dPassValue) < 0.001;
            if (isFeedbackMatch)
            {
                nd.Result = dPassValue;
                nd.IsDone = true;
                return true;
            }
            // 回饋偏離 → 回 delay 重新計時 + 重送
            nd.TpPhase = "delay";
            nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
            nd.TprPrevInput = dCurrentInput;
            ScheduleTpTimer(ctx, nd, nDelayMs);
            nd.Result = null;
            nd.IsDone = true;
            return true;
        }

        // delay：倒數中
        // (a) 值變偵測 → debounce 重置倒數
        if (nd.TprPrevInput.HasValue
            && Math.Abs(dCurrentInput.Value - nd.TprPrevInput.Value) >= 0.001)
        {
            nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
            nd.TprPrevInput = dCurrentInput;
            ScheduleTpTimer(ctx, nd, nDelayMs);
            nd.Result = null;
            nd.IsDone = true;
            return true;
        }

        // (b) 倒數結束 → 進入 confirmed、輸出 passValue、啟動 settling
        if (now >= nd.TpPhaseEnd)
        {
            nd.TpPhase = "confirmed";
            nd.TprLastSentValue = dPassValue;
            nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);  // settling 結束時間
            ScheduleTpTimer(ctx, nd, nDelayMs);
            nd.Result = dPassValue;
            nd.IsDone = true;
            return true;
        }

        // (c) 倒數中且輸入未變 → 輸出 null
        nd.Result = null;
        nd.IsDone = true;
        return true;
    }

    /// <summary>從指定節點往下游遍歷，找到第一個 output 節點的 SID</summary>
    private string? FindDownstreamOutputSid(DiagramContext ctx, int nStartNodeId)
    {
        var visited = new HashSet<int> { nStartNodeId };
        var queue = new Queue<int>();
        // 找從此節點出發的邊
        foreach (var e in ctx.Edges.Where(e => e.Source == nStartNodeId))
            queue.Enqueue(e.Target);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId)) continue;
            var target = ctx.Nodes.Find(n => n.Id == nodeId);
            if (target == null) continue;
            if (target.Type == "output" && !string.IsNullOrEmpty(target.Sid))
                return target.Sid;
            // 繼續往下游
            foreach (var e in ctx.Edges.Where(e => e.Source == nodeId))
                queue.Enqueue(e.Target);
        }
        return null;
    }

    private bool EvalContact(DiagramContext ctx, FlowNode nd)
    {
        // ── ctrl 埠模式（邏輯閘控制導通）──
        var ctrlEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "ctrl");
        if (ctrlEdge != null)
        {
            var ctrlVal = GetNodeOutputValue(ctx, ctrlEdge.Source, ctrlEdge.SourcePort);
            if (!ctrlVal.HasValue)
            {
                if (!IsSourceEvalDone(ctx, ctrlEdge.Source))
                    return false;  // 上游尚未完成本輪評估
                nd.Result = null;
                nd.IsDone = true;
                return true;
            }
            bool isOnCtrl = nd.Type == "contact_no" ? (ctrlVal.Value == 1) : (ctrlVal.Value == 0);

            var hasInEdgeCtrl = ctx.Edges.Any(e => e.Target == nd.Id && e.TargetPort == "in");
            var inValCtrl = hasInEdgeCtrl ? GetInputValue(ctx, nd.Id, "in") : null;

            if (hasInEdgeCtrl && !inValCtrl.HasValue)
            {
                if (!IsPortSourceDone(ctx, nd.Id, "in"))
                    return false;
                nd.Result = null;
                nd.IsDone = true;
                return true;
            }

            nd.Result = isOnCtrl ? (inValCtrl ?? 1) : (inValCtrl.HasValue ? null : 0);
            nd.IsDone = true;
            return true;
        }

        // ── 排程/點位模式的上游品質檢查（ctrl 模式已獨立處理，不受此影響）──
        if (HasUpstreamBad(ctx, nd.Id)) return false;

        // ── 排程模式 ──
        if (nd.ScheduleId.HasValue)
        {
            if (!_scheduleCache.TryGetValue(nd.ScheduleId.Value, out var sch))
                return false;

            bool isActive = EvalScheduleIsActive(sch);
            bool isOn = nd.Type == "contact_no" ? isActive : !isActive;

            // 檢查是否有連線到 in port（區分「沒有注入」vs「上游未就緒」）
            var hasInEdgeSch = ctx.Edges.Any(e => e.Target == nd.Id && e.TargetPort == "in");
            var inVal = hasInEdgeSch ? GetInputValue(ctx, nd.Id, "in") : null;

            // 有連線但值為 null
            if (hasInEdgeSch && !inVal.HasValue)
            {
                // 上游尚未完成本輪評估 → 等待下一 pass
                if (!IsPortSourceDone(ctx, nd.Id, "in"))
                    return false;
                // 上游已完成但輸出 null（algo grace period 等）→ 傳遞 null，不預設 1
                nd.Result = null;
                nd.IsDone = true;
                return true;
            }

            nd.Result = isOn ? (inVal ?? 1) : (inVal.HasValue ? null : 0);
            nd.IsDone = true;
            return true;
        }

        // ── 點位模式 ──
        if (string.IsNullOrEmpty(nd.Sid)) return false;

        double dPointVal;
        if (nd.HistEnabled)
        {
            // 歷史值讀取：查無值 → 不完成本節點（下游不評估、Output 不寫入）
            if (!TryGetHistoryValue(nd, out dPointVal)) return false;
        }
        else
        {
            if (!_latestCache.TryGetValue(nd.Sid, out var lv)) return false;
            if (lv.nQuality != 1) return false;
            dPointVal = lv.dValue;
        }

        bool isOnPt = nd.Type == "contact_no" ? (dPointVal == 1) : (dPointVal == 0);

        // 檢查是否有連線到 in port（區分「沒有注入」vs「上游未就緒」）
        var hasInEdge = ctx.Edges.Any(e => e.Target == nd.Id && e.TargetPort == "in");
        var inValPt = hasInEdge ? GetInputValue(ctx, nd.Id, "in") : null;

        // 有連線但值為 null
        if (hasInEdge && !inValPt.HasValue)
        {
            // 上游尚未完成本輪評估 → 等待下一 pass
            if (!IsPortSourceDone(ctx, nd.Id, "in"))
                return false;
            // 上游已完成但輸出 null（algo grace period 等）→ 傳遞 null
            nd.Result = null;  // 讓下游 TP 用 TpLastPassValue keep 住
            nd.IsDone = true;
            return true;
        }

        if (isOnPt)
            nd.Result = inValPt.HasValue ? inValPt.Value : 1;
        else
            nd.Result = inValPt.HasValue ? null : 0;
        nd.IsDone = true;
        return true;
    }

    /// <summary>
    /// 評估演算法節點：優先嘗試 C# in-process 呼叫，失敗退回 Python HTTP。
    /// </summary>
    private bool EvalAlgorithm(DiagramContext ctx, FlowNode nd)
    {
        if (HasUpstreamBad(ctx, nd.Id)) return false;
        if (string.IsNullOrEmpty(nd.Operator)) return false;

        // 收集所有輸入 port 的值（algoInputs 已是展開後的清單，含 variadic 展開的 cooling_capacity1, power1, ...）
        var algoInputs = nd.AlgoInputs ?? new List<string> { "in" };
        var inputDict = new Dictionary<string, double>();
        foreach (var portName in algoInputs)
        {
            var v = GetInputValue(ctx, nd.Id, portName);
            if (!v.HasValue) return false;  // 任一輸入尚未就緒
            inputDict[portName] = v.Value;
        }

        // ★ 優先嘗試 C# 演算法（同步 in-process，零延遲；第一版 .cs 不支援 variadic，所以不會帶 n）
        if (_csharpAlgoService.TryEvaluate(nd.Operator, inputDict, out var csResult, out var csStatus, out var csPerOutput) && csResult.Count > 0)
        {
            var perOutputTuples = ConvertCsPerOutput(csPerOutput);
            ApplyAlgoResult(nd, csResult, csStatus.CodeId, csStatus.CodeName, csStatus.Severity, perOutputTuples);
            HandleAlgoStatusTransition(ctx, nd, csStatus.CodeId, csStatus.CodeName, csStatus.Severity, perOutputTuples);
            nd.IsDone = true;
            return true;
        }

        // ★ 退回 Python HTTP
        // 快取 key：以輸入值的 hash 判斷是否需要重新呼叫
        var szInputHash = string.Join("|", inputDict.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value:G}"));
        var szCacheKey = $"{ctx.TreeId}-{nd.Id}-N{nd.InputCount?.ToString() ?? "_"}-{szInputHash}";
        var szNodeKey = $"{ctx.TreeId}-{nd.Id}";

        if (_algoResultCache.TryGetValue(szNodeKey, out _))
        {
            // 檢查輸入是否變化
            if (_algoResultCache.TryGetValue(szCacheKey, out var cachedOut))
            {
                ApplyAlgoResult(nd, cachedOut.Result, cachedOut.StatusCodeId, cachedOut.StatusCodeName, cachedOut.Severity, cachedOut.PerOutput);
                HandleAlgoStatusTransition(ctx, nd, cachedOut.StatusCodeId, cachedOut.StatusCodeName, cachedOut.Severity, cachedOut.PerOutput);
                nd.IsDone = true;
                return true;
            }
        }

        // 非同步呼叫 Python — 使用 fire-and-forget + cache 寫回
        // （EvalOneNode 是同步的，無法 await，因此先回傳上次結果，背景更新）
        var treeId = ctx.TreeId;
        var nodeId = nd.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                object payloadObj = nd.InputCount.HasValue
                    ? new { inputs = inputDict, n = nd.InputCount.Value }
                    : new { inputs = inputDict };
                var payload = JsonSerializer.Serialize(payloadObj);
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _algoHttpClient.PostAsync($"/algorithms/{nd.Operator}/evaluate", content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.Object)
                    {
                        var dResult = new Dictionary<string, double>();
                        foreach (var prop in resultEl.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number)
                                dResult[prop.Name] = prop.Value.GetDouble();
                        }
                        if (dResult.Count > 0)
                        {
                            var (sCodeId, sCodeName, sSeverity) = ParsePythonStatus(root);
                            var perOutput = ParsePythonPerOutput(root, dResult, sCodeId, sCodeName, sSeverity);

                            // 清除舊的快取項，寫入新的
                            foreach (var key in _algoResultCache.Keys.Where(k => k.StartsWith($"{treeId}-{nodeId}-")).ToList())
                                _algoResultCache.TryRemove(key, out _);
                            var cached = new AlgoCachedOutput(dResult, sCodeId, sCodeName, sSeverity, perOutput);
                            _algoResultCache[szCacheKey] = cached;
                            _algoResultCache[szNodeKey] = cached;  // 最新值快取
                            _algoPendingSince.TryRemove(szNodeKey, out _);

                            // 狀態轉換在主迴圈的下一輪由 EvalAlgorithm cache-hit 路徑統一處理
                            // （此處在 Task.Run 中，避免與主迴圈並發寫 EventLog）
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // Engine 連 Python 服務失敗 → 將狀態標為 API_CALL_FAILED
                _logger.LogDebug(ex, "Python 演算法服務連線失敗 {Algo}", nd.Operator);
                var failedTuple = (CodeId: 41, CodeName: "API_CALL_FAILED", Severity: "Error");
                var failed = new AlgoCachedOutput(
                    new Dictionary<string, double> { ["out"] = 0 },
                    failedTuple.CodeId, failedTuple.CodeName, failedTuple.Severity,
                    new Dictionary<string, (int CodeId, string CodeName, string Severity)> { ["out"] = failedTuple });
                _algoResultCache[szNodeKey] = failed;
                _algoResultCache[szCacheKey] = failed;
                _algoPendingSince.TryRemove(szNodeKey, out _);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "呼叫 Python 演算法 {Algo} 失敗", nd.Operator);
            }
        });

        // 第一次呼叫尚無快取：嘗試用最新值快取
        if (_algoResultCache.TryGetValue(szNodeKey, out var lastOut))
        {
            ApplyAlgoResult(nd, lastOut.Result, lastOut.StatusCodeId, lastOut.StatusCodeName, lastOut.Severity, lastOut.PerOutput);
            HandleAlgoStatusTransition(ctx, nd, lastOut.StatusCodeId, lastOut.StatusCodeName, lastOut.Severity, lastOut.PerOutput);
            nd.IsDone = true;
            return true;
        }

        // Grace period：首次呼叫後給 Python 一段緩衝時間，期間輸出 null（不觸發下游）
        var dtNow = DateTime.UtcNow;
        var dtPending = _algoPendingSince.GetOrAdd(szNodeKey, dtNow);
        if (dtNow - dtPending < ALGO_GRACE_PERIOD)
        {
            nd.Result = null;
            nd.AlgoResultDict = null;
            nd.AlgoStatusCodeId = 0;
            nd.AlgoStatusCodeName = "OK";
            nd.AlgoStatusSeverity = "Info";
            nd.AlgoPerOutputStatus = null;
            nd.IsDone = true;
            return true;
        }

        return false;  // 超過 grace period 仍無結果
    }

    /// <summary>把 CSharpAlgorithmStatus 字典轉成 tuple 字典（供 FlowNode / cache 使用）。</summary>
    private static Dictionary<string, (int CodeId, string CodeName, string Severity)> ConvertCsPerOutput(
        Dictionary<string, CSharpAlgorithmStatus> csPerOutput)
    {
        var result = new Dictionary<string, (int CodeId, string CodeName, string Severity)>(csPerOutput.Count);
        foreach (var (k, v) in csPerOutput)
            result[k] = (v.CodeId, v.CodeName, v.Severity);
        return result;
    }

    /// <summary>從 Python /evaluate JSON 解析 perOutput；缺欄位（舊版）→ 所有 result key 套 merged status。</summary>
    private static Dictionary<string, (int CodeId, string CodeName, string Severity)> ParsePythonPerOutput(
        JsonElement root, Dictionary<string, double> result, int mergedCodeId, string mergedCodeName, string mergedSeverity)
    {
        var perOutput = new Dictionary<string, (int CodeId, string CodeName, string Severity)>();
        if (root.TryGetProperty("perOutput", out var poEl) && poEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in poEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                int codeId = 0; string codeName = "OK"; string severity = "Info";
                if (prop.Value.TryGetProperty("statusCodeId", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    codeId = idEl.GetInt32();
                if (prop.Value.TryGetProperty("statusCodeName", out var nmEl) && nmEl.ValueKind == JsonValueKind.String)
                    codeName = nmEl.GetString() ?? "OK";
                if (prop.Value.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String)
                    severity = sevEl.GetString() ?? "Info";
                perOutput[prop.Name] = (codeId, codeName, severity);
            }
        }
        // fallback：缺 perOutput → 所有 result key 套 merged
        if (perOutput.Count == 0)
        {
            foreach (var k in result.Keys)
                perOutput[k] = (mergedCodeId, mergedCodeName, mergedSeverity);
        }
        return perOutput;
    }

    /// <summary>從 Python /evaluate 回傳 JSON 解析 status 三欄位（兼容舊回應）</summary>
    private static (int codeId, string codeName, string severity) ParsePythonStatus(JsonElement root)
    {
        int codeId = 0;
        string codeName = "OK";
        string severity = "Info";
        if (root.TryGetProperty("statusCodeId", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            codeId = idEl.GetInt32();
        if (root.TryGetProperty("statusCodeName", out var nmEl) && nmEl.ValueKind == JsonValueKind.String)
            codeName = nmEl.GetString() ?? "OK";
        if (root.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String)
            severity = sevEl.GetString() ?? "Info";
        // 舊版只有 quality，無 status 三欄 → 視為 OK
        return (codeId, codeName, severity);
    }

    /// <summary>把演算法回傳 dict + status 套到節點。HasUpstreamBad 走 per-output severity 判斷，整顆 Result 不再需要因 Error 設 null。</summary>
    private static void ApplyAlgoResult(FlowNode nd, Dictionary<string, double> result,
                                        int statusCodeId, string statusCodeName, string severity,
                                        Dictionary<string, (int CodeId, string CodeName, string Severity)> perOutput)
    {
        nd.AlgoResultDict = result;
        nd.Result = result.TryGetValue("out", out var dOut) ? dOut : result.Values.First();
        nd.AlgoStatusCodeId = statusCodeId;
        nd.AlgoStatusCodeName = statusCodeName;
        nd.AlgoStatusSeverity = severity;
        nd.AlgoPerOutputStatus = perOutput;
    }

    /// <summary>per-output 狀態變化偵測：每個 output key 各自跑 OK ↔ 非 OK 切換寫 EventLog。
    /// SID 格式：ALGO:{nodeId}@{treeId}:{outputKey}，避免一組混合狀態被聚合掩蓋。</summary>
    private void HandleAlgoStatusTransition(DiagramContext ctx, FlowNode nd,
                                            int mergedCodeId, string mergedCodeName, string mergedSeverity,
                                            Dictionary<string, (int CodeId, string CodeName, string Severity)> perOutput)
    {
        var nodeName = string.IsNullOrEmpty(nd.Operator) ? $"node#{nd.Id}" : nd.Operator;
        foreach (var (outputKey, status) in perOutput)
        {
            var szKey = $"{ctx.TreeId}-{nd.Id}:{outputKey}";
            var prev = _algoLastStatus.TryGetValue(szKey, out var p) ? p : new AlgoStatusSnapshot(0, "OK", "Info");

            if (prev.CodeId == status.CodeId) continue;  // 沒變化

            var szSid = $"ALGO:{nd.Id}@{ctx.TreeId}:{outputKey}";

            // 1) 先前為非 OK → 清舊事件
            if (prev.CodeId != 0)
            {
                var sidToClear = szSid;
                _ = Task.Run(async () =>
                {
                    try { await _alarmEventLogRepo.ClearEventAsync(sidToClear); }
                    catch (Exception ex) { _logger.LogWarning(ex, "演算法 status 清舊事件失敗 {Sid}", sidToClear); }
                });
            }

            // 2) 新狀態為非 OK 且 severity 非 Info → 寫新事件
            if (status.CodeId != 0 && status.Severity != "Info")
            {
                byte nEventType = status.Severity == "Error" ? (byte)1 : (byte)2;
                byte nSeverity = status.Severity == "Error" ? (byte)1 : (byte)2;
                var szMessage = $"{nodeName}.{outputKey}: {status.CodeName} ({status.Severity})";

                var model = new ScadaEngine.Common.Data.Models.EventLogModel
                {
                    szSID = szSid,
                    nEventType = nEventType,
                    nSeverity = nSeverity,
                    szMessage = szMessage,
                    szMessageKey = null,
                    szMessageArgs = $"{{\"statusCodeId\":{status.CodeId},\"statusCodeName\":\"{status.CodeName}\",\"severity\":\"{status.Severity}\",\"node\":\"{nodeName}\",\"outputKey\":\"{outputKey}\"}}",
                    dtOccurredAt = DateTime.Now,
                };

                _ = Task.Run(async () =>
                {
                    try { await _alarmEventLogRepo.InsertEventAsync(model); }
                    catch (Exception ex) { _logger.LogWarning(ex, "演算法 status 寫 EventLog 失敗 {Sid}", szSid); }
                });
            }

            _algoLastStatus[szKey] = new AlgoStatusSnapshot(status.CodeId, status.CodeName, status.Severity);
        }
    }

    /// <summary>
    /// 評估排程是否在有效時段內。
    /// 跨日邏輯：以「啟始日」為日期條件比對基準 — 凌晨段（now &lt; endTime）視為昨天那筆排程的延續。
    /// 例外日 / 加開日皆以基準日比對，加開日無視重複規則強制啟動，例外日當天整段停。
    /// </summary>
    private static bool EvalScheduleIsActive(ScheduleRecord sch)
    {
        var now = DateTime.Now;

        if (string.IsNullOrEmpty(sch.StartTime) || string.IsNullOrEmpty(sch.EndTime)) return false;

        var sp = sch.StartTime.Split(':');
        var ep = sch.EndTime.Split(':');
        if (sp.Length < 2 || ep.Length < 2) return false;
        if (!int.TryParse(sp[0], out var startH) || !int.TryParse(sp[1], out var startM)) return false;
        if (!int.TryParse(ep[0], out var endH) || !int.TryParse(ep[1], out var endM)) return false;

        int nowMin = now.Hour * 60 + now.Minute;
        int startMin = startH * 60 + startM;
        int endMin = endH * 60 + endM;
        bool isCrossDay = endMin <= startMin;

        DateTime baseDate;
        if (isCrossDay)
        {
            if (nowMin >= startMin)
                baseDate = now.Date;                    // 晚上段：今天啟動
            else if (nowMin < endMin)
                baseDate = now.Date.AddDays(-1);        // 凌晨段：屬於昨天那筆排程的延續
            else
                return false;                           // 中午段：完全不在排程內
        }
        else
        {
            if (nowMin < startMin || nowMin >= endMin) return false;
            baseDate = now.Date;
        }

        return CheckDayMatch(sch, baseDate);
    }

    /// <summary>
    /// 比對某基準日是否符合排程的日期條件。
    /// 順序：加開日 → 例外日 → 重複規則。前端 + 後端都防止例外/加開交集，運行時不會發生衝突。
    /// </summary>
    private static bool CheckDayMatch(ScheduleRecord sch, DateTime baseDate)
    {
        var szDateKey = baseDate.ToString("yyyy-MM-dd");

        if (DateListContains(sch.IncludeDates, szDateKey)) return true;
        if (DateListContains(sch.ExcludeDates, szDateKey)) return false;

        return sch.RecurrenceType switch
        {
            0 => CheckDaysOfWeek(baseDate, sch.DaysOfWeek),
            1 => CheckWeekCycle(baseDate, sch) && CheckDaysOfWeek(baseDate, sch.DaysOfWeek),
            2 => CheckDaysOfMonth(baseDate, sch.DaysOfMonth),
            3 => CheckMonthCycle(baseDate, sch) && CheckDaysOfMonth(baseDate, sch.DaysOfMonth),
            _ => false
        };
    }

    private static bool DateListContains(string? listStr, string szDateKey)
    {
        if (string.IsNullOrWhiteSpace(listStr)) return false;
        foreach (var part in listStr.Split(','))
        {
            if (part.Trim() == szDateKey) return true;
        }
        return false;
    }

    private static bool CheckDaysOfWeek(DateTime baseDate, string? daysStr)
    {
        if (string.IsNullOrEmpty(daysStr)) return false;
        int isoDay = baseDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)baseDate.DayOfWeek; // 1=Mon..7=Sun
        return daysStr.Split(',').Any(d => int.TryParse(d.Trim(), out var v) && v == isoDay);
    }

    private static bool CheckDaysOfMonth(DateTime baseDate, string? daysStr)
    {
        if (string.IsNullOrEmpty(daysStr)) return false;
        int dom = baseDate.Day;
        return daysStr.Split(',').Any(d => int.TryParse(d.Trim(), out var v) && v == dom);
    }

    private static bool CheckWeekCycle(DateTime baseDate, ScheduleRecord sch)
    {
        if (!sch.AnchorDateTime.HasValue || !sch.RunLength.HasValue || !sch.RestLength.HasValue) return false;
        var elapsed = baseDate - sch.AnchorDateTime.Value;
        if (elapsed.TotalMilliseconds < 0) return false;
        int totalCycle = sch.RunLength.Value + sch.RestLength.Value;
        int elapsedWeeks = (int)(elapsed.TotalDays / 7);
        return (elapsedWeeks % totalCycle) < sch.RunLength.Value;
    }

    private static bool CheckMonthCycle(DateTime baseDate, ScheduleRecord sch)
    {
        if (!sch.AnchorDateTime.HasValue || !sch.RunLength.HasValue || !sch.RestLength.HasValue) return false;
        var anchor = sch.AnchorDateTime.Value;
        int totalMonths = (baseDate.Year - anchor.Year) * 12 + (baseDate.Month - anchor.Month);
        if (totalMonths < 0) return false;
        int totalCycle = sch.RunLength.Value + sch.RestLength.Value;
        return (totalMonths % totalCycle) < sch.RunLength.Value;
    }

    // ─── 輔助方法 ────────────────────────────────────────────────────────────

    private double? GetNodeOutputValue(DiagramContext ctx, int nNodeId, string szSourcePort = "out")
    {
        var nd = ctx.Nodes.Find(n => n.Id == nNodeId);
        if (nd == null) return null;

        if (nd.Type == "input" && !string.IsNullOrEmpty(nd.Sid))
        {
            if (nd.HistEnabled)
                return TryGetHistoryValue(nd, out var dHist) ? dHist : null;
            if (!_latestCache.TryGetValue(nd.Sid, out var lv)) return null;
            if (lv.nQuality != 1) return null; // Bad quality
            return lv.dValue;
        }
        if (nd.Type == "constant") return nd.ConstValue ?? 0;
        if (nd.Type == "algorithm")
        {
            // 多輸出：依 edge.SourcePort 從 dict 取值；找不到才退回 Result（向後相容）
            if (nd.AlgoResultDict != null && nd.AlgoResultDict.TryGetValue(szSourcePort, out var dPortVal))
                return dPortVal;
            return nd.Result;
        }
        if (nd.Type == "counter")
        {
            // counter 多輸出：q（預設）= 是否達到 preset；cv = 目前累加值
            if (szSourcePort == "cv") return (double)nd.CounterValue;
            return nd.Result; // q
        }
        if (nd.Type is "math" or "compare" or "and" or "or" or "not" or "xor" or "timer" or "contact_no" or "contact_nc")
            return nd.Result;

        return null;
    }

    private double? GetInputValue(DiagramContext ctx, int nNodeId, string szPortName)
    {
        var edge = ctx.Edges.Find(e => e.Target == nNodeId && e.TargetPort == szPortName);
        if (edge == null) return null;
        return GetNodeOutputValue(ctx, edge.Source, edge.SourcePort);
    }

    /// <summary>上游可評估節點是否已完成本輪評估（input/constant/output 視為永遠就緒）</summary>
    private static bool IsSourceEvalDone(DiagramContext ctx, int nSourceId)
    {
        var srcNode = ctx.Nodes.Find(n => n.Id == nSourceId);
        if (srcNode == null) return true;
        if (srcNode.Type is "input" or "constant" or "output") return true;
        return srcNode.IsDone;
    }

    /// <summary>檢查某節點的某 port 上游是否已完成評估</summary>
    private static bool IsPortSourceDone(DiagramContext ctx, int nNodeId, string szPort)
    {
        var edge = ctx.Edges.Find(e => e.Target == nNodeId && e.TargetPort == szPort);
        if (edge == null) return true;
        return IsSourceEvalDone(ctx, edge.Source);
    }

    /// <summary>檢查指定節點是否有任一輸入邊線來自 Bad upstream。
    /// algorithm 節點走 per-output severity 判斷（依 edge.SourcePort 查 perOutput），
    /// 同節點不同 output port 各自決定是否 Bad，cop1 Error 不會連帶蓋掉 cop2 Good。</summary>
    private bool HasUpstreamBad(DiagramContext ctx, int nNodeId)
    {
        var inEdges = ctx.Edges.Where(e => e.Target == nNodeId).ToList();
        foreach (var edge in inEdges)
        {
            var srcNode = ctx.Nodes.Find(n => n.Id == edge.Source);
            if (srcNode == null) continue;
            if (srcNode.Type == "input" && !string.IsNullOrEmpty(srcNode.Sid))
            {
                if (IsInputSourceBad(srcNode))
                    return true;
            }
            // 演算法節點：per-port severity 判斷（依消費邊線的 sourcePort 查 perOutput）
            if (srcNode.Type == "algorithm" && IsAlgoPortBad(srcNode, edge.SourcePort))
                return true;
            // 遞迴檢查
            if (srcNode.Type is "math" or "compare" or "and" or "or" or "not" or "xor" or "timer" or "contact_no" or "contact_nc" or "algorithm" or "counter")
            {
                if (HasUpstreamBad(ctx, srcNode.Id)) return true;
            }
        }
        return false;
    }

    /// <summary>判斷演算法節點的指定 output port 是否為 Bad（severity = Error）。
    /// 有 perOutput → 走 per-port；無（grace period 或舊資料）→ fallback 走 merged severity。</summary>
    private static bool IsAlgoPortBad(FlowNode srcNode, string szSourcePort)
    {
        if (srcNode.AlgoPerOutputStatus != null)
        {
            return srcNode.AlgoPerOutputStatus.TryGetValue(szSourcePort, out var portStatus)
                   && portStatus.Severity == "Error";
        }
        return srcNode.AlgoStatusSeverity == "Error";
    }

    private void ResetTpState(FlowNode nd)
    {
        nd.TpTimer?.Dispose();
        nd.TpTimer = null;
        nd.TpPhase = null;
        nd.TpPhaseEnd = DateTime.MinValue;
        nd.TpHasHeld = false;
        nd.TpPrevInputValue = null;
        nd.TprPrevInput = null;
        nd.TprLastSentValue = null;
        nd.Result = null;
    }

    private void ScheduleTpTimer(DiagramContext ctx, FlowNode nd, int nMilliseconds)
    {
        nd.TpTimer?.Dispose();
        nd.TpTimer = new Timer(async _ =>
        {
            if (!await _evalLock.WaitAsync(0)) return;
            try
            {
                EvaluateNodes(ctx);
                await ProcessOutputNodesAsync(ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LogicFlow TP Timer 回呼錯誤");
            }
            finally
            {
                _evalLock.Release();
            }
        }, null, nMilliseconds, Timeout.Infinite);
    }

    // ─── TP 狀態發布 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 安全地把 DateTime 轉成 Unix 毫秒；遇到 MinValue/MaxValue（含 Unspecified Kind 套用本地時區後溢位）
    /// 一律回傳 0，避免 DateTimeOffset(DateTime) 在台灣時區（+08:00）下將 MinValue 推到年份 0 之前而拋例外。
    /// </summary>
    private static long ToUnixMs(DateTime dt)
    {
        if (dt == DateTime.MinValue || dt == DateTime.MaxValue) return 0;
        var dtUtc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return new DateTimeOffset(dtUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>收集所有 TP 計時器狀態，僅在階段變化時才透過 MQTT 發布</summary>
    private async Task PublishTimerStatesAsync()
    {
        try
        {
            if (!_mqttPublishService.IsConnected) return;

            var timers = new Dictionary<string, object>();
            // 建立指紋：只用 phase 判斷變化（phaseEndMs 每次階段切換才會改變）
            var sbFingerprint = new System.Text.StringBuilder();

            foreach (var ctx in _diagrams.Values)
            {
                foreach (var nd in ctx.Nodes)
                {
                    if (nd.Type != "timer") continue;
                    // TON
                    if (nd.Operator == "ton")
                    {
                        if (nd.TonPhase == null) continue;
                        var szKey = $"{ctx.TreeId}-{nd.Id}";
                        var nPhaseEndMs = ToUnixMs(nd.TonPhaseEnd);
                        timers[szKey] = new
                        {
                            phase = nd.TonPhase,
                            phaseEndMs = nPhaseEndMs,
                            hasHeld = false
                        };
                        sbFingerprint.Append(szKey).Append(':').Append(nd.TonPhase).Append(':').Append(nPhaseEndMs).Append(';');
                        continue;
                    }
                    // TP
                    if (nd.TpPhase == null) continue;
                    {
                        var szKey = $"{ctx.TreeId}-{nd.Id}";
                        var nPhaseEndMs = ToUnixMs(nd.TpPhaseEnd);
                        timers[szKey] = new
                        {
                            phase = nd.TpPhase,
                            phaseEndMs = nPhaseEndMs,
                            hasHeld = nd.TpHasHeld
                        };
                        sbFingerprint.Append(szKey).Append(':').Append(nd.TpPhase).Append(':').Append(nPhaseEndMs).Append(';');
                    }
                }
            }

            if (timers.Count == 0) return;

            // 指紋未變 → 跳過發布
            var szFingerprint = sbFingerprint.ToString();
            if (szFingerprint == _szLastPublishedTimerState) return;
            _szLastPublishedTimerState = szFingerprint;

            var szPayload = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                timers
            });

            await _mqttPublishService.PublishRawJsonAsync(TIMER_STATE_TOPIC, szPayload, isRetain: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布 TP 計時器狀態至 MQTT 失敗");
        }
    }

    // ─── Modbus 寫入（複製自 ConditionControlService 模式）─────────────────

    private async Task<bool> ExecuteControlWriteAsync(string szSid, double dValue)
    {
        // DB 來源 SID（DB{coordId}-S{n}）— 改寫 DBLatestData，不走 Modbus 路徑
        if (szSid.StartsWith("DB", StringComparison.Ordinal))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
                var isOk = await repo.UpdateDbLatestDataAsync(szSid, dValue);
                if (isOk)
                    _logger.LogInformation("[LogicFlow] DBLatestData 寫入成功: {SID} = {Value}", szSid, dValue);
                return isOk;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LogicFlow] DBLatestData 寫入失敗: SID={SID}", szSid);
                return false;
            }
        }

        try
        {
            var parts = szSid.Split('-');
            if (parts.Length != 2
                || !parts[1].StartsWith('S')
                || !int.TryParse(parts[0], out var nXXX)
                || !int.TryParse(parts[1][1..], out var nN))
            {
                _logger.LogError("[LogicFlow] 控制點位 SID 格式不合法: {SID}", szSid);
                return false;
            }

            var nTemp = nXXX - 1;
            var nDatabaseId = nTemp / 65536;
            var nModbusId = (nTemp % 65536) / 256;
            var nTagIndex = nN - 1;

            var deviceConfig = _deviceConfigs.FirstOrDefault(c => c.nDatabaseId == nDatabaseId);
            if (deviceConfig == null)
            {
                _logger.LogError("[LogicFlow] 找不到 DatabaseId={Id} 的設備配置", nDatabaseId);
                return false;
            }

            if (nTagIndex < 0 || nTagIndex >= deviceConfig.tagList.Count)
            {
                _logger.LogError("[LogicFlow] TagIndex={Idx} 超出範圍", nTagIndex);
                return false;
            }

            var tag = deviceConfig.tagList[nTagIndex];
            await ExecuteModbusWriteAsync(deviceConfig, tag, nModbusId, dValue);

            _logger.LogInformation("[LogicFlow] Modbus 寫入成功: {TagName} = {Value}", tag.szName, dValue);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LogicFlow] 執行控制寫入失敗: SID={SID}", szSid);
            return false;
        }
    }

    private async Task ExecuteModbusWriteAsync(
        ModbusDeviceConfigModel deviceConfig, ModbusTagModel tag, int nModbusId, double dValue)
    {
        using var client = new ModbusTcpClient();
        try
        {
            if (!tag.ParseAddress())
            {
                _logger.LogError("[LogicFlow] 無法解析 Modbus 地址: {Address}", tag.szAddress);
                return;
            }

            if (tag.nFunctionCode != 1 && tag.nFunctionCode != 3)
            {
                _logger.LogError("[LogicFlow] 點位不支援寫入 (FC={FC}): {Address}", tag.nFunctionCode, tag.szAddress);
                return;
            }

            var endpoint = new System.Net.IPEndPoint(
                System.Net.IPAddress.Parse(deviceConfig.szIP), deviceConfig.nPort);

            client.ConnectTimeout = deviceConfig.nConnectTimeout;
            client.ReadTimeout = deviceConfig.nConnectTimeout;
            client.WriteTimeout = deviceConfig.nConnectTimeout;
            client.Connect(endpoint);

            if (tag.nFunctionCode == 1)
            {
                client.WriteSingleCoil((byte)nModbusId, (ushort)tag.nParsedAddress, dValue > 0.5);
                await Task.Delay(50);
            }
            else
            {
                await WriteHoldingRegisterAsync(client, nModbusId, tag, dValue);
            }
        }
        finally
        {
            try { if (client.IsConnected) client.Disconnect(); } catch { }
        }
    }

    private async Task WriteHoldingRegisterAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag, double dValue)
    {
        if (!float.TryParse(tag.szRatio, out var fRatio)) fRatio = 1.0f;

        switch (tag.szDataType.ToUpper())
        {
            case "INTEGER":
            {
                var nRaw = (short)(dValue / fRatio);
                var swapped = (ushort)(((nRaw & 0xFF) << 8) | ((nRaw >> 8) & 0xFF));
                client.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swapped);
                break;
            }
            case "UINTEGER":
            {
                var uRaw = (ushort)Math.Max(0, dValue / fRatio);
                var swapped = (ushort)((uRaw << 8) | (uRaw >> 8));
                client.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swapped);
                break;
            }
            case "FLOATINGPT":
                await WriteFloatAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: false);
                break;
            case "SWAPPEDFP":
                await WriteFloatAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: true);
                break;
            case "DOUBLE":
                await WriteDoubleAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: false);
                break;
            case "SWAPPEDDOUBLE":
                await WriteDoubleAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: true);
                break;
            default:
                _logger.LogError("[LogicFlow] 不支援的資料型態: {DataType}", tag.szDataType);
                break;
        }
        await Task.Delay(50);
    }

    private async Task WriteFloatAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag,
        double dValue, float fRatio, bool isSwapped)
    {
        var bytes = BitConverter.GetBytes((float)(dValue / fRatio));
        var regs = new ushort[2];
        if (isSwapped)
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
        }
        else
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
        }
        client.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, regs);
        await Task.Delay(50);
    }

    private async Task WriteDoubleAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag,
        double dValue, float fRatio, bool isSwapped)
    {
        var bytes = BitConverter.GetBytes(dValue / fRatio);
        var regs = new ushort[4];
        if (isSwapped)
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 6));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 4));
            regs[2] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[3] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
        }
        else
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[2] = SwapBytes(BitConverter.ToUInt16(bytes, 4));
            regs[3] = SwapBytes(BitConverter.ToUInt16(bytes, 6));
        }
        client.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, regs);
        await Task.Delay(50);
    }

    private static ushort SwapBytes(ushort value) => (ushort)((value << 8) | (value >> 8));

    // ─── DiagramJson 解析 ────────────────────────────────────────────────────

    private DiagramContext? ParseDiagram(int nTreeId, string szName, string szJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(szJson);
            var root = doc.RootElement;

            var nodes = new List<FlowNode>();
            var edges = new List<FlowEdge>();

            if (root.TryGetProperty("nodes", out var nodesEl))
            {
                foreach (var n in nodesEl.EnumerateArray())
                {
                    nodes.Add(new FlowNode
                    {
                        Id = n.GetProperty("id").GetInt32(),
                        Type = n.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Sid = n.TryGetProperty("sid", out var s) ? s.GetString() : null,
                        Operator = n.TryGetProperty("operator", out var op) ? op.GetString() : null,
                        ConstValue = n.TryGetProperty("constValue", out var cv) ? cv.GetDouble() : null,
                        TimerDelay = n.TryGetProperty("timerDelay", out var td) ? td.GetDouble() : 5,
                        TimerHold = n.TryGetProperty("timerHold", out var th) ? th.GetDouble() : 2,
                        FMin = n.TryGetProperty("fMin", out var fmin) ? fmin.GetDouble() : 0,
                        FMax = n.TryGetProperty("fMax", out var fmax) ? fmax.GetDouble() : 100,
                        ScheduleId = n.TryGetProperty("scheduleId", out var schId) ? schId.GetInt32() : null,
                        AlgoInputs = n.TryGetProperty("algoInputs", out var ai)
                            ? ai.EnumerateArray().Select(x => x.GetString() ?? "in").ToList()
                            : null,
                        InputCount = n.TryGetProperty("inputCount", out var ic) && ic.ValueKind == JsonValueKind.Number
                            ? ic.GetInt32()
                            : null,
                        PresetValue = n.TryGetProperty("presetValue", out var pv) && pv.ValueKind == JsonValueKind.Number
                            ? pv.GetInt32()
                            : 10,
                        CuMinIntervalMs = n.TryGetProperty("cuMinIntervalMs", out var cmi) && cmi.ValueKind == JsonValueKind.Number
                            ? cmi.GetInt32()
                            : 60000,
                        HistEnabled = n.TryGetProperty("histEnabled", out var he) && he.ValueKind == JsonValueKind.True,
                        HistOffsetMinutes = n.TryGetProperty("histOffsetMinutes", out var hom) && hom.ValueKind == JsonValueKind.Number
                            ? hom.GetInt32()
                            : null,
                    });
                }
            }

            if (root.TryGetProperty("edges", out var edgesEl))
            {
                foreach (var e in edgesEl.EnumerateArray())
                {
                    edges.Add(new FlowEdge
                    {
                        Id = e.GetProperty("id").GetInt32(),
                        Source = e.GetProperty("source").GetInt32(),
                        Target = e.GetProperty("target").GetInt32(),
                        SourcePort = e.TryGetProperty("sourcePort", out var sp) ? sp.GetString() ?? "out" : "out",
                        TargetPort = e.TryGetProperty("targetPort", out var tp) ? tp.GetString() ?? "in" : "in",
                    });
                }
            }

            return new DiagramContext
            {
                TreeId = nTreeId,
                Name = szName,
                Nodes = nodes,
                Edges = edges,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 解析 DiagramJson 失敗: TreeId={TreeId}", nTreeId);
            return null;
        }
    }

    // ─── 內部資料模型 ────────────────────────────────────────────────────────

    private class DiagramContext
    {
        public int TreeId { get; init; }
        public string Name { get; init; } = "";
        public string RawJson { get; set; } = "";
        public List<FlowNode> Nodes { get; init; } = new();
        public List<FlowEdge> Edges { get; init; } = new();
        public Dictionary<int, (bool isGreen, double? dValue)> OutputPrevState { get; } = new();
        public Dictionary<int, OutputRetryState> RetryStates { get; } = new();
        /// <summary>每個 Output 節點最後一次成功寫入的時間（用於健康檢查）</summary>
        public Dictionary<int, DateTime> OutputLastWriteOk { get; } = new();
    }

    private class OutputRetryState
    {
        public int nFailCount { get; set; }
        public DateTime dtLastRetry { get; set; } = DateTime.MinValue;
        public bool isAlarmRaised { get; set; }
    }

    private static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HEALTH_CHECK_INTERVAL = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan STARTUP_GRACE_PERIOD = TimeSpan.FromSeconds(30);
    private const int MAX_RETRY_BEFORE_ALARM = 10;

    private class FlowNode
    {
        public int Id { get; init; }
        public string Type { get; init; } = "";
        public string? Sid { get; init; }
        public string? Operator { get; init; }
        public double? ConstValue { get; init; }
        public double TimerDelay { get; init; } = 5;
        public double TimerHold { get; init; } = 2;
        public double FMin { get; init; } = 0;
        public double FMax { get; init; } = 100;
        public int? ScheduleId { get; init; }
        public List<string>? AlgoInputs { get; init; }
        /// <summary>variadic 演算法的批次數 N（非 variadic 為 null）</summary>
        public int? InputCount { get; init; }

        // counter（CTU）設定
        public int PresetValue { get; init; } = 10;
        public int CuMinIntervalMs { get; init; } = 60000;

        // 歷史值讀取（input / contact 點位模式）：讀「N 分鐘前」HistoryData 值；不啟用時行為與即時值完全相同
        public bool HistEnabled { get; init; }
        public int? HistOffsetMinutes { get; init; }

        // 運行時狀態
        public double? Result { get; set; }
        /// <summary>多輸出演算法結果（key 對應 edge.SourcePort）；單輸出時也會放入 {"out": value}</summary>
        public Dictionary<string, double>? AlgoResultDict { get; set; }
        /// <summary>演算法狀態（merged，所有 output 取嚴重度最高者）：codeId（0=OK）</summary>
        public int AlgoStatusCodeId { get; set; }
        /// <summary>演算法狀態（merged）：codeName（OK / DIVIDE_BY_ZERO / ...）</summary>
        public string AlgoStatusCodeName { get; set; } = "OK";
        /// <summary>演算法狀態（merged）：severity（Info / Warning / Error）</summary>
        public string AlgoStatusSeverity { get; set; } = "Info";
        /// <summary>每個輸出 port (含 variadic suffix) 的 status；HasUpstreamBad / EventLog 走此 map per-port 判斷</summary>
        public Dictionary<string, (int CodeId, string CodeName, string Severity)>? AlgoPerOutputStatus { get; set; }
        public bool IsDone { get; set; }

        // TP 脈衝狀態機
        public string? TpPhase { get; set; }
        public DateTime TpPhaseEnd { get; set; } = DateTime.MinValue;
        public bool TpHasHeld { get; set; }
        public Timer? TpTimer { get; set; }
        public double TpLastPassValue { get; set; } = 1;  // 上次注入值（上游暫時無值時 keep）
        public double? TpPrevInputValue { get; set; }      // 質變偵測：上次輸入值

        // TON 延時開啟狀態機
        public string? TonPhase { get; set; }
        public DateTime TonPhaseEnd { get; set; } = DateTime.MinValue;

        // TPR 重複脈衝：回饋用（運行時快取下游 output SID）
        public string? TprFeedbackSid { get; set; }
        // TPR 值變偵測：上次注入值（用於 delay 倒數中的 debounce 與 confirmed 內值變判斷）
        public double? TprPrevInput { get; set; }
        // TPR 已輸出值：confirmed 內若 passValue ≠ 此值即視為「輸入值變」，回 delay
        public double? TprLastSentValue { get; set; }

        // counter 運行時狀態（不持久化，Engine 重啟歸零）
        public int CounterValue { get; set; }
        public double? CounterPrevCu { get; set; }
        public DateTime CounterLastEdgeAt { get; set; } = DateTime.MinValue;
    }

    private class FlowEdge
    {
        public int Id { get; init; }
        public int Source { get; init; }
        public int Target { get; init; }
        public string SourcePort { get; init; } = "out";
        public string TargetPort { get; init; } = "in";
    }

    /// <summary>演算法輸出快取項：含 result dict + merged status + per-output status</summary>
    private record AlgoCachedOutput(
        Dictionary<string, double> Result,
        int StatusCodeId,
        string StatusCodeName,
        string Severity,
        Dictionary<string, (int CodeId, string CodeName, string Severity)> PerOutput);

    /// <summary>演算法 status 快照（用於變化偵測，決定是否寫 EventLog）</summary>
    private record AlgoStatusSnapshot(int CodeId, string CodeName, string Severity);
}
