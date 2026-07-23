using ScadaEngine.Web.Features.ScadaPage.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// ScadaPage 迴路指標元件計算核心（Scoped）。四指標與 EMS 完全同源（plan 決策 1 / 5）：
///   day_kwh    — 本日度數（曆日 [今日 00:00, 明日)）→ EnergyReportService.GetBucketKwhForRangesAsync
///   month_kwh  — 本月度數（曆月 [1 號 00:00, 次月)）→ 同上
///   period_kwh — 本月電度（月結期別）→ ElectricityCostService.GetStatusAsync().totalKwh（EMS 電費卡同一支）
///   period_cost— 本月電費（月結期別）→ ElectricityCostService.GetStatusAsync().totalCost（零重算，逐字同 EMS）
/// 結果進 WidgetCircuitMetricCache（TTL 60s + per-key 鎖防 stampede）。
/// </summary>
public class WidgetCircuitMetricService
{
    public static readonly HashSet<string> ValidMetrics = new() { "day_kwh", "month_kwh", "period_kwh", "period_cost" };

    private readonly ILogger<WidgetCircuitMetricService> _logger;
    private readonly EnergyCircuitService _circuitService;
    private readonly EnergyReportService _reportService;
    private readonly ElectricityCostService _costService;
    private readonly WidgetCircuitMetricCache _cache;
    private readonly int _nResultCacheSeconds;

    public WidgetCircuitMetricService(
        ILogger<WidgetCircuitMetricService> logger,
        EnergyCircuitService circuitService,
        EnergyReportService reportService,
        ElectricityCostService costService,
        WidgetCircuitMetricCache cache,
        IConfiguration configuration)
    {
        _logger = logger;
        _circuitService = circuitService;
        _reportService = reportService;
        _costService = costService;
        _cache = cache;
        _nResultCacheSeconds = configuration.GetValue<int?>("ScadaPageCircuitMetric:ResultCacheSeconds") ?? 60;
    }

    /// <summary>批次計算。dtNow 由呼叫端傳入（正常為 DateTime.Now），便於測試跨日/跨月換期。</summary>
    public async Task<List<CircuitMetricResultDto>> ComputeAsync(List<CircuitMetricQueryItem> items, DateTime dtNow)
    {
        var results = new List<CircuitMetricResultDto>();
        foreach (var item in items)
        {
            var szKey = WidgetCircuitMetricCache.ResultKey(item);
            if (_cache.TryGetResult(szKey, dtNow, out var cached))
            {
                results.Add(cached);
                continue;
            }

            var semaphore = _cache.GetLock(szKey);
            await semaphore.WaitAsync();
            try
            {
                // 拿到鎖後重查 — 其他請求可能剛算完
                if (_cache.TryGetResult(szKey, dtNow, out cached))
                {
                    results.Add(cached);
                    continue;
                }

                var dto = await ComputeOneAsync(item, dtNow);
                _cache.SetResult(szKey, dto, dtNow.AddSeconds(_nResultCacheSeconds));
                results.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "迴路指標計算失敗 CircuitId={CircuitId} Metric={Metric}", item.nCircuitId, item.szMetric);
                results.Add(new CircuitMetricResultDto
                {
                    nCircuitId = item.nCircuitId,
                    szMetric = item.szMetric,
                    szStatus = "no_data",
                    dtCalcTime = dtNow,
                });
            }
            finally
            {
                semaphore.Release();
            }
        }
        return results;
    }

    private async Task<CircuitMetricResultDto> ComputeOneAsync(CircuitMetricQueryItem item, DateTime dtNow)
    {
        var dto = new CircuitMetricResultDto
        {
            nCircuitId = item.nCircuitId,
            szMetric = item.szMetric,
            dtCalcTime = dtNow,
        };

        // 迴路被刪除 → no_data（不可讓 GetStatusAsync 的「查無 fallback 回根迴路」誤套主表數字）
        var circuit = await _circuitService.GetByIdAsync(item.nCircuitId);
        if (circuit == null)
        {
            dto.szStatus = "no_data";
            return dto;
        }

        switch (item.szMetric)
        {
            case "day_kwh":
                await ComputeRangeKwhAsync(dto, item.nCircuitId, dtNow.Date, dtNow.Date.AddDays(1));
                break;
            case "month_kwh":
                var dtMonthStart = new DateTime(dtNow.Year, dtNow.Month, 1);
                await ComputeRangeKwhAsync(dto, item.nCircuitId, dtMonthStart, dtMonthStart.AddMonths(1));
                break;
            case "period_kwh":
            case "period_cost":
                await ComputePeriodMetricAsync(dto, item);
                break;
        }
        return dto;
    }

    /// <summary>曆日/曆月 kWh — 與 EnergyReport 同計算核心（遞迴葉子 × EffectiveSign、staleness、溢位；當期未過完自動夾到現在）</summary>
    private async Task ComputeRangeKwhAsync(CircuitMetricResultDto dto, int nCircuitId, DateTime dtStart, DateTime dtEnd)
    {
        // 虛擬迴路下無任何綁 SID 葉子 → 無資料可算
        var leaves = await _circuitService.GetLeavesUnderAsync(nCircuitId);
        if (leaves.Count == 0)
        {
            dto.szStatus = "no_data";
            return;
        }

        var (bucketSums, staleFlags) = await _reportService.GetBucketKwhForRangesAsync(
            nCircuitId, new List<(DateTime, DateTime)> { (dtStart, dtEnd) });
        dto.dValue = Math.Round(bucketSums[0], 2);
        dto.szStatus = staleFlags[0] ? "stale" : "ok";
        dto.szUnit = "kWh";
    }

    /// <summary>期別電度/電費 — 直接取 EMS 電費狀態卡結果（決策 5：零重算，數字逐字相同）</summary>
    private async Task ComputePeriodMetricAsync(CircuitMetricResultDto dto, CircuitMetricQueryItem item)
    {
        var status = await _costService.GetStatusAsync(item.nCircuitId);
        if (!status.hasPlan)
        {
            dto.szStatus = "no_plan";
            return;
        }
        if (!status.hasCircuit)
        {
            dto.szStatus = "no_data";
            return;
        }

        if (item.szMetric == "period_kwh")
        {
            dto.dValue = status.totalKwh;
            dto.szUnit = "kWh";
        }
        else
        {
            dto.dValue = status.totalCost ?? 0;
            dto.isEstimated = status.isEstimated;
        }
        dto.szStatus = "ok";
    }
}
