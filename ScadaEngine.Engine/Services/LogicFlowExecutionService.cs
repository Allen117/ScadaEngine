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

    // 快取
    private readonly ConcurrentDictionary<int, DiagramContext> _diagrams = new();
    private Dictionary<string, (double dValue, int nQuality)> _latestCache = new();
    private Dictionary<string, (double dValue, bool isAuto)> _manualControlCache = new();
    private List<ModbusDeviceConfigModel> _deviceConfigs = new();
    private Dictionary<int, ScheduleRecord> _scheduleCache = new();

    // 重載計時
    private DateTime _dtLastDiagramReload = DateTime.MinValue;
    private DateTime _dtLastDeviceConfigReload = DateTime.MinValue;
    private DateTime _dtLastManualControlReload = DateTime.MinValue;
    private DateTime _dtLastScheduleReload = DateTime.MinValue;

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
    // 演算法結果快取：key = "{nodeId}-{inputHash}", value = result
    private readonly ConcurrentDictionary<string, double> _algoResultCache = new();

    private static readonly TimeSpan DIAGRAM_RELOAD_INTERVAL = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DEVICE_CONFIG_RELOAD_INTERVAL = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MANUAL_CONTROL_RELOAD_INTERVAL = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SCHEDULE_RELOAD_INTERVAL = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EVAL_INTERVAL = TimeSpan.FromMilliseconds(200);

    private const string TIMER_STATE_TOPIC = "SCADA/LogicFlow/TimerState";

    public LogicFlowExecutionService(
        ILogger<LogicFlowExecutionService> logger,
        IServiceProvider serviceProvider,
        ModbusConfigService modbusConfigService,
        LogicFlowRepository logicFlowRepository,
        RealtimeDataStorageService realtimeDataService,
        MqttPublishService mqttPublishService,
        CSharpAlgorithmService csharpAlgoService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _modbusConfigService = modbusConfigService;
        _logicFlowRepository = logicFlowRepository;
        _realtimeDataService = realtimeDataService;
        _mqttPublishService = mqttPublishService;
        _csharpAlgoService = csharpAlgoService;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogicFlow 刷新快取失敗");
        }
    }

    /// <summary>多輪迭代求值所有節點</summary>
    private void EvaluateNodes(DiagramContext ctx)
    {
        var evalTypes = new HashSet<string> { "math", "compare", "and", "or", "not", "xor", "timer", "contact_no", "contact_nc", "algorithm" };
        var evalNodes = ctx.Nodes.Where(n => evalTypes.Contains(n.Type)).ToList();

        // 清除上一輪結果（保留 TP 狀態機屬性）
        foreach (var nd in evalNodes)
        {
            nd.Result = null;
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
            default: return false;
        }
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

        // TP 脈衝：狀態機驅動
        var inEdge = ctx.Edges.Find(e => e.Target == nd.Id && e.TargetPort == "in");
        double dPassValue = 1;
        if (inEdge != null)
        {
            if (HasUpstreamBad(ctx, nd.Id))
            {
                ResetTpState(nd);
                nd.IsDone = true;
                return true;
            }
            var v = GetNodeOutputValue(ctx, inEdge.Source);
            if (!v.HasValue) return false;
            dPassValue = v.Value;
        }

        double dEffDelay = GetInputValue(ctx, nd.Id, "delay") ?? nd.TimerDelay;
        double dEffHold = GetInputValue(ctx, nd.Id, "hold") ?? nd.TimerHold;
        int nDelayMs = Math.Max((int)(dEffDelay * 1000), 500);
        int nHoldMs = Math.Max((int)(dEffHold * 1000), 500);
        var now = DateTime.Now;

        // 狀態機初始化
        if (nd.TpPhase == null)
        {
            nd.TpPhase = "delay";
            nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
            nd.TpHasHeld = false;
            ScheduleTpTimer(ctx, nd, nDelayMs);
        }

        // 階段轉換
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
                nd.TpPhase = "delay";
                nd.TpPhaseEnd = now.AddMilliseconds(nDelayMs);
                ScheduleTpTimer(ctx, nd, nDelayMs);
            }
        }

        // 輸出值
        nd.Result = nd.TpPhase == "hold" ? dPassValue : (nd.TpHasHeld ? 0 : null);
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
            var v = GetNodeOutputValue(ctx, inEdge.Source);
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

    private bool EvalContact(DiagramContext ctx, FlowNode nd)
    {
        if (HasUpstreamBad(ctx, nd.Id)) return false;

        // ── 排程模式 ──
        if (nd.ScheduleId.HasValue)
        {
            if (!_scheduleCache.TryGetValue(nd.ScheduleId.Value, out var sch))
                return false;

            bool isActive = EvalScheduleIsActive(sch);
            bool isOn = nd.Type == "contact_no" ? isActive : !isActive;

            var inVal = GetInputValue(ctx, nd.Id, "in");
            nd.Result = isOn ? (inVal ?? 1) : (inVal.HasValue ? null : 0);  // 有左側注入值時，未導通不送值
            nd.IsDone = true;
            return true;
        }

        // ── 點位模式 ──
        if (string.IsNullOrEmpty(nd.Sid)) return false;

        if (!_latestCache.TryGetValue(nd.Sid, out var lv)) return false;
        if (lv.nQuality != 1) return false;

        bool isOnPt = nd.Type == "contact_no" ? (lv.dValue == 1) : (lv.dValue == 0);

        var inValPt = GetInputValue(ctx, nd.Id, "in");

        if (isOnPt)
            nd.Result = inValPt.HasValue ? inValPt.Value : 1;
        else
            nd.Result = inValPt.HasValue ? null : 0;  // 有左側注入值時，未導通不送值
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

        // 收集所有輸入 port 的值
        var algoInputs = nd.AlgoInputs ?? new List<string> { "in" };
        var inputDict = new Dictionary<string, double>();
        foreach (var portName in algoInputs)
        {
            var v = GetInputValue(ctx, nd.Id, portName);
            if (!v.HasValue) return false;  // 任一輸入尚未就緒
            inputDict[portName] = v.Value;
        }

        // ★ 優先嘗試 C# 演算法（同步 in-process，零延遲）
        if (_csharpAlgoService.TryEvaluate(nd.Operator, inputDict, out var csResult))
        {
            if (csResult.TryGetValue("out", out var dOut))
            {
                nd.Result = dOut;
                nd.IsDone = true;
                return true;
            }
            if (csResult.Count > 0)
            {
                nd.Result = csResult.Values.First();
                nd.IsDone = true;
                return true;
            }
        }

        // ★ 退回 Python HTTP（原有邏輯）
        // 快取 key：以輸入值的 hash 判斷是否需要重新呼叫
        var szInputHash = string.Join("|", inputDict.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value:G}"));
        var szCacheKey = $"{ctx.TreeId}-{nd.Id}-{szInputHash}";

        if (_algoResultCache.TryGetValue($"{ctx.TreeId}-{nd.Id}", out _))
        {
            // 檢查輸入是否變化
            if (_algoResultCache.TryGetValue(szCacheKey, out var cachedResult))
            {
                nd.Result = cachedResult;
                nd.IsDone = true;
                return true;
            }
        }

        // 非同步呼叫 Python — 使用 fire-and-forget + cache 寫回
        // （EvalOneNode 是同步的，無法 await，因此先回傳上次結果，背景更新）
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { inputs = inputDict });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _algoHttpClient.PostAsync($"/algorithms/{nd.Operator}/evaluate", content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                        resultEl.TryGetProperty("out", out var outEl))
                    {
                        var dResult = outEl.GetDouble();
                        // 清除舊的快取項，寫入新的
                        foreach (var key in _algoResultCache.Keys.Where(k => k.StartsWith($"{ctx.TreeId}-{nd.Id}-")).ToList())
                            _algoResultCache.TryRemove(key, out _);
                        _algoResultCache[szCacheKey] = dResult;
                        _algoResultCache[$"{ctx.TreeId}-{nd.Id}"] = dResult;  // 最新值快取
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "呼叫 Python 演算法 {Algo} 失敗", nd.Operator);
            }
        });

        // 第一次呼叫尚無快取：嘗試用最新值快取
        if (_algoResultCache.TryGetValue($"{ctx.TreeId}-{nd.Id}", out var lastResult))
        {
            nd.Result = lastResult;
            nd.IsDone = true;
            return true;
        }

        return false;  // 尚無任何結果
    }

    /// <summary>評估排程是否在有效時段內</summary>
    private static bool EvalScheduleIsActive(ScheduleRecord sch)
    {
        var now = DateTime.Now;

        // 日期條件
        bool dayMatch = sch.RecurrenceType switch
        {
            0 => CheckDaysOfWeek(now, sch.DaysOfWeek),
            1 => CheckWeekCycle(now, sch) && CheckDaysOfWeek(now, sch.DaysOfWeek),
            2 => CheckDaysOfMonth(now, sch.DaysOfMonth),
            3 => CheckMonthCycle(now, sch) && CheckDaysOfMonth(now, sch.DaysOfMonth),
            _ => false
        };
        if (!dayMatch) return false;

        // 時間條件（支援跨日）
        return CheckTimeWindow(now, sch.StartTime, sch.EndTime);
    }

    private static bool CheckDaysOfWeek(DateTime now, string? daysStr)
    {
        if (string.IsNullOrEmpty(daysStr)) return false;
        int isoDay = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek; // 1=Mon..7=Sun
        return daysStr.Split(',').Any(d => int.TryParse(d.Trim(), out var v) && v == isoDay);
    }

    private static bool CheckDaysOfMonth(DateTime now, string? daysStr)
    {
        if (string.IsNullOrEmpty(daysStr)) return false;
        int dom = now.Day;
        return daysStr.Split(',').Any(d => int.TryParse(d.Trim(), out var v) && v == dom);
    }

    private static bool CheckTimeWindow(DateTime now, string startStr, string endStr)
    {
        if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr)) return false;
        int nowMin = now.Hour * 60 + now.Minute;
        var sp = startStr.Split(':'); var ep = endStr.Split(':');
        if (sp.Length < 2 || ep.Length < 2) return false;
        int startMin = int.Parse(sp[0]) * 60 + int.Parse(sp[1]);
        int endMin = int.Parse(ep[0]) * 60 + int.Parse(ep[1]);
        if (endMin <= startMin)
            return nowMin >= startMin || nowMin < endMin; // 跨日
        return nowMin >= startMin && nowMin < endMin;
    }

    private static bool CheckWeekCycle(DateTime now, ScheduleRecord sch)
    {
        if (!sch.AnchorDateTime.HasValue || !sch.RunLength.HasValue || !sch.RestLength.HasValue) return false;
        var elapsed = now - sch.AnchorDateTime.Value;
        if (elapsed.TotalMilliseconds < 0) return false;
        int totalCycle = sch.RunLength.Value + sch.RestLength.Value;
        int elapsedWeeks = (int)(elapsed.TotalDays / 7);
        return (elapsedWeeks % totalCycle) < sch.RunLength.Value;
    }

    private static bool CheckMonthCycle(DateTime now, ScheduleRecord sch)
    {
        if (!sch.AnchorDateTime.HasValue || !sch.RunLength.HasValue || !sch.RestLength.HasValue) return false;
        var anchor = sch.AnchorDateTime.Value;
        int totalMonths = (now.Year - anchor.Year) * 12 + (now.Month - anchor.Month);
        if (totalMonths < 0) return false;
        int totalCycle = sch.RunLength.Value + sch.RestLength.Value;
        return (totalMonths % totalCycle) < sch.RunLength.Value;
    }

    // ─── 輔助方法 ────────────────────────────────────────────────────────────

    private double? GetNodeOutputValue(DiagramContext ctx, int nNodeId)
    {
        var nd = ctx.Nodes.Find(n => n.Id == nNodeId);
        if (nd == null) return null;

        if (nd.Type == "input" && !string.IsNullOrEmpty(nd.Sid))
        {
            if (!_latestCache.TryGetValue(nd.Sid, out var lv)) return null;
            if (lv.nQuality != 1) return null; // Bad quality
            return lv.dValue;
        }
        if (nd.Type == "constant") return nd.ConstValue ?? 0;
        if (nd.Type is "math" or "compare" or "and" or "or" or "not" or "xor" or "timer" or "contact_no" or "contact_nc" or "algorithm")
            return nd.Result;

        return null;
    }

    private double? GetInputValue(DiagramContext ctx, int nNodeId, string szPortName)
    {
        var edge = ctx.Edges.Find(e => e.Target == nNodeId && e.TargetPort == szPortName);
        if (edge == null) return null;
        return GetNodeOutputValue(ctx, edge.Source);
    }

    private bool HasUpstreamBad(DiagramContext ctx, int nNodeId)
    {
        var inEdges = ctx.Edges.Where(e => e.Target == nNodeId).ToList();
        foreach (var edge in inEdges)
        {
            var srcNode = ctx.Nodes.Find(n => n.Id == edge.Source);
            if (srcNode == null) continue;
            if (srcNode.Type == "input" && !string.IsNullOrEmpty(srcNode.Sid))
            {
                if (!_latestCache.TryGetValue(srcNode.Sid, out var lv) || lv.nQuality != 1)
                    return true;
            }
            // 遞迴檢查
            if (srcNode.Type is "math" or "compare" or "and" or "or" or "not" or "xor" or "timer" or "contact_no" or "contact_nc" or "algorithm")
            {
                if (HasUpstreamBad(ctx, srcNode.Id)) return true;
            }
        }
        return false;
    }

    private void ResetTpState(FlowNode nd)
    {
        nd.TpTimer?.Dispose();
        nd.TpTimer = null;
        nd.TpPhase = null;
        nd.TpPhaseEnd = DateTime.MinValue;
        nd.TpHasHeld = false;
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
                        var nPhaseEndMs = new DateTimeOffset(nd.TonPhaseEnd).ToUnixTimeMilliseconds();
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
                        var nPhaseEndMs = new DateTimeOffset(nd.TpPhaseEnd).ToUnixTimeMilliseconds();
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

        // 運行時狀態
        public double? Result { get; set; }
        public bool IsDone { get; set; }

        // TP 脈衝狀態機
        public string? TpPhase { get; set; }
        public DateTime TpPhaseEnd { get; set; } = DateTime.MinValue;
        public bool TpHasHeld { get; set; }
        public Timer? TpTimer { get; set; }

        // TON 延時開啟狀態機
        public string? TonPhase { get; set; }
        public DateTime TonPhaseEnd { get; set; } = DateTime.MinValue;
    }

    private class FlowEdge
    {
        public int Id { get; init; }
        public int Source { get; init; }
        public int Target { get; init; }
        public string SourcePort { get; init; } = "out";
        public string TargetPort { get; init; } = "in";
    }
}
