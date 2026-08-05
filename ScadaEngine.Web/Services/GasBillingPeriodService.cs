using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 氣費月結週期（期別）— 用氣報表 / 氣費報表 / EMS 氣表卡的唯一期界來源。
/// 與電費（<see cref="BillingPeriodService"/>）、水費（<see cref="WaterBillingPeriodService"/>）期別**完全獨立**。
///
/// ⚠️ **本服務與電/水兩版結構不再全等** — 多了「期別略過（IsSkipped）」機制，
///    用來支援供氣事業常見的**兩月一期**抄表週期。
///    日後若有人「照水版對稱修正」而移除 skipped 相關分支，兩月一期會直接失效，請勿這麼做。
///
/// 期別 M 的解析分兩層：
///   ① **nominal（名目）**：完全等同水費版的推導 —
///      有非 skipped 自訂 row → 直接採用；否則起始 = 前一期結束 +1 天（往前追溯至最近一筆非 skipped 自訂 row
///      逐期級聯），結束 = 起始 + 1 個月 − 1 天；完全無自訂 row 時 = 自然月。
///      **skipped 的 row 在這一層視同不存在**（其 StartDate/EndDate 僅為刪除當下的存檔，不參與級聯）。
///   ② **effective（生效）**：skipped 的期別不存在（回 null）；存在的期別會**吸收**相鄰的 skipped 期日數 —
///      向後吸收同年緊接其後的連續 skipped 期；若本期之前（同年）所有期皆 skipped，則向前吸收至該年 1 月起始。
///      → 「刪除某期 = 其日數併入前一期」；刪掉該年第一期時改由下一期向前吸收
///        （不併進去年 12 月期 — 期別的年歸屬是「起始所在月份的年」，併過去會竄改去年帳單）。
///
/// 因此「刪除 / 復原」都只是單一 row 的寫入或刪除，**不動鄰期**，完全可逆。
/// 空窗/重疊為使用者自己的選擇 — 僅警告不阻擋；唯一硬性驗證為 結束 ≥ 起始。
/// </summary>
public class GasBillingPeriodService
{
    private readonly ILogger<GasBillingPeriodService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    // 全自訂 row 快取 — 表小、讀多寫少；static 跨 Scoped 實例共用，寫入時失效。
    // ⚠️ 這個欄位刻意與 BillingPeriodService / WaterBillingPeriodService 的快取分開宣告（不抽共用基底類別）：
    //    static 欄位會被基底類別的所有子類共享，一旦共用會造成「改氣費期別污染電/水費期別」，
    //    症狀是偶爾抓到對方的期別，極難除錯。
    private static volatile Dictionary<(int nYear, int nMonth), GasBillingPeriodModel>? _cachedRows;

    public GasBillingPeriodService(ILogger<GasBillingPeriodService> logger, DatabaseConfigService configService)
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

    private async Task<Dictionary<(int, int), GasBillingPeriodModel>> GetRowsAsync()
    {
        var cached = _cachedRows;
        if (cached != null) return cached;

        const string szSql = @"
            SELECT PeriodYear AS nPeriodYear, PeriodMonth AS nPeriodMonth,
                   StartDate AS dtStartDate, EndDate AS dtEndDate,
                   IsSkipped AS isSkipped, UpdatedAt AS dtUpdatedAt
            FROM   GasBillingPeriods";
        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<GasBillingPeriodModel>(szSql);
        var dict = rows.ToDictionary(r => (r.nPeriodYear, r.nPeriodMonth));
        _cachedRows = dict;
        return dict;
    }

    /// <summary>
    /// 取得單一期別的生效解析結果。該期已被刪除（skipped）時，回傳「吸收它的那一期」
    /// —— 呼叫端（EMS 月檢視等）以年月定位時仍能拿到一段涵蓋該月的期界。全年皆 skipped 時回 null。
    /// </summary>
    public async Task<BillingPeriodRange?> GetPeriodAsync(int nYear, int nMonth)
    {
        var rows = await GetRowsAsync();
        return ResolveContaining(nYear, nMonth, rows);
    }

    /// <summary>
    /// 取得期別區間 [fromYM, toYM]（含頭尾）每期一對 [起, 訖) 邊界 — 用氣報表月粒度 bucket 來源。
    /// **已刪除（skipped）的期別不產生 bucket** — 一年兩月一期時只會回 6 個 bucket。
    /// dtFromYM / dtToYM 只取年月，日時分忽略。
    /// </summary>
    public async Task<List<BillingPeriodRange>> GetPeriodRangesAsync(DateTime dtFromYM, DateTime dtToYM)
    {
        var rows = await GetRowsAsync();
        return BuildPeriodRanges(dtFromYM, dtToYM, rows);
    }

