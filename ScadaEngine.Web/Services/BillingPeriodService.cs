using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 月結週期（期別）— 全系統月粒度報表的唯一期界來源。
/// 推導核心（ResolvePeriod / 級聯 / 自然月 fallback）已抽至 Common `BillingPeriodResolver`
/// 與 Engine 迴路用電虛擬點位（NRGP-）共用；本 service 保留 DB 讀寫與快取，推導委派 Resolver。
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
        return BillingPeriodResolver.ResolvePeriod(nYear, nMonth, rows);
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
            list.Add(BillingPeriodResolver.ResolvePeriod(t.Year, t.Month, rows));
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
            var period = BillingPeriodResolver.ResolvePeriod(nYear, m, rows);
            var prevPeriod = m == 1
                ? BillingPeriodResolver.ResolvePeriod(nYear - 1, 12, rows)
                : BillingPeriodResolver.ResolvePeriod(nYear, m - 1, rows);
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
        return BillingPeriodResolver.ResolveCurrentPeriod(dtToday, rows);
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

    /// <summary>月 bucket 顯示標籤 — 委派 Common Resolver（多處 caller 沿用此入口，簽名不變）</summary>
    public static string BuildLabel(BillingPeriodRange p) => BillingPeriodResolver.BuildLabel(p);
}
