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
    private string _szConnectionString = string.Empty;

    public RefrigerationTonReportService(
        ILogger<RefrigerationTonReportService> logger,
        DatabaseConfigService configService,
        WaterCircuitService circuitService)
    {
        _logger = logger;
        _configService = configService;
        _circuitService = circuitService;
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

        var boundaries = BuildBoundaries(szGranularity, dtStart, dtEnd);
        var labels = BuildLabels(szGranularity, boundaries);

        var result = new RefrigerationTonReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = boundaries[0],
            dtEnd = boundaries[^1],
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning) = await ComputeBucketSumsForCircuitAsync(nCircuitId, boundaries, conn);

        FillBucketsAndTotal(result, boundaries, labels, bucketSums);
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

        var boundaries = BuildBoundaries(szGranularity, dtStart, dtEnd);
        var labels = BuildLabels(szGranularity, boundaries);

        var result = new RefrigerationTonReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = boundaries[0],
            dtEnd = boundaries[^1],
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning) = await ComputeBucketSumsForCircuitAsync(nCircuitId, boundaries, conn);
        FillBucketsAndTotal(result, boundaries, labels, bucketSums);
        result.isHasWarning = bHasWarning;

        // 自己就是葉子 → 不展開子層
        if (!string.IsNullOrEmpty(circuit.szSID))
            return result;

        var children = await _circuitService.GetDirectChildrenAsync(nCircuitId);
        foreach (var child in children)
        {
            var (childSums, childWarning) = await ComputeBucketSumsForCircuitAsync(child.nId, boundaries, conn);
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

    /// <summary>對單一迴路（葉子或虛擬皆可）計算每個 bucket 的 RT·h 累計。</summary>
    private async Task<(double[] bucketSums, bool isHasWarning)> ComputeBucketSumsForCircuitAsync(
        int nCircuitId, List<DateTime> boundaries, SqlConnection conn)
    {
        var nBuckets = boundaries.Count - 1;
        var bucketSums = new double[nBuckets];
        var bHasWarning = false;

        var leaves = await _circuitService.GetLeavesUnderAsync(nCircuitId);
        if (leaves.Count == 0) return (bucketSums, bHasWarning);

        var dtRangeStart = boundaries[0];
        var dtRangeEnd = boundaries[^1];   // exclusive

        // 預期該迴路在區間內應有多少 hour（用來偵測「資料不全」警告）
        var nExpectedHoursPerLeaf = (int)Math.Round((dtRangeEnd - dtRangeStart).TotalHours);

        foreach (var leaf in leaves)
        {
            if (string.IsNullOrEmpty(leaf.szSID)) continue;
            var rows = await GetLeafHourRowsAsync(conn, leaf.szSID!, dtRangeStart, dtRangeEnd);

            int nHoursGot = 0;
            foreach (var row in rows)
            {
                var nIdx = FindBucketIndex(boundaries, row.dtHourStart);
                if (nIdx < 0 || nIdx >= nBuckets) continue;
                bucketSums[nIdx] += row.dRtHour;
                nHoursGot++;
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
        RefrigerationTonReportResult result, List<DateTime> boundaries, List<string> labels, double[] bucketSums)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            result.buckets.Add(new RefrigerationTonReportBucket
            {
                dtBucketStart = boundaries[i],
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
                {
                    var t = new DateTime(dtStart.Year, dtStart.Month, 1);
                    var endMonth = new DateTime(dtEnd.Year, dtEnd.Month, 1);
                    while (t <= endMonth)
                    {
                        list.Add(t);
                        t = t.AddMonths(1);
                    }
                    list.Add(endMonth.AddMonths(1));
                    break;
                }
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
