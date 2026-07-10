using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 國定假日設定 — Holidays 表（date PK 窄表）讀寫與區間查詢。
/// 標註日在 TOU 計價時以 sun_offday（週日及離峰日）費率落段（見 ElectricityCostService.ResolveDayType）。
/// 儲存採「整年批次覆蓋」：刪該年 + 重插，天然冪等。
/// 表小、讀多寫少 → 全表 static 快取跨 Scoped 實例共用，寫入時失效。
/// </summary>
public class HolidayService
{
    private readonly ILogger<HolidayService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    // 全表快取（date 集合）— 計價端逐時查 DayType 不能每次打 DB
    private static volatile HashSet<DateTime>? _cachedDates;

    public HolidayService(ILogger<HolidayService> logger, DatabaseConfigService configService)
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

    /// <summary>取得全部標註日期（快取）</summary>
    public async Task<HashSet<DateTime>> GetAllAsync()
    {
        var cached = _cachedDates;
        if (cached != null) return cached;

        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<DateTime>("SELECT HolidayDate FROM Holidays");
        var set = rows.Select(d => d.Date).ToHashSet();
        _cachedDates = set;
        return set;
    }

    /// <summary>取得指定年度的標註日期（設定頁年曆用）</summary>
    public async Task<List<DateTime>> GetYearAsync(int nYear)
    {
        var all = await GetAllAsync();
        return all.Where(d => d.Year == nYear).OrderBy(d => d).ToList();
    }

    /// <summary>指定日期是否為標註假日（計價端 DayType 判定用，走快取）</summary>
    public async Task<bool> IsHolidayAsync(DateTime dtDate)
    {
        var all = await GetAllAsync();
        return all.Contains(dtDate.Date);
    }

    /// <summary>
    /// 整年批次覆蓋儲存：刪該年所有列 + 重插傳入集合（僅接受該年度日期，其他年份忽略）。
    /// </summary>
    public async Task SaveYearAsync(int nYear, IEnumerable<DateTime> dates)
    {
        if (nYear < 2000 || nYear > 2100)
            throw new ArgumentException($"年份超出範圍：{nYear}");

        var list = dates.Select(d => d.Date)
            .Where(d => d.Year == nYear)
            .Distinct()
            .ToList();

        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(
                "DELETE FROM Holidays WHERE HolidayDate >= @dtFrom AND HolidayDate < @dtTo",
                new { dtFrom = new DateTime(nYear, 1, 1), dtTo = new DateTime(nYear + 1, 1, 1) }, tran);

            foreach (var d in list)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO Holidays (HolidayDate, CreatedAt) VALUES (@d, GETDATE())",
                    new { d }, tran);
            }
            tran.Commit();
        }
        catch
        {
            tran.Rollback();
            throw;
        }

        _cachedDates = null;
        _logger.LogInformation("國定假日設定已更新 {Year} 年：{Count} 天", nYear, list.Count);
    }
}
