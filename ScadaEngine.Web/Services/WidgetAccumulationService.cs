using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.ScadaPage.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// ScadaPage 累積量元件計算核心（任意 SID 的當日/當月累積）。
/// 兩種點位性質：
///   meter     — 累積讀值差值：最近值 − 期初邊界值，含溢位處理
///               （語意與 EnergyReportService.CalcDeltaWithRollover / GetBoundaryValuesAsync 一致）
///   integrate — 瞬時值時間積分（左矩形法，每筆值持續至下一筆，
///               段長 clamp 至 min(下一筆, 期末, 本筆+MaxGap)，掉線時段不灌水）
/// 積分走分層快取（完成日/完成小時 bucket 不變 + 當前小時尾段即時算），
/// 穩態每 SID 每次輪詢只掃當前小時 ≤60 筆。
/// </summary>
public class WidgetAccumulationService
{
    private readonly ILogger<WidgetAccumulationService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly MqttRealtimeSubscriberService _mqttService;
    private readonly WidgetAccumulationCache _cache;
    private readonly int _nMaxStalenessHours;
    private readonly int _nMaxGapMinutes;
    private readonly int _nResultCacheSeconds;
    private string _szConnectionString = string.Empty;

    public WidgetAccumulationService(
        ILogger<WidgetAccumulationService> logger,
        DatabaseConfigService configService,
        MqttRealtimeSubscriberService mqttService,
        WidgetAccumulationCache cache,
        IConfiguration configuration)
    {
        _logger = logger;
        _configService = configService;
        _mqttService = mqttService;
        _cache = cache;
        _nMaxStalenessHours = configuration.GetValue<int?>("ScadaPageAccumulation:MaxStalenessHours") ?? 2;
        _nMaxGapMinutes = configuration.GetValue<int?>("ScadaPageAccumulation:MaxGapMinutes") ?? 5;
        _nResultCacheSeconds = configuration.GetValue<int?>("ScadaPageAccumulation:ResultCacheSeconds") ?? 30;
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
    /// 批次計算。dtNow 由呼叫端傳入（正常為 DateTime.Now），便於測試跨日/跨月換期。
    /// </summary>
    public async Task<List<AccumulationResultDto>> ComputeAsync(List<AccumulationQueryItem> items, DateTime dtNow)
    {
        _cache.PruneIfDayChanged(dtNow);

        var results = new List<AccumulationResultDto>();
        SqlConnection? conn = null;
        try
        {
            foreach (var item in items)
            {
                var szResultKey = WidgetAccumulationCache.ResultKey(item);
                if (_cache.TryGetResult(szResultKey, dtNow, out var cached))
                {
                    results.Add(cached);
                    continue;
                }

                var semaphore = _cache.GetLock(item.szSid);
                await semaphore.WaitAsync();
                try
                {
                    // 拿到鎖後重查 — 其他請求可能剛算完
                    if (_cache.TryGetResult(szResultKey, dtNow, out cached))
                    {
                        results.Add(cached);
                        continue;
                    }

                    conn ??= await GetConnectionAsync();
                    var dto = item.szAccKind == "integrate"
                        ? await ComputeIntegrateAsync(conn, item, dtNow)
                        : await ComputeMeterAsync(conn, item, dtNow);
                    _cache.SetResult(szResultKey, dto, dtNow.AddSeconds(_nResultCacheSeconds));
                    results.Add(dto);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }
        finally
        {
            if (conn != null) await conn.DisposeAsync();
        }
        return results;
    }

    private static DateTime GetPeriodStart(string szAccMode, DateTime dtNow)
        => szAccMode == "month" ? new DateTime(dtNow.Year, dtNow.Month, 1) : dtNow.Date;

    // ══════════════════════ meter：累積讀值差值 ══════════════════════

    private async Task<AccumulationResultDto> ComputeMeterAsync(
        SqlConnection conn, AccumulationQueryItem item, DateTime dtNow)
    {
        var dtPeriodStart = GetPeriodStart(item.szAccMode, dtNow);
        var dto = new AccumulationResultDto
        {
            szSid = item.szSid,
            szAccMode = item.szAccMode,
            dtPeriodStart = dtPeriodStart,
            dtCalcTime = dtNow,
        };

        // 期初邊界值（期內不變 → 快取；查無不快取，TOP 1 seek 便宜可重試）
        double? dStart;
        if (_cache.TryGetBoundary(item.szSid, dtPeriodStart, out var dCachedStart))
        {
            dStart = dCachedStart;
        }
        else
        {
            dStart = await GetNearestValueAsync(conn, item.szSid, dtPeriodStart);
            if (dStart.HasValue)
                _cache.SetBoundary(item.szSid, dtPeriodStart, dStart.Value);
        }

        if (!dStart.HasValue)
        {
            dto.szStatus = "no_data";
            return dto;
        }

        // 最近值：優先記憶體即時快照（與圖面即時值同源、零 DB 成本），過舊/BAD 再 fallback DB
        var (dEnd, isStale) = await GetLatestValueAsync(conn, item.szSid, dtNow);
        if (!dEnd.HasValue)
        {
            dto.szStatus = "stale"; // 期初有值但現值抓不到（掉線超過 staleness window）
            return dto;
        }

        dto.dValue = CalcDeltaWithRollover(dStart.Value, dEnd.Value, item.dMaxValue, item.szSid);
        dto.szStatus = isStale ? "stale" : "ok";
        return dto;
    }

    /// <summary>
    /// 溢位規則（語意同 EnergyReportService.CalcDeltaWithRollover）：
    /// end >= start 正常差；end &lt; start 且 MaxValue > 0 → (Max−Vs)+Ve；否則視為歸零/異常 → 0 並警告。
    /// 已知限制：整期只能偵測一次溢位。
    /// </summary>
    private double CalcDeltaWithRollover(double dStart, double dEnd, double? dMaxValue, string szSid)
    {
        if (dEnd >= dStart)
            return dEnd - dStart;

        if (dMaxValue.HasValue && dMaxValue.Value > 0)
            return (dMaxValue.Value - dStart) + dEnd;

        _logger.LogWarning(
            "累積量元件：點位 {SID} 累積值倒退（{Start} → {End}）但未設定溢位上限，delta 視為 0",
            szSid, dStart, dEnd);
        return 0;
    }

    /// <summary>取 boundary 時刻的最近一筆 Quality=1 值（staleness window 內），查無回 null</summary>
    private async Task<double?> GetNearestValueAsync(SqlConnection conn, string szSid, DateTime dtBoundary)
    {
        return await conn.ExecuteScalarAsync<double?>(@"
            SELECT TOP 1 Value FROM HistoryData WITH (NOLOCK)
            WHERE  SID = @sid
               AND Timestamp <= @boundary
               AND Timestamp >= DATEADD(HOUR, -@maxStalenessHours, @boundary)
               AND Quality = 1
            ORDER BY Timestamp DESC",
            new { sid = szSid, boundary = dtBoundary, maxStalenessHours = _nMaxStalenessHours });
    }

    /// <summary>
    /// 取點位現值：記憶體快照 GOOD 且夠新 → 直接用；否則 fallback DB 最近一筆。
    /// 回傳 (值, 是否過舊)。
    /// </summary>
    private async Task<(double? dValue, bool isStale)> GetLatestValueAsync(
        SqlConnection conn, string szSid, DateTime dtNow)
    {
        var item = _mqttService.GetRealtimeDataBySids(new[] { szSid }).FirstOrDefault();
        if (item is { hasData: true, isQualityGood: true })
        {
            var isFresh = item.isFreshBypass
                          || dtNow.Subtract(item.dtTimestamp).TotalMinutes <= _nMaxGapMinutes;
            if (isFresh) return (item.dValue, false);
        }

        var dDbValue = await GetNearestValueAsync(conn, szSid, dtNow);
        return (dDbValue, true); // 走到 DB fallback 就視為 stale（即時來源已不新鮮）
    }

    // ══════════════════════ integrate：時間積分 ══════════════════════

    private async Task<AccumulationResultDto> ComputeIntegrateAsync(
        SqlConnection conn, AccumulationQueryItem item, DateTime dtNow)
    {
        var dtPeriodStart = GetPeriodStart(item.szAccMode, dtNow);
        var dto = new AccumulationResultDto
        {
            szSid = item.szSid,
            szAccMode = item.szAccMode,
            dtPeriodStart = dtPeriodStart,
            dtCalcTime = dtNow,
        };

        var dtToday = dtNow.Date;
        var dtCurrentHour = new DateTime(dtNow.Year, dtNow.Month, dtNow.Day, dtNow.Hour, 0, 0);

        // 當日 = Σ今日完成小時(L2) + 當前小時尾段(L3 即時算)
        var dDayTotal = await SumTodayHourBucketsAsync(conn, item.szSid, dtToday, dtCurrentHour)
                        + await IntegrateRangeAsync(conn, item.szSid, dtCurrentHour, dtNow);

        double dTotal;
        if (item.szAccMode == "month")
        {
            // 當月 = Σ完成日(L1，1 號 ~ 昨日) + 當日
            dTotal = await SumMonthDayBucketsAsync(conn, item.szSid, dtPeriodStart, dtToday) + dDayTotal;
        }
        else
        {
            dTotal = dDayTotal;
        }

        // 總量為 0 時區分「真的 0」vs「期內無任何樣本」
        if (dTotal == 0)
        {
            var hasSample = await conn.ExecuteScalarAsync<int?>(@"
                SELECT TOP 1 1 FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Quality = 1 AND Timestamp >= @start AND Timestamp < @end",
                new { sid = item.szSid, start = dtPeriodStart, end = dtNow });
            if (hasSample == null)
            {
                dto.szStatus = "no_data";
                return dto;
            }
        }

        dto.dValue = dTotal;

        // stale 判定：即時快照不新鮮（積分因 MaxGap clamp 已自然停在最後一筆）
        var rt = _mqttService.GetRealtimeDataBySids(new[] { item.szSid }).FirstOrDefault();
        var isFresh = rt is { hasData: true, isQualityGood: true }
                      && (rt.isFreshBypass || dtNow.Subtract(rt.dtTimestamp).TotalMinutes <= _nMaxGapMinutes);
        dto.szStatus = isFresh ? "ok" : "stale";
        return dto;
    }

    /// <summary>Σ 本月完成日 bucket（L1），缺的用一條 GROUP BY 日 SQL 補洞後快取</summary>
    private async Task<double> SumMonthDayBucketsAsync(
        SqlConnection conn, string szSid, DateTime dtMonthStart, DateTime dtTodayStart)
    {
        var missingDays = new List<DateTime>();
        var dSum = 0.0;
        for (var dtDay = dtMonthStart; dtDay < dtTodayStart; dtDay = dtDay.AddDays(1))
        {
            if (_cache.TryGetBucket(WidgetAccumulationCache.DayBucketKey(szSid, dtDay), out var dVal))
                dSum += dVal;
            else
                missingDays.Add(dtDay);
        }
        if (missingDays.Count == 0) return dSum;

        // 補洞：一條 SQL 算齊缺的所有日（首次查詢/服務重啟時的唯一一次整月掃描）
        var dtRangeStart = missingDays.Min();
        var dtRangeEnd = missingDays.Max().AddDays(1);
        // 注意：SQL Server 不允許彙總函式內含子查詢，段終點 SegEnd 須先在 segs 層算好再 SUM
        var rows = await conn.QueryAsync<(DateTime BucketStart, double dIntegral)>(@"
            ;WITH pts AS (
                SELECT Timestamp, Value,
                       CAST(CAST(Timestamp AS DATE) AS DATETIME) AS BucketStart,
                       LEAD(Timestamp) OVER (ORDER BY Timestamp) AS NextTs
                FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Quality = 1
                  AND Timestamp >= @start AND Timestamp < @end
            ),
            segs AS (
                SELECT Timestamp, Value, BucketStart,
                       (SELECT MIN(v) FROM (VALUES
                           (ISNULL(NextTs, @end)),
                           (DATEADD(DAY, 1, BucketStart)),
                           (DATEADD(SECOND, @maxGapSec, Timestamp))) AS m(v)) AS SegEnd
                FROM pts
            )
            SELECT BucketStart,
                   SUM(Value * DATEDIFF(SECOND, Timestamp, SegEnd) / 3600.0) AS dIntegral
            FROM segs
            GROUP BY BucketStart",
            new { sid = szSid, start = dtRangeStart, end = dtRangeEnd, maxGapSec = _nMaxGapMinutes * 60 });

        var computed = rows.ToDictionary(r => r.BucketStart, r => r.dIntegral);
        foreach (var dtDay in missingDays)
        {
            var dVal = computed.TryGetValue(dtDay, out var d) ? d : 0.0; // 無樣本日 = 0，也快取避免重掃
            _cache.SetBucket(WidgetAccumulationCache.DayBucketKey(szSid, dtDay), dVal);
            dSum += dVal;
        }
        return dSum;
    }

    /// <summary>Σ 今日完成小時 bucket（L2），缺的用一條 GROUP BY 小時 SQL 補洞後快取</summary>
    private async Task<double> SumTodayHourBucketsAsync(
        SqlConnection conn, string szSid, DateTime dtTodayStart, DateTime dtCurrentHour)
    {
        var missingHours = new List<DateTime>();
        var dSum = 0.0;
        for (var dtHour = dtTodayStart; dtHour < dtCurrentHour; dtHour = dtHour.AddHours(1))
        {
            if (_cache.TryGetBucket(WidgetAccumulationCache.HourBucketKey(szSid, dtHour), out var dVal))
                dSum += dVal;
            else
                missingHours.Add(dtHour);
        }
        if (missingHours.Count == 0) return dSum;

        var dtRangeStart = missingHours.Min();
        var dtRangeEnd = missingHours.Max().AddHours(1);
        var rows = await conn.QueryAsync<(DateTime BucketStart, double dIntegral)>(@"
            ;WITH pts AS (
                SELECT Timestamp, Value,
                       DATEADD(HOUR, DATEDIFF(HOUR, 0, Timestamp), 0) AS BucketStart,
                       LEAD(Timestamp) OVER (ORDER BY Timestamp) AS NextTs
                FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Quality = 1
                  AND Timestamp >= @start AND Timestamp < @end
            ),
            segs AS (
                SELECT Timestamp, Value, BucketStart,
                       (SELECT MIN(v) FROM (VALUES
                           (ISNULL(NextTs, @end)),
                           (DATEADD(HOUR, 1, BucketStart)),
                           (DATEADD(SECOND, @maxGapSec, Timestamp))) AS m(v)) AS SegEnd
                FROM pts
            )
            SELECT BucketStart,
                   SUM(Value * DATEDIFF(SECOND, Timestamp, SegEnd) / 3600.0) AS dIntegral
            FROM segs
            GROUP BY BucketStart",
            new { sid = szSid, start = dtRangeStart, end = dtRangeEnd, maxGapSec = _nMaxGapMinutes * 60 });

        var computed = rows.ToDictionary(r => r.BucketStart, r => r.dIntegral);
        foreach (var dtHour in missingHours)
        {
            var dVal = computed.TryGetValue(dtHour, out var d) ? d : 0.0;
            _cache.SetBucket(WidgetAccumulationCache.HourBucketKey(szSid, dtHour), dVal);
            dSum += dVal;
        }
        return dSum;
    }

    /// <summary>單段即時積分（L3 當前小時尾段，不快取；穩態 ≤60 筆）</summary>
    private async Task<double> IntegrateRangeAsync(
        SqlConnection conn, string szSid, DateTime dtStart, DateTime dtEnd)
    {
        if (dtEnd <= dtStart) return 0;
        return await conn.ExecuteScalarAsync<double?>(@"
            ;WITH pts AS (
                SELECT Timestamp, Value,
                       LEAD(Timestamp) OVER (ORDER BY Timestamp) AS NextTs
                FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Quality = 1
                  AND Timestamp >= @start AND Timestamp < @end
            ),
            segs AS (
                SELECT Timestamp, Value,
                       (SELECT MIN(v) FROM (VALUES
                           (ISNULL(NextTs, @end)),
                           (DATEADD(SECOND, @maxGapSec, Timestamp))) AS m(v)) AS SegEnd
                FROM pts
            )
            SELECT SUM(Value * DATEDIFF(SECOND, Timestamp, SegEnd) / 3600.0)
            FROM segs",
            new { sid = szSid, start = dtStart, end = dtEnd, maxGapSec = _nMaxGapMinutes * 60 }) ?? 0;
    }
}