    /// <summary>設定頁用：該年「實際存在」的期別（skipped 不在其中）+ 相鄰期空窗/重疊天數，另回已刪除清單供復原</summary>
    public async Task<(List<(BillingPeriodRange period, int nGapDays)> periods, List<BillingPeriodRange> skipped)>
        GetYearAsync(int nYear)
    {
        var rows = await GetRowsAsync();
        return BuildYear(nYear, rows);
    }

    /// <summary>
    /// 今天所屬期別 — 用氣報表日粒度預設起訖、EMS 氣費卡「本期」用。
    /// 掃描前後數期（skipped 期自動跳過），取「起始 ≤ 今天 &lt; 訖」且起始最晚者（重疊時取後開始的期）；
    /// 空窗落點無任何期涵蓋時，退回今天年月所屬期別。
    /// </summary>
    public async Task<BillingPeriodRange> GetCurrentPeriodAsync(DateTime dtToday)
    {
        var rows = await GetRowsAsync();
        var dtDay = dtToday.Date;
        BillingPeriodRange? best = null;
        for (var nOffset = -3; nOffset <= 1; nOffset++)
        {
            var ym = new DateTime(dtDay.Year, dtDay.Month, 1).AddMonths(nOffset);
            var p = ResolveEffective(ym.Year, ym.Month, rows);
            if (p == null) continue;
            if (p.dtStart <= dtDay && dtDay < p.dtEndExclusive && (best == null || p.dtStart > best.dtStart))
                best = p;
        }
        return best
            ?? ResolveContaining(dtDay.Year, dtDay.Month, rows)
            ?? MakeNaturalMonth(dtDay.Year, dtDay.Month);
    }

    // ---------- 寫入 ----------

    /// <summary>
    /// UPSERT 自訂期別（一律寫 IsSkipped=0 — 對已刪除的期別存檔等同復原）。
    /// 硬性驗證：結束 ≥ 起始（空窗/重疊僅警告，呼叫端顯示）。
    /// </summary>
    public async Task SaveAsync(int nYear, int nMonth, DateTime dtStartDate, DateTime dtEndDate)
    {
        if (nMonth < 1 || nMonth > 12)
            throw new ArgumentException($"期別月份必須為 1–12：{nMonth}");
        if (dtEndDate.Date < dtStartDate.Date)
            throw new ArgumentException("結束日期不可早於起始日期");

        const string szSql = @"
            MERGE GasBillingPeriods AS t
            USING (SELECT @nYear AS PeriodYear, @nMonth AS PeriodMonth) AS s
               ON t.PeriodYear = s.PeriodYear AND t.PeriodMonth = s.PeriodMonth
            WHEN MATCHED THEN
                UPDATE SET StartDate = @dtStart, EndDate = @dtEnd, IsSkipped = 0, UpdatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (PeriodYear, PeriodMonth, StartDate, EndDate, IsSkipped, UpdatedAt)
                VALUES (@nYear, @nMonth, @dtStart, @dtEnd, 0, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new
        {
            nYear,
            nMonth,
            dtStart = dtStartDate.Date,
            dtEnd = dtEndDate.Date
        });
        _cachedRows = null;
        _logger.LogInformation("氣費月結週期已更新 {Year}-{Month:00}: {Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd}",
            nYear, nMonth, dtStartDate, dtEndDate);
    }

    /// <summary>刪除自訂 row（還原為推導預設）。已刪除（skipped）的期別不受影響 — 復原走 UnskipAsync。</summary>
    public async Task<bool> DeleteAsync(int nYear, int nMonth)
    {
        const string szSql = @"
            DELETE FROM GasBillingPeriods
            WHERE PeriodYear = @nYear AND PeriodMonth = @nMonth AND IsSkipped = 0";
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(szSql, new { nYear, nMonth });
        _cachedRows = null;
        return nAffected > 0;
    }

