using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 冷凍噸報表 — on-demand 計算。
/// 流程：依粒度產生 N 個 bucket → 從 WaterLeafHourly 一次撈該迴路下所有葉子在區間內的 hourly RT·h →
/// 每個 hour 落入對應 bucket 累加 → 多葉子加總 = 該 bucket 冷量。
///
/// 對標 <see cref="EnergyReportService"/>，差異：
///   - 葉子值取自 <c>WaterLeafHourly</c>（已預聚合），非 boundary 相減
///   - 階層加總無 sign（WaterCircuit 表無 Sign 欄位）
///   - 無 MaxKwh 溢位概念
/// </summary>
public class RefrigerationTonReportService
{
    private readonly ILogger<RefrigerationTonReportService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly WaterCircuitService _circuitService;
    private readonly BillingPeriodService _billingPeriodService;
    private string _szConnectionString = string.Empty;

    public RefrigerationTonReportService(
        ILogger<RefrigerationTonReportService> logger,
        DatabaseConfigService configService,
        WaterCircuitService circuitService,
        BillingPeriodService billingPeriodService)
    {
        _logger = logger;
        _configService = configService;
        _circuitService = circuitService;
        _billingPeriodService = billingPeriodService;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<RefrigerationTonReportResult> GetReportAsync(
        int nCircuitId, string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"水系統迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = await BuildBucketRangesAsync(szGranularity, dtStart, dtEnd);

        var result = new RefrigerationTonReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);

        FillBucketsAndTotal(result, ranges, labels, bucketSums);
        result.isHasWarning = bHasWarning;
        return result;
    }

    /// <summary>同 GetReportAsync，再額外展開「直接子迴路」每個的 bucket series — 給 Excel 匯出多欄使用。</summary>
    public async Task<RefrigerationTonReportResult> GetReportWithChildrenAsync(
        int nCircuitId, string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"水系統迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = await BuildBucketRangesAsync(szGranularity, dtStart, dtEnd);

        var result = new RefrigerationTonReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        FillBucketsAndTotal(result, ranges, labels, bucketSums);
        result.isHasWarning = bHasWarning;

        // 自己就是葉子 → 不展開子層
        if (!string.IsNullOrEmpty(circuit.szSID))
            return result;

        var children = await _circuitService.GetDirectChildrenAsync(nCircuitId);
        foreach (var child in children)
        {
            var (childSums, childWarning) = await ComputeBucketSumsForCircuitAsync(child.nId, ranges, conn);
            var series = new RefrigerationTonReportChildSeries
            {
                nCircuitId = child.nId,
                szName = child.szName,
            };
            double dTotal = 0;
            for (var i = 0; i < labels.Count; i++)
            {
                var dValue = childSums[i];
                series.dRtHourPerBucket.Add(Math.Round(dValue, 3));
                dTotal += dValue;
            }
            series.dTotalRtHour = Math.Round(dTotal, 3);
            if (childWarning) result.isHasWarning = true;
            result.children.Add(series);
        }

        return result;
    }

    /// <summary>
    /// 產生 N 個 bucket 的 [起, 訖) 邊界對與標籤 — 與 EnergyReportService 同構。
    /// 月粒度 = 期別（BillingPeriodService 解析，期別間可能空窗/重疊）；其餘粒度沿用連續邊界切法。
    /// </summary>
    private async Task<(List<(DateTime dtStart, DateTime dtEnd)> ranges, List<string> labels)>
        BuildBucketRangesAsync(string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        if (szGranularity == "month")
        {
            var periods = await _billingPeriodService.GetPeriodRangesAsync(dtStart, dtEnd);
            return (periods.Select(p => (p.dtStart, p.dtEndExclusive)).ToList(),
                    periods.Select(p => p.szLabel).ToList());
        }
        var boundaries = BuildBoundaries(szGranularity, dtStart, dtEnd);
        var ranges = new List<(DateTime, DateTime)>(boundaries.Count - 1);
        for (var i = 0; i < boundaries.Count - 1; i++)
            ranges.Add((boundaries[i], boundaries[i + 1]));
        return (ranges, BuildLabels(szGranularity, boundaries));
    }

    /// <summary>
    /// 能源申報專用 — 指定年度的 12 個「曆月」bucket（每月 1 號 00:00 ~ 次月 1 號 00:00），
    /// 不走月結期別（BillingPeriodService）。共用 ComputeBucketSumsForCircuitAsync 計算核心。
    /// </summary>
    public async Task<RefrigerationTonReportResult> GetCalendarMonthlyReportAsync(int nCircuitId, int nYear)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"水系統迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = EnergyReportService.BuildCalendarMonthRanges(nYear);

