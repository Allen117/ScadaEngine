using System.Globalization;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Common.Data.Services;

/// <summary>
/// 電費月結週期（期別）推導核心 — 純邏輯、無 DB/DI 依賴，Web 與 Engine 共用單一真相。
/// 自 Web BillingPeriodService 抽出（該 service 保留 DB 讀寫與快取、推導委派此處）；
/// Engine 迴路用電虛擬點位（NRGP- 本月電度）需要相同期界，抽共用避免兩份實作分岔。
///
/// 期別 M 的解析規則（設計決策見 docs/功能說明書_能源管理.md §月結週期）：
///   1. BillingPeriods 有 row → 直接採用（使用者自訂）
///   2. 無 row → 起始 = 前一期結束 +1 天（往前追溯至最近一筆自訂 row 逐期級聯），
///      結束 = 起始 + 1 個月 − 1 天；完全沒有任何自訂 row 時 = 自然月（1 日～最後一日）
/// </summary>
public static class BillingPeriodResolver
{
    /// <summary>取得單一期別的解析結果（自訂或推導）</summary>
    public static BillingPeriodRange ResolvePeriod(
        int nYear, int nMonth, IReadOnlyDictionary<(int, int), BillingPeriodModel> rows)
    {
        if (rows.TryGetValue((nYear, nMonth), out var row))
            return MakeRange(nYear, nMonth, row.dtStartDate.Date, row.dtEndDate.Date, isCustomized: true);

        // 最近一筆更早的自訂 row（(年, 月) tuple 字典序即時間序）
        var target = (nYear, nMonth);
        (int, int)? anchor = null;
        foreach (var key in rows.Keys)
        {
            if (key.CompareTo(target) >= 0) continue;
            if (anchor == null || key.CompareTo(anchor.Value) > 0) anchor = key;
        }

        if (anchor == null)
        {
            // 完全無自訂 → 自然月
            var dtNatural = new DateTime(nYear, nMonth, 1);
            return MakeRange(nYear, nMonth, dtNatural, dtNatural.AddMonths(1).AddDays(-1), isCustomized: false);
        }

        // 從最近自訂 row 逐期級聯：起始 = 前期結束 +1 天，結束 = 起始 + 1 個月 − 1 天
        var (nAnchorYear, nAnchorMonth) = anchor.Value;
        var dtPrevEnd = rows[anchor.Value].dtEndDate.Date;
        var cur = new DateTime(nAnchorYear, nAnchorMonth, 1);
        var dtTargetYM = new DateTime(nYear, nMonth, 1);
        var dtStart = dtPrevEnd; // 迴圈至少跑一次後為正確值
        var dtEnd = dtPrevEnd;
        while (cur < dtTargetYM)
        {
            cur = cur.AddMonths(1);
            dtStart = dtPrevEnd.AddDays(1);
            dtEnd = dtStart.AddMonths(1).AddDays(-1);
            dtPrevEnd = dtEnd;
        }
        return MakeRange(nYear, nMonth, dtStart, dtEnd, isCustomized: false);
    }

    /// <summary>
    /// 今天所屬期別。掃描前後數期，取「起始 ≤ 今天 &lt; 訖」且起始最晚者（重疊時取後開始的期）；
    /// 空窗落點無任何期涵蓋時，退回今天年月對應期別。
    /// </summary>
    public static BillingPeriodRange ResolveCurrentPeriod(
        DateTime dtToday, IReadOnlyDictionary<(int, int), BillingPeriodModel> rows)
    {
        var dtDay = dtToday.Date;
        BillingPeriodRange? best = null;
        for (var nOffset = -3; nOffset <= 1; nOffset++)
        {
            var ym = new DateTime(dtDay.Year, dtDay.Month, 1).AddMonths(nOffset);
            var p = ResolvePeriod(ym.Year, ym.Month, rows);
            if (p.dtStart <= dtDay && dtDay < p.dtEndExclusive && (best == null || p.dtStart > best.dtStart))
                best = p;
        }
        return best ?? ResolvePeriod(dtDay.Year, dtDay.Month, rows);
    }

    private static BillingPeriodRange MakeRange(
        int nYear, int nMonth, DateTime dtStartDate, DateTime dtEndDateInclusive, bool isCustomized)
    {
        var range = new BillingPeriodRange
        {
            nYear = nYear,
            nMonth = nMonth,
            dtStart = dtStartDate,
            dtEndExclusive = dtEndDateInclusive.AddDays(1),
            isCustomized = isCustomized,
        };
        range.szLabel = BuildLabel(range);
        return range;
    }

    /// <summary>
    /// 月 bucket 顯示標籤（報表/Excel 共用）：
    /// 自然月 → yyyy-MM（零視覺變化）；非自然月 → yyyy-MM-dd~MM-dd（跨年右端帶年份）。
    /// </summary>
    public static string BuildLabel(BillingPeriodRange p)
    {
        var ci = CultureInfo.InvariantCulture;
        if (p.isNaturalMonth)
            return new DateTime(p.nYear, p.nMonth, 1).ToString("yyyy-MM", ci);
        var dtEnd = p.dtEndInclusive;
        return p.dtStart.Year == dtEnd.Year
            ? $"{p.dtStart.ToString("yyyy-MM-dd", ci)}~{dtEnd.ToString("MM-dd", ci)}"
            : $"{p.dtStart.ToString("yyyy-MM-dd", ci)}~{dtEnd.ToString("yyyy-MM-dd", ci)}";
    }
}
