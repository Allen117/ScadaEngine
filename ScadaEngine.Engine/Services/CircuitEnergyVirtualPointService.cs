using Microsoft.Extensions.DependencyInjection;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 迴路用電虛擬點位背景服務。
/// 每 5 分鐘對齊整 5 分鐘計算每個 EnergyCircuit 迴路（含虛擬節點）的
/// 每日用電（[今日 00:00, now]）、每月用電（[本月 1 日 00:00, now]，曆月）與
/// 本月電度（[當前期別起日 00:00, now]，電費期別 — Common BillingPeriodResolver 推導）累積 kWh，
/// 以虛擬點位 SID（NRGD- / NRGM- / NRGP-{circuitId}）比照需量（DMD-）三路發布：
/// RealtimeDataStorageService（記憶體＋LatestData）→ 條件控制/LogicFlow、
/// HistoryDataStorageService（HistoryData）→ 歷史查詢、
/// MqttPublishService → 警報引擎/Web 即時。
///
/// 設計重點（plan 決策 3/4）：
/// - 算法：每葉子取期初邊界值與最新值相減（HistoryData、staleness 視窗、MaxKwh 溢位），
///   套有效 sign 加總 — 語意同 Web WidgetAccumulationService meter 模式 / Designer day_kwh、month_kwh
/// - 樹展開：一次撈全樹在記憶體展開（葉子 = SID 非空節點，有效 sign = 路徑 Sign 乘積、不含查詢根自身）
/// - Quality 忠實透傳：任一葉子邊界缺值 → 整迴路發 值=0/Quality=Bad（寧缺勿假，警報不誤觸發）
/// - 「月」= 曆月，非電費期別；迴路清單/名稱每 tick 重讀（改名/增刪迴路自動跟上）
/// </summary>
public class CircuitEnergyVirtualPointService : BackgroundService
{
    /// <summary>每日用電虛擬點位 SID 前綴（NRGD-{circuitId}，可逆推迴路）</summary>
    public const string NRG_DAILY_SID_PREFIX = "NRGD-";
    /// <summary>每月用電虛擬點位 SID 前綴（NRGM-{circuitId}，曆月）</summary>
    public const string NRG_MONTHLY_SID_PREFIX = "NRGM-";
    /// <summary>本月電度虛擬點位 SID 前綴（NRGP-{circuitId}，電費期別制）</summary>
    public const string NRG_PERIOD_SID_PREFIX = "NRGP-";

    /// <summary>迴路用電虛擬點位指標（決定 SID 前綴 / 點名後綴 / 期初邊界）</summary>
    public enum NrgMetric
    {
        /// <summary>每日用電（曆日，今日 00:00 起）</summary>
        Daily,
        /// <summary>每月用電（曆月，本月 1 日 00:00 起）</summary>
        Monthly,
        /// <summary>本月電度（電費期別，當前期別起日 00:00 起）</summary>
        Period
    }

    private const int INTERVAL_MINUTES = 5;

    private readonly ILogger<CircuitEnergyVirtualPointService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RealtimeDataStorageService _realtimeDataStorageService;
    private readonly HistoryDataStorageService _historyDataStorageService;
    private readonly MqttPublishService _mqttPublishService;
    private readonly int _nMaxStalenessHours;