        var result = new RefrigerationTonReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = "month",
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        FillBucketsAndTotal(result, ranges, labels, bucketSums);
        result.isHasWarning = bHasWarning;
        return result;
    }

    /// <summary>
    /// 對單一迴路（葉子或虛擬皆可）計算每個 bucket 的 RT·h 累計。
    /// bucket 連續時（時/日/年與自然月期別）走 binary search；
    /// 期別不連續/重疊時逐 bucket 比對（重疊段依規格計入兩期、空窗段不計入任何期）。
    /// </summary>
    private async Task<(double[] bucketSums, bool isHasWarning)> ComputeBucketSumsForCircuitAsync(
        int nCircuitId, List<(DateTime dtStart, DateTime dtEnd)> ranges, SqlConnection conn)
    {
        var nBuckets = ranges.Count;
        var bucketSums = new double[nBuckets];
        var bHasWarning = false;

        var leaves = await _circuitService.GetLeavesUnderAsync(nCircuitId);
        if (leaves.Count == 0) return (bucketSums, bHasWarning);

        var dtRangeStart = ranges.Min(r => r.dtStart);
        var dtRangeEnd = ranges.Max(r => r.dtEnd);   // exclusive

        // 連續判定：全部 bucket 首尾相接 → 可用舊版 binary search 快路徑
        var isContiguous = true;
        for (var i = 0; i < nBuckets - 1; i++)
        {
            if (ranges[i].dtEnd != ranges[i + 1].dtStart) { isContiguous = false; break; }
        }
        List<DateTime>? boundaries = null;
        if (isContiguous)
        {
            boundaries = new List<DateTime>(nBuckets + 1);
            foreach (var r in ranges) boundaries.Add(r.dtStart);
            boundaries.Add(ranges[^1].dtEnd);
        }

        // 預期該迴路在區間內應有多少 hour（用來偵測「資料不全」警告）— 以各 bucket 長度加總。
        // 「當期未過完」：把每個 bucket 起訖夾到 min(t, 現在) 再算寬度，未來時數不計入分母，
        // 否則當期（如當月、當日）尚未到來的小時會拉低覆蓋率而誤觸「資料不完整」警告。
        var dtNow = DateTime.Now;
        var nExpectedHoursPerLeaf = (int)Math.Round(ranges.Sum(r =>
        {
            var dtCs = r.dtStart < dtNow ? r.dtStart : dtNow;
            var dtCe = r.dtEnd < dtNow ? r.dtEnd : dtNow;
            var dHours = (dtCe - dtCs).TotalHours;
            return dHours > 0 ? dHours : 0;
        }));

        foreach (var leaf in leaves)
        {
            if (string.IsNullOrEmpty(leaf.szSID)) continue;
            var rows = await GetLeafHourRowsAsync(conn, leaf.szSID!, dtRangeStart, dtRangeEnd);

            int nHoursGot = 0;
            foreach (var row in rows)
            {
                if (isContiguous)
                {
                    var nIdx = FindBucketIndex(boundaries!, row.dtHourStart);
                    if (nIdx < 0 || nIdx >= nBuckets) continue;
                    bucketSums[nIdx] += row.dRtHour;
                    nHoursGot++;
                }
                else
                {
                    for (var i = 0; i < nBuckets; i++)
                    {
                        if (row.dtHourStart < ranges[i].dtStart || row.dtHourStart >= ranges[i].dtEnd) continue;
                        bucketSums[i] += row.dRtHour;
                        nHoursGot++;
                    }
                }
            }

            // 葉子在區間內覆蓋率 < 90% → 整體警告（避免使用者誤以為 total 完整）
            if (nExpectedHoursPerLeaf > 0 && nHoursGot < nExpectedHoursPerLeaf * 0.9)
                bHasWarning = true;
        }
        return (bucketSums, bHasWarning);
    }

    private record LeafHourRow(DateTime dtHourStart, double dRtHour);

    private static async Task<List<LeafHourRow>> GetLeafHourRowsAsync(
        SqlConnection conn, string szSid, DateTime dtFrom, DateTime dtToExclusive)
    {
        const string szSql = @"
            SELECT HourStart, RtHour
            FROM   WaterLeafHourly WITH (NOLOCK)
            WHERE  SID = @sid AND HourStart >= @from AND HourStart < @to
            ORDER BY HourStart";

        var rows = await conn.QueryAsync<(DateTime HourStart, double RtHour)>(szSql, new
        {
            sid = szSid,
            from = dtFrom,
            to = dtToExclusive
        });
        return rows.Select(r => new LeafHourRow(r.HourStart, r.RtHour)).ToList();
    }

    /// <summary>binary search：找 hourStart 落在哪個 bucket（boundaries[i] &lt;= hour &lt; boundaries[i+1]）</summary>
    private static int FindBucketIndex(List<DateTime> boundaries, DateTime dtHour)
    {
        int lo = 0, hi = boundaries.Count - 1;   // hi 為最後 boundary 的 index（exclusive）
        if (dtHour < boundaries[0] || dtHour >= boundaries[hi]) return -1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (dtHour < boundaries[mid]) hi = mid;
            else lo = mid;
        }
        return lo;
    }

    private static void FillBucketsAndTotal(
        RefrigerationTonReportResult result, List<(DateTime dtStart, DateTime dtEnd)> ranges, List<string> labels, double[] bucketSums)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            result.buckets.Add(new RefrigerationTonReportBucket
            {
                dtBucketStart = ranges[i].dtStart,
                szLabel = labels[i],
                dRtHour = Math.Round(bucketSums[i], 3)
            });
            result.dTotalRtHour += bucketSums[i];
        }
        result.dTotalRtHour = Math.Round(result.dTotalRtHour, 3);
    }

    /// <summary>產生 N+1 個邊界時刻（含起點與終點）— 與 EnergyReportService 同邏輯</summary>
    public List<DateTime> BuildBoundaries(string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var list = new List<DateTime>();
        switch (szGranularity)
        {
            case "hour":
                {
                    var hourStart = new DateTime(dtStart.Year, dtStart.Month, dtStart.Day, dtStart.Hour, 0, 0);
                    var hourEndInclusive = new DateTime(dtEnd.Year, dtEnd.Month, dtEnd.Day, dtEnd.Hour, 0, 0);
                    var hourEndExclusive = hourEndInclusive.AddHours(1);
                    if (hourEndExclusive <= hourStart)
                        hourEndExclusive = hourStart.AddHours(1);
                    for (var t = hourStart; t <= hourEndExclusive; t = t.AddHours(1))
                        list.Add(t);
                    break;
                }
            case "day":
                {
                    var dayStart = dtStart.Date;
                    var dayEndExclusive = dtEnd.Date.AddDays(1);
                    if (dayEndExclusive <= dayStart)
                        dayEndExclusive = dayStart.AddDays(1);
                    for (var t = dayStart; t <= dayEndExclusive; t = t.AddDays(1))
                        list.Add(t);
                    break;
                }
            case "month":
                // 月粒度已改為期別切法（每期一對 [起, 訖) 邊界，可能不連續）— 走 BuildBucketRangesAsync
                throw new ArgumentException("月粒度期界由 BillingPeriodService 解析，不支援 BuildBoundaries");
            case "year":
                {
                    var t = new DateTime(dtStart.Year, 1, 1);
                    var endYear = new DateTime(dtEnd.Year, 1, 1);
                    while (t <= endYear)
                    {
                        list.Add(t);
                        t = t.AddYears(1);
                    }
                    list.Add(endYear.AddYears(1));
                    break;
                }
            default:
                throw new ArgumentException($"未知粒度: {szGranularity}");
        }
        return list;
    }

    /// <summary>由邊界陣列產出 N 個 bucket 的顯示標籤 — 與 EnergyReportService 同邏輯</summary>
    public List<string> BuildLabels(string szGranularity, List<DateTime> boundaries)
    {
        var ci = CultureInfo.InvariantCulture;
        var labels = new List<string>(boundaries.Count - 1);
        var bDayCrossYear = szGranularity == "day"
            && boundaries.Count >= 2
            && boundaries[0].Year != boundaries[^2].Year;
        var bHourCrossDay = szGranularity == "hour"
            && boundaries.Count >= 2
            && boundaries[0].Date != boundaries[^2].Date;
        var bHourCrossYear = bHourCrossDay && boundaries[0].Year != boundaries[^2].Year;
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var t = boundaries[i];
            labels.Add(szGranularity switch
            {
                "hour" => bHourCrossYear ? t.ToString("yyyy-MM-dd HH:00", ci)
                        : bHourCrossDay ? t.ToString("MM/dd HH:00", ci)
                        : t.ToString("HH:00", ci),
                "day" => bDayCrossYear ? t.ToString("yyyy-MM-dd", ci) : t.ToString("MM/dd", ci),
                "month" => t.ToString("yyyy-MM", ci),
                "year" => t.ToString("yyyy", ci),
                _ => t.ToString("yyyy-MM-dd HH:mm", ci)
            });
        }
        return labels;
    }
}
