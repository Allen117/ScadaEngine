using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 用電報表 — on-demand 計算。
/// 流程：依粒度產生 N 個 bucket 的 [起, 訖) 邊界對（月粒度 = 期別，見 BillingPeriodService）
/// → 一條 SQL 取每個葉子在每個邊界時刻的最近值 →
/// 每 bucket 訖值減起值 (含 kWh 溢位處理) → 各葉子 delta 加總 = 該 bucket 用電量。
/// </summary>
public class EnergyReportService
{
    private readonly ILogger<EnergyReportService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly EnergyCircuitService _circuitService;
    private readonly BillingPeriodService _billingPeriodService;
    private readonly int _nMaxStalenessHours;
    private string _szConnectionString = string.Empty;

    public EnergyReportService(
        ILogger<EnergyReportService> logger,
        DatabaseConfigService configService,
        EnergyCircuitService circuitService,
        BillingPeriodService billingPeriodService,
        IConfiguration configuration)
    {
        _logger = logger;
        _configService = configService;
        _circuitService = circuitService;
        _billingPeriodService = billingPeriodService;
        // 邊界值有效期視窗（小時）：超過此值的「最近一筆」視為 null，避免電表斷線復原時將累積差異全壓在恢復首小時
        _nMaxStalenessHours = configuration.GetValue<int?>("EnergyAggregation:MaxStalenessHours") ?? 2;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// 取得報表結果。
    /// </summary>
    /// <param name="nCircuitId">迴路 Id（葉子或虛擬皆可）</param>
    /// <param name="szGranularity">hour / day / month / year</param>
    /// <param name="dtStart">區間起點（含），時/日粒度需精確到天，月需精確到月，年需精確到年</param>
    /// <param name="dtEnd">區間終點（含），同上</param>
    public async Task<EnergyReportResult> GetReportAsync(
        int nCircuitId, string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = await BuildBucketRangesAsync(szGranularity, dtStart, dtEnd);

        var result = new EnergyReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning, staleFlags) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);