    /// <summary>
    /// 「刪除此期」— 寫 IsSkipped=1，該期自報表與設定頁消失，其日數由前一期吸收
    /// （該年第一期被刪時改由下一期向前吸收）。不動任何鄰期 row，可用 UnskipAsync 完整還原。
    /// 該年只剩一期時拒絕刪除（否則整年日數無人涵蓋）。
    /// </summary>
    public async Task SkipAsync(int nYear, int nMonth)
    {
        if (nMonth < 1 || nMonth > 12)
            throw new ArgumentException($"期別月份必須為 1–12：{nMonth}");

        var rows = await GetRowsAsync();
        if (IsSkipped(nYear, nMonth, rows)) return;   // 已刪除 → 冪等

        var nExisting = Enumerable.Range(1, 12).Count(m => !IsSkipped(nYear, m, rows));
        if (nExisting <= 1)
            throw new ArgumentException("該年僅剩一期，不可再刪除");

        // 存下刪除當下的名目起訖，供設定頁「已刪除」摺疊區顯示（不參與級聯推導）
        var nominal = ResolveNominal(nYear, nMonth, rows);

        const string szSql = @"
            MERGE GasBillingPeriods AS t
            USING (SELECT @nYear AS PeriodYear, @nMonth AS PeriodMonth) AS s
               ON t.PeriodYear = s.PeriodYear AND t.PeriodMonth = s.PeriodMonth
            WHEN MATCHED THEN
                UPDATE SET StartDate = @dtStart, EndDate = @dtEnd, IsSkipped = 1, UpdatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (PeriodYear, PeriodMonth, StartDate, EndDate, IsSkipped, UpdatedAt)
                VALUES (@nYear, @nMonth, @dtStart, @dtEnd, 1, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new
        {
            nYear,
            nMonth,
            dtStart = nominal.dtStart,
            dtEnd = nominal.dtEndInclusive
        });
        _cachedRows = null;
        _logger.LogInformation("氣費月結週期 {Year}-{Month:00} 已刪除（日數併入相鄰期別）", nYear, nMonth);
    }

    /// <summary>「復原此期」— 刪掉 IsSkipped row，該期回到推導預設，鄰期自動收回被吸收的日數。</summary>
    public async Task<bool> UnskipAsync(int nYear, int nMonth)
    {
        const string szSql = @"
            DELETE FROM GasBillingPeriods
            WHERE PeriodYear = @nYear AND PeriodMonth = @nMonth AND IsSkipped = 1";
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(szSql, new { nYear, nMonth });
        _cachedRows = null;
        if (nAffected > 0)
            _logger.LogInformation("氣費月結週期 {Year}-{Month:00} 已復原", nYear, nMonth);
        return nAffected > 0;
    }

    // ---------- 推導核心（static 純邏輯，可單元測試不觸 DB） ----------

    /// <summary>該期是否已被刪除（skipped）</summary>
    public static bool IsSkipped(int nYear, int nMonth, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows) =>
        rows.TryGetValue((nYear, nMonth), out var row) && row.isSkipped;

    /// <summary>
    /// 名目期界 — 完全等同水費版推導；**skipped 的 row 在這一層視同不存在**。
    /// </summary>
    public static BillingPeriodRange ResolveNominal(
        int nYear, int nMonth, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        if (rows.TryGetValue((nYear, nMonth), out var row) && !row.isSkipped)
            return MakeRange(nYear, nMonth, row.dtStartDate.Date, row.dtEndDate.Date, isCustomized: true);

        // 最近一筆更早的「非 skipped」自訂 row（(年, 月) tuple 字典序即時間序）
        var target = (nYear, nMonth);
        (int, int)? anchor = null;
        foreach (var kv in rows)
        {
            if (kv.Value.isSkipped) continue;
            var key = kv.Key;
            if (key.CompareTo(target) >= 0) continue;
            if (anchor == null || key.CompareTo(anchor.Value) > 0) anchor = key;
        }

        if (anchor == null)
            return MakeNaturalMonth(nYear, nMonth);

        // 從最近自訂 row 逐期級聯：起始 = 前期結束 +1 天，結束 = 起始 + 1 個月 − 1 天
        var (nAnchorYear, nAnchorMonth) = anchor.Value;
        var dtPrevEnd = rows[anchor.Value].dtEndDate.Date;
        var cur = new DateTime(nAnchorYear, nAnchorMonth, 1);
        var dtTargetYM = new DateTime(nYear, nMonth, 1);
        var dtStart = dtPrevEnd; // 迴圈至少跑一次後為正確值
        var dtEnd = dtPrevEnd;
        while (cur < dtTargetYM)
        {
            // anchor 是「最近一筆更早的非 skipped row」→ 中間各月必無自訂 row，一律逐期級聯推導
            cur = cur.AddMonths(1);
            dtStart = dtPrevEnd.AddDays(1);
            dtEnd = dtStart.AddMonths(1).AddDays(-1);
            dtPrevEnd = dtEnd;
        }
        return MakeRange(nYear, nMonth, dtStart, dtEnd, isCustomized: false);
    }

