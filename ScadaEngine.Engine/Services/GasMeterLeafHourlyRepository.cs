using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 氣表葉子層 hourly 預聚合表（GasMeterLeafHourly）的資料存取。
/// 提供 UPSERT、查既有列、查葉子清單（從 GasMeterCircuit）等基本操作。
/// </summary>
public class GasMeterLeafHourlyRepository
{
    private readonly ILogger<GasMeterLeafHourlyRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public GasMeterLeafHourlyRepository(
        ILogger<GasMeterLeafHourlyRepository> logger,
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

    /// <summary>葉子節點 (綁 SID + 可選 MaxVolume + UnitScale) — 從 GasMeterCircuit 取出。</summary>
    public record LeafInfo(string szSID, double? dMaxVolume, double dUnitScale, string szName);

    /// <summary>取得 GasMeterCircuit 內所有綁 SID 的葉子（含 MaxVolume / UnitScale / Name）。</summary>
    public async Task<List<LeafInfo>> GetAllLeafSidsAsync()
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string SID, double? MaxVolume, double UnitScale, string Name)>(@"
            SELECT SID, MaxVolume, UnitScale, Name
            FROM   GasMeterCircuit
            WHERE  SID IS NOT NULL AND LEN(SID) > 0");
        return rows.Select(r => new LeafInfo(r.SID, r.MaxVolume, r.UnitScale, r.Name)).ToList();
    }

    /// <summary>查指定 SID 在 [from, to) 區間內已存在的 HourStart 集合（給 catch-up 跳過已聚合用）</summary>
    public async Task<HashSet<DateTime>> GetExistingHoursAsync(string szSid, DateTime dtFrom, DateTime dtTo)
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<DateTime>(@"
            SELECT HourStart
            FROM   GasMeterLeafHourly
            WHERE  SID = @SID AND HourStart >= @From AND HourStart < @To",
            new { SID = szSid, From = dtFrom, To = dtTo });
        return new HashSet<DateTime>(rows);
    }

    /// <summary>UPSERT 一筆聚合資料（同 (SID, HourStart) 已存在則覆寫）。</summary>
    public async Task UpsertAsync(GasMeterLeafHourlyModel model)
    {
        using var conn = await OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            MERGE GasMeterLeafHourly WITH (HOLDLOCK) AS tgt
            USING (SELECT @SID AS SID, @HourStart AS HourStart) AS src
               ON tgt.SID = src.SID AND tgt.HourStart = src.HourStart
            WHEN MATCHED THEN
                UPDATE SET DeltaM3 = @DeltaM3,
                           Quality = @Quality,
                           IsRolledOver = @IsRolledOver,
                           CreatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (SID, HourStart, DeltaM3, Quality, IsRolledOver, CreatedAt)
                VALUES (@SID, @HourStart, @DeltaM3, @Quality, @IsRolledOver, GETDATE());",
            new
            {
                SID = model.szSID,
                HourStart = model.dtHourStart,
                DeltaM3 = model.dDeltaM3,
                Quality = (byte)model.nQuality,
                IsRolledOver = model.isRolledOver
            });
    }
}
