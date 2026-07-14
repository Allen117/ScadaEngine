using System.Collections.Concurrent;
using ScadaEngine.Web.Features.ScadaPage.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// ScadaPage 累積量元件的程序內快取（Singleton）。
/// 分層設計：
///   L1 完成日積分  key = "{sid}|D|yyyy-MM-dd"     — 歷史不變，跨月 prune
///   L2 完成小時積分 key = "{sid}|H|yyyy-MM-dd-HH"  — 歷史不變，跨日 prune
///   （L3 當前小時尾段不快取，每次即時算）
///   meter 期初邊界值 key = "{sid}|B|yyyyMMddHHmm"  — 期內不變（只快取查得到值的；查無不快取，讓便宜的 TOP 1 seek 重試）
///   L4 結果微快取   key = "{sid}|{mode}|{kind}|{max}" — TTL 秒級，多分頁/多人輪詢去重
/// </summary>
public class WidgetAccumulationCache
{
    private readonly ConcurrentDictionary<string, double> _buckets = new();
    private readonly ConcurrentDictionary<string, double> _boundaries = new();
    private readonly ConcurrentDictionary<string, (AccumulationResultDto dto, DateTime dtExpire)> _results = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private DateTime _dtLastPruneDay = DateTime.MinValue;
    private readonly object _pruneLock = new();

    // ── bucket（L1/L2 積分值）─────────────────────────────

    public bool TryGetBucket(string szKey, out double dValue) => _buckets.TryGetValue(szKey, out dValue);

    public void SetBucket(string szKey, double dValue) => _buckets[szKey] = dValue;

    public static string DayBucketKey(string szSid, DateTime dtDay) => $"{szSid}|D|{dtDay:yyyy-MM-dd}";

    public static string HourBucketKey(string szSid, DateTime dtHour) => $"{szSid}|H|{dtHour:yyyy-MM-dd-HH}";

    // ── meter 期初邊界值 ─────────────────────────────────

    public bool TryGetBoundary(string szSid, DateTime dtBoundary, out double dValue)
        => _boundaries.TryGetValue(BoundaryKey(szSid, dtBoundary), out dValue);

    public void SetBoundary(string szSid, DateTime dtBoundary, double dValue)
        => _boundaries[BoundaryKey(szSid, dtBoundary)] = dValue;

    private static string BoundaryKey(string szSid, DateTime dtBoundary) => $"{szSid}|B|{dtBoundary:yyyyMMddHHmm}";

    // ── L4 結果微快取 ────────────────────────────────────

    public bool TryGetResult(string szKey, DateTime dtNow, out AccumulationResultDto dto)
    {
        dto = null!;
        if (!_results.TryGetValue(szKey, out var entry)) return false;
        if (entry.dtExpire <= dtNow)
        {
            _results.TryRemove(szKey, out _);
            return false;
        }
        dto = entry.dto;
        return true;
    }

    public void SetResult(string szKey, AccumulationResultDto dto, DateTime dtExpire)
        => _results[szKey] = (dto, dtExpire);

    public static string ResultKey(AccumulationQueryItem item)
        => $"{item.szSid}|{item.szAccMode}|{item.szAccKind}|{item.dMaxValue?.ToString() ?? ""}";

    // ── per-SID 計算鎖（防 stampede）──────────────────────

    public SemaphoreSlim GetLock(string szSid) => _locks.GetOrAdd(szSid, _ => new SemaphoreSlim(1, 1));

    // ── 跨期 prune ───────────────────────────────────────

    /// <summary>
    /// 每日第一次呼叫時清理過期項目：
    /// L2 小時 bucket 只留今日、L1 日 bucket 只留本月與上月（月初查上月邊界仍可能用到）、
    /// meter 邊界值只留仍為有效期初者、L4 過期項。
    /// </summary>
    public void PruneIfDayChanged(DateTime dtNow)
    {
        if (_dtLastPruneDay.Date == dtNow.Date) return;
        lock (_pruneLock)
        {
            if (_dtLastPruneDay.Date == dtNow.Date) return;
            _dtLastPruneDay = dtNow;

            var szToday = dtNow.ToString("yyyy-MM-dd");
            var dtPrevMonth = new DateTime(dtNow.Year, dtNow.Month, 1).AddMonths(-1);

            foreach (var szKey in _buckets.Keys)
            {
                var parts = szKey.Split('|');
                if (parts.Length != 3) continue;
                if (parts[1] == "H" && !parts[2].StartsWith(szToday))
                    _buckets.TryRemove(szKey, out _);
                else if (parts[1] == "D"
                         && DateTime.TryParse(parts[2], out var dtDay)
                         && dtDay < dtPrevMonth)
                    _buckets.TryRemove(szKey, out _);
            }

            // 邊界值：只有「今日 00:00」與「本月 1 號 00:00」是活的期初，其餘移除
            var szValid1 = dtNow.Date.ToString("yyyyMMddHHmm");
            var szValid2 = new DateTime(dtNow.Year, dtNow.Month, 1).ToString("yyyyMMddHHmm");
            foreach (var szKey in _boundaries.Keys)
            {
                var szTail = szKey[(szKey.LastIndexOf('|') + 1)..];
                if (szTail != szValid1 && szTail != szValid2)
                    _boundaries.TryRemove(szKey, out _);
            }

            foreach (var kvp in _results)
            {
                if (kvp.Value.dtExpire <= dtNow)
                    _results.TryRemove(kvp.Key, out _);
            }
        }
    }
}