        FillBucketsAndTotal(result, ranges, labels, bucketSums, staleFlags);
        result.isHasWarning = bHasWarning;
        return result;
    }

    /// <summary>
    /// 同 GetReportAsync，再額外展開「直接子迴路」每個的 bucket series — 給 Excel 匯出多欄使用。
    /// 若查詢的迴路本身就是葉子（綁 SID），children 保持空，匯出格式維持 2 欄。
    /// </summary>
    public async Task<EnergyReportResult> GetReportWithChildrenAsync(
        int nCircuitId, string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = await BuildBucketRangesAsync(szGranularity, dtStart, dtEnd);

        var result = new EnergyReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = szGranularity,
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning, staleFlags) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        FillBucketsAndTotal(result, ranges, labels, bucketSums, staleFlags);
        result.isHasWarning = bHasWarning;

        // 自己就是葉子 → 不展開子層（與舊版單錶匯出格式相容）
        if (!string.IsNullOrEmpty(circuit.szSID))
            return result;

        var children = await _circuitService.GetDirectChildrenAsync(nCircuitId);
        foreach (var child in children)
        {
            // 子迴路內部 leaves 的 sign 已由 GetLeavesUnderAsync 累乘（相對於 child），
            // child 自己對父的方向需在這裡額外乘上。
            var (childSums, childWarning, _) = await ComputeBucketSumsForCircuitAsync(child.nId, ranges, conn);
            var nChildSign = child.nSign == -1 ? -1 : 1;
            var series = new EnergyReportChildSeries
            {
                nCircuitId = child.nId,
                szName = child.szName,
            };
            double dTotal = 0;
            for (var i = 0; i < labels.Count; i++)
            {
                var dValue = childSums[i] * nChildSign;
                series.dKwhPerBucket.Add(Math.Round(dValue, 3));
                dTotal += dValue;
            }
            series.dTotalKwh = Math.Round(dTotal, 3);
            if (childWarning) result.isHasWarning = true;
            result.children.Add(series);
        }

        return result;
    }

    /// <summary>
    /// 能源申報專用 — 指定年度的 12 個「曆月」bucket（每月 1 號 00:00 ~ 次月 1 號 00:00），
    /// 不走月結期別（BillingPeriodService）。共用 ComputeBucketSumsForCircuitAsync 計算核心，
    /// 溢位/Sign/staleness 規則與一般報表完全一致。
    /// </summary>
    public async Task<EnergyReportResult> GetCalendarMonthlyReportAsync(int nCircuitId, int nYear)
    {
        var circuit = await _circuitService.GetByIdAsync(nCircuitId);
        if (circuit == null)
            throw new InvalidOperationException($"迴路 Id={nCircuitId} 不存在");

        var (ranges, labels) = BuildCalendarMonthRanges(nYear);

        var result = new EnergyReportResult
        {
            nCircuitId = nCircuitId,
            szCircuitName = circuit.szName,
            szGranularity = "month",
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };

        using var conn = await GetConnectionAsync();
        var (bucketSums, bHasWarning, staleFlags) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        FillBucketsAndTotal(result, ranges, labels, bucketSums, staleFlags);
        result.isHasWarning = bHasWarning;
        return result;
    }

    /// <summary>指定年度的 12 個曆月 [起, 訖) 邊界對與 yyyy-MM 標籤</summary>
    public static (List<(DateTime dtStart, DateTime dtEnd)> ranges, List<string> labels)
        BuildCalendarMonthRanges(int nYear)
    {
        var ranges = new List<(DateTime, DateTime)>(12);
        var labels = new List<string>(12);
        for (var m = 1; m <= 12; m++)
        {
            var dtMonthStart = new DateTime(nYear, m, 1);
            ranges.Add((dtMonthStart, dtMonthStart.AddMonths(1)));
            labels.Add(dtMonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
        return (ranges, labels);
    }

    /// <summary>
    /// 取得指定迴路在區間內的總用電量 = 該粒度所有 bucket 的加總。
    /// 與 GetReportAsync 共用同一計算核心（staleness window / 溢位規則一致），
    /// 確保「期間總量」與長條圖各柱總和完全對得上。
    /// 注意：回傳值未套用迴路自身對父層的 Sign — 子迴路呼叫端需比照 GetReportWithChildrenAsync 額外乘上。
    /// </summary>
    public async Task<double> GetTotalKwhAsync(int nCircuitId, string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var (ranges, _) = await BuildBucketRangesAsync(szGranularity, dtStart, dtEnd);
        using var conn = await GetConnectionAsync();
        var (bucketSums, _, _) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        return Math.Round(bucketSums.Sum(), 3);
    }

    /// <summary>
    /// 對自訂 [起, 訖) bucket 邊界對計算迴路 kWh — 能源基線/SEU 取樣用
    /// （曆日/曆月等切法由呼叫端決定，不走本服務的粒度/期別規則）。
    /// 計算核心與 GetReportAsync 完全共用（staleness window / 溢位 / Sign 規則一致）。
    /// 注意：回傳值未套用迴路自身對父層的 Sign，語意同 GetTotalKwhAsync。
    /// </summary>
    public async Task<(double[] bucketSums, bool[] staleFlags)> GetBucketKwhForRangesAsync(
        int nCircuitId, List<(DateTime dtStart, DateTime dtEnd)> ranges)
    {
        using var conn = await GetConnectionAsync();
        var (bucketSums, _, staleFlags) = await ComputeBucketSumsForCircuitAsync(nCircuitId, ranges, conn);
        return (bucketSums, staleFlags);
    }

    /// <summary>
    /// 產生 N 個 bucket 的 [起, 訖) 邊界對與標籤。
    /// 月粒度 = 期別：dtStart/dtEnd 的年月視為期別編號，期界由 BillingPeriodService 解析
    /// （期別間可能空窗/重疊 → 不共用邊界點）；其餘粒度沿用連續邊界切法。
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
    /// 對單一迴路（葉子或虛擬皆可）計算每個 bucket 的 kWh 累計。
    /// 共用核心：取葉子 → 對每葉子查邊界值 → 套溢位規則加總。
    /// 邊界時刻去重後一次查詢 — 連續 bucket 共用邊界點時查詢量與舊版相同。
    /// </summary>
    private async Task<(double[] bucketSums, bool isHasWarning, bool[] staleFlags)> ComputeBucketSumsForCircuitAsync(
        int nCircuitId, List<(DateTime dtStart, DateTime dtEnd)> ranges, SqlConnection conn)
    {
        var nBuckets = ranges.Count;
        var bucketSums = new double[nBuckets];
        var staleFlags = new bool[nBuckets];
        var bHasWarning = false;

        var leaves = await _circuitService.GetLeavesUnderAsync(nCircuitId);
        if (leaves.Count == 0) return (bucketSums, bHasWarning, staleFlags);

        // 「當期未過完」語意：把每個 bucket 的計算起訖夾到 min(t, 現在)，
        // 讓當期期末（落在未來）改抓「現在的累積值」→ 得到期初到現在的差值；
        // 純未來 bucket 被夾成零寬 → delta 0。顯示用 ranges/labels 維持原值（FillBucketsAndTotal 使用），此處僅供取邊界值。
        var dtNow = DateTime.Now;
        var clampedRanges = new (DateTime dtStart, DateTime dtEnd)[nBuckets];
        for (var i = 0; i < nBuckets; i++)
        {
            var dtCs = ranges[i].dtStart < dtNow ? ranges[i].dtStart : dtNow;
            var dtCe = ranges[i].dtEnd < dtNow ? ranges[i].dtEnd : dtNow;
            clampedRanges[i] = (dtCs, dtCe);
        }

        var times = clampedRanges.SelectMany(r => new[] { r.dtStart, r.dtEnd })
            .Distinct().OrderBy(t => t).ToList();
        var timeIndex = new Dictionary<DateTime, int>(times.Count);
        for (var i = 0; i < times.Count; i++) timeIndex[times[i]] = i;

        foreach (var leafWithSign in leaves)
        {
            var leaf = leafWithSign.Leaf;
            var nEffectiveSign = leafWithSign.nEffectiveSign;
            var values = await GetBoundaryValuesAsync(conn, leaf.szSID!, times);
            for (var i = 0; i < nBuckets; i++)
            {
                var fStart = values[timeIndex[clampedRanges[i].dtStart]];
                var fEnd = values[timeIndex[clampedRanges[i].dtEnd]];
                if (fStart == null || fEnd == null)
                {
                    // 邊界值抓不到（staleness window 內無 Quality=1 資料 / 缺資料）→ 標記該 bucket 斷線
                    staleFlags[i] = true;
                    continue;
                }

                // 物理 delta 永遠 ≥ 0（含溢位處理），sign 在這裡套用合併方向
                var (dDelta, isWarn) = CalcDeltaWithRollover(fStart.Value, fEnd.Value, leaf.dMaxKwh, leaf.szSID!, leaf.szName);
                bucketSums[i] += dDelta * nEffectiveSign;
                if (isWarn) bHasWarning = true;
            }
        }
        return (bucketSums, bHasWarning, staleFlags);
    }

    private static void FillBucketsAndTotal(
        EnergyReportResult result, List<(DateTime dtStart, DateTime dtEnd)> ranges, List<string> labels, double[] bucketSums, bool[] staleFlags)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            result.buckets.Add(new EnergyReportBucket
            {
                dtBucketStart = ranges[i].dtStart,
                szLabel = labels[i],
                dKwh = Math.Round(bucketSums[i], 3),
                isStale = staleFlags[i]
            });
            result.dTotalKwh += bucketSums[i];
        }
        result.dTotalKwh = Math.Round(result.dTotalKwh, 3);
    }

    /// <summary>
    /// kWh 溢位/重置 delta 計算。
    /// V_end >= V_start: 正常累計
    /// V_end &lt; V_start &amp;&amp; MaxKwh 有設: (Max - Vs) + Ve
    /// V_end &lt; V_start &amp;&amp; MaxKwh 無設: 視為電表重置/異常，回 0 並警告
    /// </summary>
    public (double dDelta, bool isWarning) CalcDeltaWithRollover(
        double dStart, double dEnd, double? dMaxKwh, string szSID, string szLeafName)
    {
        if (dEnd >= dStart)
            return (dEnd - dStart, false);

        if (dMaxKwh.HasValue && dMaxKwh.Value > 0)
            return ((dMaxKwh.Value - dStart) + dEnd, false);

        _logger.LogWarning(
            "電表 {SID} ({Name}) 偵測到累積值倒退（{Start} → {End}）但未設定 MaxKwh，該段 delta 視為 0",
            szSID, szLeafName, dStart, dEnd);
        return (0, true);
    }

    /// <summary>用 OUTER APPLY + VALUES 一條 SQL 取所有邊界對應的最近一筆值。
    /// 套 staleness window：若最近一筆 Timestamp 距 boundary 已超過 MaxStalenessHours，視為 null
    /// — 避免電表斷線復原時把整段累積差異全壓在恢復首小時的「巨柱」假象。</summary>
    private async Task<double?[]> GetBoundaryValuesAsync(SqlConnection conn, string szSid, List<DateTime> boundaries)
    {
        // 組 VALUES (索引, 邊界) 列表
        var sb = new System.Text.StringBuilder();
        var dynParams = new DynamicParameters();
        dynParams.Add("@sid", szSid);
        dynParams.Add("@maxStalenessHours", _nMaxStalenessHours);

        sb.Append("SELECT b.idx, ba.Value FROM (VALUES ");
        for (var i = 0; i < boundaries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@i{i}, @t{i})");
            dynParams.Add($"@i{i}", i);
            dynParams.Add($"@t{i}", boundaries[i]);
        }
        sb.Append(@") AS b(idx, BoundaryTime)
                   OUTER APPLY (
                       SELECT TOP 1 Value FROM HistoryData WITH (NOLOCK)
                       WHERE  SID = @sid
                          AND Timestamp <= b.BoundaryTime
                          AND Timestamp >= DATEADD(HOUR, -@maxStalenessHours, b.BoundaryTime)
                          AND Quality = 1
                       ORDER BY Timestamp DESC
                   ) ba
                   ORDER BY b.idx");

        var rows = await conn.QueryAsync<(int idx, double? Value)>(sb.ToString(), dynParams);
        var arr = new double?[boundaries.Count];
        foreach (var r in rows) arr[r.idx] = r.Value;
        return arr;
    }

    /// <summary>產生 N+1 個邊界時刻（含起點與終點）</summary>
    public List<DateTime> BuildBoundaries(string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var list = new List<DateTime>();
        switch (szGranularity)
        {
            case "hour":
                {
                    // dtStart=起時、dtEnd=訖時（皆截到整點）；產出起時 ~ 訖時隔小時的每小時邊界
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
                    // dtStart=起日，dtEnd=訖日；產出起日 00:00 ~ 訖日隔日 00:00 的每日邊界
                    var dayStart = dtStart.Date;
                    var dayEndExclusive = dtEnd.Date.AddDays(1);
                    if (dayEndExclusive <= dayStart)
                        dayEndExclusive = dayStart.AddDays(1);
                    for (var t = dayStart; t <= dayEndExclusive; t = t.AddDays(1))
                        list.Add(t);
                    break;
                }
            case "month":
                // 月粒度已改為期別切法（每期一對 [起, 訖) 邊界，可能不連續），
                // 不能用單一連續邊界列表表達 — 走 BuildBucketRangesAsync / BillingPeriodService
                throw new ArgumentException("月粒度期界由 BillingPeriodService 解析，不支援 BuildBoundaries");
            case "year":
                {
                    // dtStart=當年 1/1，dtEnd=當年 1/1
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

    /// <summary>由邊界陣列產出 N 個 bucket 的顯示標籤</summary>
    public List<string> BuildLabels(string szGranularity, List<DateTime> boundaries)
    {
        var ci = CultureInfo.InvariantCulture;
        var labels = new List<string>(boundaries.Count - 1);
        // 日粒度跨年時用 yyyy-MM-dd 避免 MM/dd 重複
        var bDayCrossYear = szGranularity == "day"
            && boundaries.Count >= 2
            && boundaries[0].Year != boundaries[^2].Year;
        // 時粒度跨日時加上日期前綴避免 HH:00 重複（同年用 MM/dd HH:00，跨年用 yyyy-MM-dd HH:00）
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