    public CircuitEnergyVirtualPointService(
        ILogger<CircuitEnergyVirtualPointService> logger,
        IServiceProvider serviceProvider,
        RealtimeDataStorageService realtimeDataStorageService,
        HistoryDataStorageService historyDataStorageService,
        MqttPublishService mqttPublishService,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _realtimeDataStorageService = realtimeDataStorageService;
        _historyDataStorageService = historyDataStorageService;
        _mqttPublishService = mqttPublishService;
        // 與電表葉子層 hourly 聚合共用同一 staleness 設定（同電力領域、同語意）
        _nMaxStalenessHours = configuration.GetValue<int?>("EnergyAggregation:MaxStalenessHours") ?? 2;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("迴路用電虛擬點位服務啟動（每 {Interval} 分鐘）", INTERVAL_MINUTES);

        await WaitForDatabaseAsync(stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        // 對齊到下一個整 5 分鐘再開始（照 DemandCalculatorService 的整分對齊模式）
        var dtNow = DateTime.Now;
        var dtTruncated = new DateTime(dtNow.Year, dtNow.Month, dtNow.Day, dtNow.Hour, dtNow.Minute, 0);
        var dtTick = dtTruncated.AddMinutes(INTERVAL_MINUTES - dtTruncated.Minute % INTERVAL_MINUTES);
        var nDelayMs = (int)Math.Ceiling((dtTick - dtNow).TotalMilliseconds);
        _logger.LogInformation("迴路用電虛擬點位服務對齊整 5 分鐘，等待 {DelayMs} ms", nDelayMs);

        try { await Task.Delay(nDelayMs, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (DateTime.Now < dtTick && !stoppingToken.IsCancellationRequested)
            await Task.Delay(1, stoppingToken);

        // dtTick 每次固定 +INTERVAL，不從 DateTime.Now 重新截斷（計算慢時不跳 tick）
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CalculateAllAsync(dtTick, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "迴路用電虛擬點位主迴圈發生錯誤");
            }

            dtTick = dtTick.AddMinutes(INTERVAL_MINUTES);
            var nWaitMs = (int)Math.Max(0, Math.Ceiling((dtTick - DateTime.Now).TotalMilliseconds));
            try { await Task.Delay(nWaitMs, stoppingToken); }
            catch (OperationCanceledException) { break; }

            while (DateTime.Now < dtTick && !stoppingToken.IsCancellationRequested)
                await Task.Delay(1, stoppingToken);
        }

        _logger.LogInformation("迴路用電虛擬點位服務已停止");
    }

    private async Task CalculateAllAsync(DateTime dtTick, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();

        // 每 tick 重讀全樹：迴路增刪改名自動跟上（plan 風險段「點名快取隨週期刷新」）
        var nodes = (await repo.GetAllEnergyCircuitNodesAsync()).ToList();
        if (nodes.Count == 0) return;

        var leavesByCircuit = ExpandLeaves(nodes);
        var namesById = nodes.ToDictionary(n => n.nId, n => n.szName);

        // 當前電費期別起日（Common Resolver 推導，與 Web 報表/EMS 同一份邏輯；每 tick 重讀 rows，期別改動 5 分鐘內生效）
        var periodRows = (await repo.GetBillingPeriodRowsAsync())
            .ToDictionary(r => (r.nPeriodYear, r.nPeriodMonth));
        var dtPeriodStart = BillingPeriodResolver.ResolveCurrentPeriod(dtTick, periodRows).dtStart;

        // 每個唯一葉子 SID 一次查齊四個邊界值（日初/月初/期初/now），SID 被多迴路共用不重查
        var dtDayStart = dtTick.Date;
        var dtMonthStart = new DateTime(dtTick.Year, dtTick.Month, 1);
        var boundaries = new Dictionary<string, (double? dDayStart, double? dMonthStart, double? dPeriodStart, double? dNow)>();
        foreach (var szSid in leavesByCircuit.Values.SelectMany(l => l).Select(l => l.szSID).Distinct())
        {
            if (ct.IsCancellationRequested) return;
            boundaries[szSid] = await repo.GetEnergyBoundaryValuesAsync(
                szSid, dtDayStart, dtMonthStart, dtPeriodStart, dtTick, _nMaxStalenessHours);
        }

        var publishList = new List<RealtimeDataModel>();
        foreach (var (nCircuitId, leaves) in leavesByCircuit)
        {
            namesById.TryGetValue(nCircuitId, out var szName);

            foreach (var metric in new[] { NrgMetric.Daily, NrgMetric.Monthly, NrgMetric.Period })
            {
                var deltas = leaves.Select(l =>
                {
                    var b = boundaries[l.szSID];
                    var dStart = metric switch
                    {
                        NrgMetric.Daily => b.dDayStart,
                        NrgMetric.Monthly => b.dMonthStart,
                        _ => b.dPeriodStart
                    };
                    return new CircuitLeafDelta(dStart, b.dNow, l.dMaxKwh, l.nEffectiveSign);
                });
                var (dTotal, isGood) = SumLeafDeltas(deltas);
                publishList.Add(ToRealtimePoint(nCircuitId, szName, metric, dTotal, isGood, dtTick));
            }
        }

        if (publishList.Count == 0) return;

        // 三路發布（照抄 DemandCalculatorService 模式）
        _historyDataStorageService.AddRealtimeDataBatch(publishList);
        _realtimeDataStorageService.AddRealtimeDataBatch(publishList);

        _ = Task.Run(async () =>
        {
            foreach (var data in publishList)
            {
                try
                {
                    await _mqttPublishService.PublishRealtimeDataAsync(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "迴路用電虛擬點位 {SID} MQTT 發布失敗", data.szSID);
                }
            }
        }, CancellationToken.None);

        _logger.LogDebug("迴路用電虛擬點位計算完成: {Count} 個迴路 × 3 指標", leavesByCircuit.Count);
    }

    /// <summary>迴路葉子引用：綁定 SID + 溢位上限 + 有效 sign（路徑乘積）</summary>
    public readonly record struct CircuitLeafRef(string szSID, double? dMaxKwh, int nEffectiveSign);

    /// <summary>單葉子期間增量素材：期初/期末邊界值（null=staleness 內查無）+ 溢位上限 + 有效 sign</summary>
    public readonly record struct CircuitLeafDelta(double? dStart, double? dEnd, double? dMaxKwh, int nSign);

    /// <summary>
    /// 全樹展開：每個迴路 Id → 其子樹內所有綁定 SID 的節點（含自身），
    /// 有效 sign = 由該迴路往下的路徑 Sign 乘積、不含迴路自身 Sign
    /// （語意同 Web EnergyCircuitService.GetLeavesUnderAsync 的遞迴 CTE）。
    /// </summary>
    public static Dictionary<int, List<CircuitLeafRef>> ExpandLeaves(IReadOnlyList<EnergyCircuitNodeModel> nodes)
    {
        var childrenByParent = nodes
            .Where(n => n.nParentId.HasValue)
            .GroupBy(n => n.nParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, List<CircuitLeafRef>>();
        foreach (var root in nodes)
        {
            var leaves = new List<CircuitLeafRef>();
            Collect(root, 1, leaves);
            result[root.nId] = leaves;
        }
        return result;

        void Collect(EnergyCircuitNodeModel node, int nEffectiveSign, List<CircuitLeafRef> leaves)
        {
            if (!string.IsNullOrEmpty(node.szSID))
                leaves.Add(new CircuitLeafRef(node.szSID, node.dMaxKwh, nEffectiveSign));
            if (childrenByParent.TryGetValue(node.nId, out var children))
                foreach (var child in children)
                    Collect(child, nEffectiveSign * child.nSign, leaves);
        }
    }

    /// <summary>
    /// 溢位規則（語意同 Web WidgetAccumulationService / EnergyReportService.CalcDeltaWithRollover）：
    /// end >= start 正常差；end &lt; start 且 MaxKwh > 0 → (Max−start)+end；否則視為歸零/異常 → 0。
    /// 已知限制：整期只能偵測一次溢位。
    /// </summary>
    public static double CalcDeltaWithRollover(double dStart, double dEnd, double? dMaxKwh)
    {
        if (dEnd >= dStart)
            return dEnd - dStart;
        if (dMaxKwh.HasValue && dMaxKwh.Value > 0)
            return (dMaxKwh.Value - dStart) + dEnd;
        return 0;
    }

    /// <summary>
    /// 葉子增量套 sign 加總。任一葉子邊界缺值（含無葉子迴路）→ (0, Bad)，寧缺勿假：
    /// 少算的總量會造成假恢復/漏報，整迴路判 Bad 讓警報引擎既有 Bad 防呆生效。
    /// </summary>
    public static (double dTotal, bool isGood) SumLeafDeltas(IEnumerable<CircuitLeafDelta> leaves)
    {
        var dTotal = 0.0;
        var hasLeaf = false;
        foreach (var leaf in leaves)
        {
            hasLeaf = true;
            if (!leaf.dStart.HasValue || !leaf.dEnd.HasValue)
                return (0, false);
            dTotal += CalcDeltaWithRollover(leaf.dStart.Value, leaf.dEnd.Value, leaf.dMaxKwh) * leaf.nSign;
        }
        return hasLeaf ? (dTotal, true) : (0, false);
    }

    /// <summary>
    /// 迴路用電計算結果 → 虛擬點位 RealtimeDataModel 映射。
    /// SID = NRGD-（每日）/ NRGM-（每月，曆月）/ NRGP-（本月電度，期別）+ {circuitId}、
    /// 名稱 = {迴路名} 每日用電 / 每月用電 / 本月電度（無名稱時退「迴路{Id}」）、單位 kWh、
    /// Quality Good/Bad（Bad 時值為 0，不美化）。
    /// </summary>
    public static RealtimeDataModel ToRealtimePoint(
        int nCircuitId, string? szCircuitName, NrgMetric metric, double dValue, bool isGood, DateTime dtTimestamp)
    {
        var szBaseName = string.IsNullOrWhiteSpace(szCircuitName) ? $"迴路{nCircuitId}" : szCircuitName;
        var (szPrefix, szSuffix) = metric switch
        {
            NrgMetric.Daily => (NRG_DAILY_SID_PREFIX, "每日用電"),
            NrgMetric.Monthly => (NRG_MONTHLY_SID_PREFIX, "每月用電"),
            _ => (NRG_PERIOD_SID_PREFIX, "本月電度")
        };
        return new RealtimeDataModel
        {
            dtTimestamp       = dtTimestamp,
            szSID             = szPrefix + nCircuitId,
            szTagName         = $"{szBaseName} {szSuffix}",
            fValue            = isGood ? (float)dValue : 0f,
            szUnit            = "kWh",
            szQuality         = isGood ? "Good" : "Bad",
            szDeviceIP        = "CircuitEnergy",
            nAddress          = 0,
            szCoordinatorName = "迴路用電",
            IsReadSuccess     = true
        };
    }

    private async Task WaitForDatabaseAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
                if (await repo.TestConnectionAsync())
                {
                    _logger.LogInformation("迴路用電虛擬點位服務：DB 連線就緒");
                    return;
                }
            }
            catch { }

            _logger.LogWarning("迴路用電虛擬點位服務：等待 DB 連線...");
            try { await Task.Delay(5000, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
