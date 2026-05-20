using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 葉子層 hourly 預聚合表的資料存取。
/// 提供 UPSERT、查既有列、查葉子清單（從 EnergyCircuit）等基本操作。
/// </summary>
public class EnergyLeafHourlyRepository
{
    private readonly ILogger<EnergyLeafHourlyRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public EnergyLeafHourlyRepository(
        ILogger<EnergyLeafHourlyRepository> logger,
        DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    private async Task EnsureConnectionStringAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        await EnsureConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>葉子節點 (綁 SID + 可選 MaxKwh) — 從 EnergyCircuit 取出。</summary>
    public record LeafInfo(string szSID, double? dMaxKwh, string szName);

    /// <summary>取得 EnergyCircuit 內所有綁 SID 的葉子（含 MaxKwh / Name）。</summary>
    public async Task<List<LeafInfo>> GetAllLeafSidsWithMaxKwhAsync()
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string SID, double? MaxKwh, string Name)>(@"
            SELECT SID, MaxKwh, Name
            FROM   EnergyCircuit
            WHERE  SID IS NOT NULL AND LEN(SID) > 0");
        return rows.Select(r => new LeafInfo(r.SID, r.MaxKwh, r.Name)).ToList();
    }

    /// <summary>查指定 SID 在 [from, to) 區間內已存在的 HourStart 集合（給 catch-up / backfill 跳過已聚合用）</summary>
    public async Task<HashSet<DateTime>> GetExistingHoursAsync(string szSid, DateTime dtFrom, DateTime dtTo)
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<DateTime>(@"
            SELECT HourStart
            FROM   EnergyLeafHourly
            WHERE  SID = @SID AND HourStart >= @From AND HourStart < @To",
            new { SID = szSid, From = dtFrom, To = dtTo });
        return new HashSet<DateTime>(rows);
    }

    /// <summary>UPSERT 一筆聚合資料（同 (SID, HourStart) 已存在則覆寫）。</summary>
    public async Task UpsertAsync(EnergyLeafHourlyModel model)
    {
        using var conn = await OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            MERGE EnergyLeafHourly WITH (HOLDLOCK) AS tgt
            USING (SELECT @SID AS SID, @HourStart AS HourStart) AS src
               ON tgt.SID = src.SID AND tgt.HourStart = src.HourStart
            WHEN MATCHED THEN
                UPDATE SET DeltaKwh = @DeltaKwh,
                           Quality = @Quality,
                           IsRolledOver = @IsRolledOver,
                           CreatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (SID, HourStart, DeltaKwh, Quality, IsRolledOver, CreatedAt)
                VALUES (@SID, @HourStart, @DeltaKwh, @Quality, @IsRolledOver, GETDATE());",
            new
            {
                SID = model.szSID,
                HourStart = model.dtHourStart,
                DeltaKwh = model.dDeltaKwh,
                Quality = (byte)model.nQuality,
                IsRolledOver = model.isRolledOver
            });
    }

    /// <summary>批次 UPSERT（同一連線重用，給 backfill 大量寫入用）</summary>
    public async Task UpsertManyAsync(IEnumerable<EnergyLeafHourlyModel> models)
    {
        var list = models.ToList();
        if (list.Count == 0) return;

        using var conn = await OpenConnectionAsync();
        const string szSql = @"
            MERGE EnergyLeafHourly WITH (HOLDLOCK) AS tgt
            USING (SELECT @SID AS SID, @HourStart AS HourStart) AS src
               ON tgt.SID = src.SID AND tgt.HourStart = src.HourStart
            WHEN MATCHED THEN
                UPDATE SET DeltaKwh = @DeltaKwh,
                           Quality = @Quality,
                           IsRolledOver = @IsRolledOver,
                           CreatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (SID, HourStart, DeltaKwh, Quality, IsRolledOver, CreatedAt)
                VALUES (@SID, @HourStart, @DeltaKwh, @Quality, @IsRolledOver, GETDATE());";

        foreach (var m in list)
        {
            await conn.ExecuteAsync(szSql, new
            {
                SID = m.szSID,
                HourStart = m.dtHourStart,
                DeltaKwh = m.dDeltaKwh,
                Quality = (byte)m.nQuality,
                IsRolledOver = m.isRolledOver
            });
        }
    }
}
