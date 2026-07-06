using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 能源申報 — 申報報表設定 CRUD + 合併查詢。
/// 查詢不自建聚合邏輯：kWh 委派 <see cref="EnergyReportService"/>（HistoryData 邊界相減，含溢位/Sign/staleness），
/// RT·h 委派 <see cref="RefrigerationTonReportService"/>（WaterLeafHourly 預聚合），
/// 兩邊 BuildBoundaries/BuildLabels 邏輯相同 → bucket 依 index 對齊合併，
/// 確保申報數字與各自單獨報表查詢結果完全一致。
/// </summary>
public class EnergyDeclarationService
{
    private readonly ILogger<EnergyDeclarationService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly EnergyReportService _energyReportService;
    private readonly RefrigerationTonReportService _rtReportService;
    private string _szConnectionString = string.Empty;

    public EnergyDeclarationService(
        ILogger<EnergyDeclarationService> logger,
        DatabaseConfigService configService,
        EnergyReportService energyReportService,
        RefrigerationTonReportService rtReportService)
    {
        _logger = logger;
        _configService = configService;
        _energyReportService = energyReportService;
        _rtReportService = rtReportService;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ---------- 設定 CRUD ----------

    public async Task<List<EnergyDeclarationModel>> GetAllAsync()
    {
        const string szSql = @"
            SELECT Id AS nId, Name AS szName,
                   EnergyCircuitId AS nEnergyCircuitId, WaterCircuitId AS nWaterCircuitId,
                   SortOrder AS nSortOrder, Description AS szDescription,
                   CreatedAt AS dtCreatedAt, UpdatedAt AS dtUpdatedAt
            FROM   EnergyDeclarationReport
            ORDER BY SortOrder, Id";
        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<EnergyDeclarationModel>(szSql);
        return rows.ToList();
    }

    public async Task<EnergyDeclarationModel?> GetByIdAsync(int nId)
    {
        const string szSql = @"
            SELECT Id AS nId, Name AS szName,
                   EnergyCircuitId AS nEnergyCircuitId, WaterCircuitId AS nWaterCircuitId,
                   SortOrder AS nSortOrder, Description AS szDescription,
                   CreatedAt AS dtCreatedAt, UpdatedAt AS dtUpdatedAt
            FROM   EnergyDeclarationReport
            WHERE  Id = @nId";
        using var conn = await GetConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<EnergyDeclarationModel>(szSql, new { nId });
    }

    public async Task<int> CreateAsync(EnergyDeclarationModel model)
    {
        const string szSql = @"
            INSERT INTO EnergyDeclarationReport (Name, EnergyCircuitId, WaterCircuitId, SortOrder, Description)
            OUTPUT INSERTED.Id
            VALUES (@szName, @nEnergyCircuitId, @nWaterCircuitId,
                    ISNULL((SELECT MAX(SortOrder) + 1 FROM EnergyDeclarationReport), 0), @szDescription)";
        using var conn = await GetConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(szSql, new
        {
            model.szName,
            model.nEnergyCircuitId,
            model.nWaterCircuitId,
            model.szDescription
        });
    }

    public async Task<bool> UpdateAsync(int nId, string szName, int nEnergyCircuitId, int nWaterCircuitId, string? szDescription)
    {
        const string szSql = @"
            UPDATE EnergyDeclarationReport
            SET    Name = @szName, EnergyCircuitId = @nEnergyCircuitId, WaterCircuitId = @nWaterCircuitId,
                   Description = @szDescription, UpdatedAt = GETDATE()
            WHERE  Id = @nId";
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(szSql, new { nId, szName, nEnergyCircuitId, nWaterCircuitId, szDescription });
        return nAffected > 0;
    }

    public async Task<bool> DeleteAsync(int nId)
    {
        const string szSql = "DELETE FROM EnergyDeclarationReport WHERE Id = @nId";
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(szSql, new { nId });
        return nAffected > 0;
    }

    // ---------- 合併查詢 ----------

    /// <summary>
    /// 取得申報報表結果 — 頁面固定格式：指定年度 → 12 個曆月 bucket（每月 1 號 00:00 ~ 次月 1 號）。
    /// kWh 與 RT·h 皆走 GetCalendarMonthlyReportAsync（曆月切界，不走月結期別 —
    /// 月粒度報表已改由 BillingPeriodService 期別切界，申報格式固定曆月不受影響）。
    /// 依 bucket index 合併並計算每月效率 kWh/RTh（RT·h ≤ 0 時為 null）。
    /// 綁定的迴路若已被刪除，擲出 InvalidOperationException（訊息含來源別）。
    /// </summary>
    public async Task<EnergyDeclarationResult> GetDeclarationReportAsync(int nReportId, int nYear)
    {
        if (nYear < 2000 || nYear > 2100)
            throw new InvalidOperationException($"年度 {nYear} 超出範圍（2000~2100）");

        var report = await GetByIdAsync(nReportId);
        if (report == null)
            throw new InvalidOperationException($"申報報表 Id={nReportId} 不存在");

        EnergyReportResult kwhResult;
        try
        {
            kwhResult = await _energyReportService.GetCalendarMonthlyReportAsync(
                report.nEnergyCircuitId, nYear);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"申報報表「{report.szName}」綁定的用電迴路 Id={report.nEnergyCircuitId} 已不存在，請重新編輯設定");
        }

        RefrigerationTonReportResult rtResult;
        try
        {
            rtResult = await _rtReportService.GetCalendarMonthlyReportAsync(report.nWaterCircuitId, nYear);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"申報報表「{report.szName}」綁定的水系統迴路 Id={report.nWaterCircuitId} 已不存在，請重新編輯設定");
        }

        // 兩邊皆為 12 個曆月 → bucket 數必然一致；防禦性檢查以免日後演算法分岐
        if (kwhResult.buckets.Count != rtResult.buckets.Count)
        {
            _logger.LogError(
                "能源申報 bucket 數不一致 kWh={KwhCount} RT={RtCount} reportId={ReportId}",
                kwhResult.buckets.Count, rtResult.buckets.Count, nReportId);
            throw new InvalidOperationException("用電與冷凍噸報表時段數不一致，無法合併");
        }

        var result = new EnergyDeclarationResult
        {
            nReportId = report.nId,
            szReportName = report.szName,
            szEnergyCircuitName = kwhResult.szCircuitName,
            szWaterCircuitName = rtResult.szCircuitName,
            nYear = nYear,
            dtStart = kwhResult.dtStart,
            dtEnd = kwhResult.dtEnd,
            dTotalKwh = kwhResult.dTotalKwh,
            dTotalRtHour = rtResult.dTotalRtHour,
            isHasKwhWarning = kwhResult.isHasWarning,
            isHasRtWarning = rtResult.isHasWarning,
        };

        for (var i = 0; i < kwhResult.buckets.Count; i++)
        {
            var dKwh = kwhResult.buckets[i].dKwh;
            var dRtHour = rtResult.buckets[i].dRtHour;
            result.buckets.Add(new EnergyDeclarationBucket
            {
                dtBucketStart = kwhResult.buckets[i].dtBucketStart,
                szLabel = kwhResult.buckets[i].szLabel,
                dKwh = dKwh,
                dRtHour = dRtHour,
                dKwhPerRtHour = dRtHour > 0 ? Math.Round(dKwh / dRtHour, 3) : null,
                isKwhStale = kwhResult.buckets[i].isStale,
            });
        }

        result.dTotalKwhPerRtHour = result.dTotalRtHour > 0
            ? Math.Round(result.dTotalKwh / result.dTotalRtHour, 3)
            : null;
        return result;
    }
}
