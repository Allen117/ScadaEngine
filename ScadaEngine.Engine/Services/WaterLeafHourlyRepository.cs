using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 冷凍噸 — 葉子層 hourly 預聚合表的資料存取。
/// 提供 UPSERT、查既有列、查葉子清單（從 WaterCircuit）等基本操作。
/// 對標 <see cref="EnergyLeafHourlyRepository"/>，差異：無 MaxKwh，多 SampleCount。
/// </summary>
public class WaterLeafHourlyRepository
{
    private readonly ILogger<WaterLeafHourlyRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public WaterLeafHourlyRepository(
        ILogger<WaterLeafHourlyRepository> logger,
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

    /// <summary>葉子節點（綁 SID）— 從 WaterCircuit 取出。</summary>
    public record LeafInfo(string szSID, string szName);

    /// <summary>取得 WaterCircuit 內所有綁 SID 的葉子。</summary>
    public async Task<List<LeafInfo>> GetAllLeafSidsAsync()
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string SID, string Name)>(@"
            SELECT SID, Name
            FROM   WaterCircuit
            WHERE  SID IS NOT NULL AND LEN(SID) > 0");
        return rows.Select(r => new LeafInfo(r.SID, r.Name)).ToList();
    }

    /// <summary>查指定 SID 在 [from, to) 區間內已存在的 HourStart 集合（給 catch-up / backfill 跳過已聚合用）</summary>
    public async Task<HashSet<DateTime>> GetExistingHoursAsync(string szSid, DateTime dtFrom, DateTime dtTo)
    {
        using var conn = await OpenConnectionAsync();
        var rows = await conn.QueryAsync<DateTime>(@"
            SELECT HourStart
            FROM   WaterLeafHourly
            WHERE  SID = @SID AND HourStart >= @From AND HourStart < @To",
            new { SID = szSid, From = dtFrom, To = dtTo });
        return new HashSet<DateTime>(rows);
    }

    /// <summary>UPSERT 一筆聚合資料（同 (SID, HourStart) 已存在則覆寫）。</summary>
    public async Task UpsertAsync(WaterLeafHourlyModel model)
    {
        using var conn = await OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            MERGE WaterLeafHourly WITH (HOLDLOCK) AS tgt
            USING (SELECT @SID AS SID, @HourStart AS HourStart) AS src
               ON tgt.SID = src.SID AND tgt.HourStart = src.HourStart
            WHEN MATCHED THEN
                UPDATE SET RtHour = @RtHour,
                           SampleCount = @SampleCount,
                           Quality = @Quality,
                           IsBackfilled = @IsBackfilled,
                           CreatedAt = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT (SID, HourStart, RtHour, SampleCount, Quality, IsBackfilled, CreatedAt)
                VALUES (@SID, @HourStart, @RtHour, @SampleCount, @Quality, @IsBackfilled, GETDATE());",
            new
            {
                SID = model.szSID,
                HourStart = model.dtHourStart,
                RtHour = model.dRtHour,
                SampleCount = model.nSampleCount,
                Quality = (byte)model.nQuality,
                IsBackfilled = model.isBackfilled
            });
    }
}