    /// <summary>
    /// 生效期界 — skipped 回 null；存在的期別吸收相鄰 skipped 期的日數（見類別註解 ②）。
    /// </summary>
    public static BillingPeriodRange? ResolveEffective(
        int nYear, int nMonth, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        if (IsSkipped(nYear, nMonth, rows)) return null;

        var nominal = ResolveNominal(nYear, nMonth, rows);
        var dtStart = nominal.dtStart;
        var dtEndInclusive = nominal.dtEndInclusive;

        // 向後吸收：同年緊接其後的連續 skipped 期（不跨年 — 跨年那期歸下一年處理）
        for (var k = nMonth + 1; k <= 12 && IsSkipped(nYear, k, rows); k++)
            dtEndInclusive = ResolveNominal(nYear, k, rows).dtEndInclusive;

        // 向前吸收：本期之前同年所有期皆 skipped（本期是該年第一個存在的期）→ 起始延伸至該年 1 月名目起始
        if (nMonth > 1 && Enumerable.Range(1, nMonth - 1).All(j => IsSkipped(nYear, j, rows)))
            dtStart = ResolveNominal(nYear, 1, rows).dtStart;

        return MakeRange(nYear, nMonth, dtStart, dtEndInclusive, nominal.isCustomized);
    }

    /// <summary>(年,月) 所落入的生效期別 — 該月自身被刪除時回傳吸收它的那一期；全年皆刪回 null。</summary>
    public static BillingPeriodRange? ResolveContaining(
        int nYear, int nMonth, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        var self = ResolveEffective(nYear, nMonth, rows);
        if (self != null) return self;

        // 先往前找同年最近的存在期（它會向後吸收本期）
        for (var m = nMonth - 1; m >= 1; m--)
        {
            var p = ResolveEffective(nYear, m, rows);
            if (p != null) return p;
        }
        // 同年前面全被刪 → 往後找（該期會向前吸收）
        for (var m = nMonth + 1; m <= 12; m++)
        {
            var p = ResolveEffective(nYear, m, rows);
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>[fromYM, toYM] 每個「存在」的期別一段 [起, 訖)（純邏輯版）</summary>
    public static List<BillingPeriodRange> BuildPeriodRanges(
        DateTime dtFromYM, DateTime dtToYM, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        var list = new List<BillingPeriodRange>();
        var t = new DateTime(dtFromYM.Year, dtFromYM.Month, 1);
        var end = new DateTime(dtToYM.Year, dtToYM.Month, 1);
        while (t <= end)
        {
            var p = ResolveEffective(t.Year, t.Month, rows);
            if (p != null) list.Add(p);
            t = t.AddMonths(1);
        }
        return list;
    }

    /// <summary>設定頁一年清單（純邏輯版）：存在的期別 + 空窗/重疊天數，另回已刪除期別（名目起訖）</summary>
    public static (List<(BillingPeriodRange period, int nGapDays)> periods, List<BillingPeriodRange> skipped)
        BuildYear(int nYear, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        var periods = new List<(BillingPeriodRange, int)>(12);
        var skipped = new List<BillingPeriodRange>();

        for (var m = 1; m <= 12; m++)
        {
            var period = ResolveEffective(nYear, m, rows);
            if (period == null)
            {
                skipped.Add(ResolveNominal(nYear, m, rows));
                continue;
            }
            // 空窗（+N）/ 重疊（−N）天數：本期起始 vs 前一個「存在」期別的結束隔日
            var prev = FindPrevExisting(nYear, m, rows);
            var nGapDays = prev == null ? 0 : (int)(period.dtStart - prev.dtEndExclusive).TotalDays;
            periods.Add((period, nGapDays));
        }
        return (periods, skipped);
    }

    /// <summary>往前找最近一個「存在」的期別（最多回溯 24 個月）</summary>
    private static BillingPeriodRange? FindPrevExisting(
        int nYear, int nMonth, IReadOnlyDictionary<(int, int), GasBillingPeriodModel> rows)
    {
        var ym = new DateTime(nYear, nMonth, 1);
        for (var i = 1; i <= 24; i++)
        {
            var t = ym.AddMonths(-i);
            var p = ResolveEffective(t.Year, t.Month, rows);
            if (p != null) return p;
        }
        return null;
    }

    private static BillingPeriodRange MakeNaturalMonth(int nYear, int nMonth)
    {
        var dtNatural = new DateTime(nYear, nMonth, 1);
        return MakeRange(nYear, nMonth, dtNatural, dtNatural.AddMonths(1).AddDays(-1), isCustomized: false);
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
    /// 格式與電費/水費版一致 — 兩月一期時自然落在非自然月分支，如 2026-01-01~02-28。
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
