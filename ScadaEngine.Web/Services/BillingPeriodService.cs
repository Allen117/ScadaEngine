using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 月結週期（期別）— 全系統月粒度報表的唯一期界來源。
/// 期別 M 的解析規則（設計決策見 docs/功能說明書_能源管理.md §月結週期）：
///   1. BillingPeriods 有 row → 直接採用（使用者自訂）
///   2. 無 row → 起始 = 前一期結束 +1 天（往前追溯至最近一筆自訂 row 逐期級聯），
///      結束 = 起始 + 1 個月 − 1 天；完全沒有任何自訂 row 時 = 自然月（1 日～最後一日）
/// 空窗/重疊為使用者自己的選擇 — 僅警告不阻擋；唯一硬性驗證為 結束 ≥ 起始。
/// 推導值不落 DB（避免污染未來月份），只影響顯示與查詢時的期界計算。
/// </summary>
public class BillingPeriodService
{
    private readonly ILogger<BillingPeriodService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    // 全自訂 row 快取 — 表小、讀多寫少；static 跨 Scoped 實例共用，寫入時失效
    private static volatile Dictionary<(int nYear, int nMonth), BillingPeriodModel>? _cachedRows;

    public BillingPeriodService(ILogger<BillingPeriodService> logger, DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ---------- 讀取（含快取） ----------

    private async Task<Dictionary<(int, int), BillingPeriodModel>> GetRowsAsync()
    {
        var cached = _cachedRows;
        if (cached != null) return cached;

        const string szSql = @"
            SELECT PeriodYear AS nPeriodYear, PeriodMonth AS nPeriodMonth,
                   StartDate AS dtStartDate, EndDate AS dtEndDate, UpdatedAt AS dtUpdatedAt
            FROM   BillingPeriods";
        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<BillingPeriodModel>(szSql);
        var dict = rows.ToDictionary(r => (r.nPeriodYear, r.nPeriodMonth));
        _cachedRows = dict;
        return dict;
    }

    /// <summary>取得單一期別的解析結果（自訂或推導）</summary>
    public async Task<BillingPeriodRange> GetPeriodAsync(int nYear, int nMonth)
    {
        var rows = await GetRowsAsync();
        return ResolvePeriod(nYear, nMonth, rows);
    }

    /// <summary>
    /// 取得期別區間 [fromYM, toYM]（含頭尾）每期一對 [起, 訖) 邊界 — 報表月粒度 bucket 來源。
    /// dtFromYM / dtToYM 只取年月，日時分忽略。
    /// </summary>
    public async Task<List<BillingPeriodRange>> GetPeriodRangesAsync(DateTime dtFromYM, DateTime dtToYM)
    {
        var rows = await GetRowsAsync();
        var list = new List<BillingPeriodRange>();
        var t = new DateTime(dtFromYM.Year, dtFromYM.Month, 1);
        var end = new DateTime(dtToYM.Year, dtToYM.Month, 1);
        while (t <= end)
        {
            list.Add(ResolvePeriod(t.Year, t.Month, rows));
            t = t.AddMonths(1);
        }
        return list;
    }

    /// <summary>設定頁用：一年 12 期 + 相鄰期空窗/重疊天數</summary>
    public async Task<List<(BillingPeriodRange period, int nGapDays)>> GetYearAsync(int nYear)
    {
        var rows = await GetRowsAsync();
        var list = new List<(BillingPeriodRange, int)>(12);
        for (var m = 1; m <= 12; m++)
        {
            var period = ResolvePeriod(nYear, m, rows);
            var prevPeriod = m == 1 ? ResolvePeriod(nYear - 1, 12, rows) : ResolvePeriod(nYear, m - 1, rows);
            // 空窗（+N）/ 重疊（−N）天數：本期起始 vs 上期結束隔日
            var nGapDays = (int)(period.dtStart - prevPeriod.dtEndExclusive).TotalDays;
            list.Add((period, nGapDays));
        }
        return list;
    }

    /// <summary>
    /// 今天所屬期別 — 用電報表日粒度預設起訖用。
    /// 掃描前後數期，取「起始 ≤ 今天 &lt; 訖」且起始最晚者（重疊時取後開始的期）；
    /// 空窗落點無任何期涵蓋時，退回今天年月對應期別。
    /// </summary>
    public async Task<BillingPeriodRange> GetCurrentPeriodAsync(DateTime dtToday)
    {
        var rows = await GetRowsAsync();
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

    // ---------- 寫入 ----------

    /// <summary>UPSERT 自訂期別。硬性驗證：結束 ≥ 起始（空窗/重疊僅警告，呼叫端顯示）。</summary>
    public async Task SaveAsync(int nYear, int nMonth, DateTime dtStartDate, DateTime dtEndDate)
    {
        if (nMonth < 1 || nMonth > 12)
            throw new ArgumentException($"期別月份必須為 1–12：{nMonth}");
        if (dtEndDate.Date < dtStartDate.Date)
            throw new ArgumentException("結束日期不可早於起始日期");

        const string szSql = @"
            MERGE BillingPeriods AS t
            USING (SELECT @nYear AS PeriodYear, @nMonth AS PeriodMonth) AS s
               ON t.PeriodYear = s.PeriodYear AND t.PeriodMonth = s.PeriodMonth
            WHEN MATCHED THEN
                UPDATE SET StartDate = @dtStart, EndDate = @dtEnd, UpdatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (PeriodYear, PeriodMonth, StartDate, EndDate, UpdatedAt)
                VALUES (@nYear, @nMonth, @dtStart, @dtEnd, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new
        {
            nYear,
            nMonth,
            dtStart = dtStartDate.Date,
            dtEnd = dtEndDate.Date
        });
        _cachedRows = null;
        _logger.LogInformation("月結週期已更新 {Year}-{Month:00}: {Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd}",
            nYear, nMonth, dtStartDate, dtEndDate);
    }

    /// <summary>刪除自訂 row（還原為推導預設）</summary>
    public async Task<bool> DeleteAsync(int nYear, int nMonth)
    {
        const string szSql = "DELETE FROM BillingPeriods WHERE PeriodYear = @nYear AND PeriodMonth = @nMonth";
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(szSql, new { nYear, nMonth });
        _cachedRows = null;
        return nAffected > 0;
    }

    // ---------- 推導核心 ----------

    private static BillingPeriodRange ResolvePeriod(
        int nYear, int nMonth, Dictionary<(int, int), BillingPeriodModel> rows)
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
