using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 葉子層 hourly 聚合 — 純邏輯類。
/// 給 EnergyLeafAggregationService（hourly Timer）與 EnergyLeafBackfillSubscriber（MQTT 觸發）共用。
/// 輸入單一 SID + 單一小時，回傳 EnergyLeafHourlyModel 或 null（兩邊邊界都缺 → 不寫，sparse storage）。
/// 與 Web 端 EnergyReportService.CalcDeltaWithRollover 行為必須一致。
/// </summary>
public class EnergyLeafAggregator
{
    private readonly ILogger<EnergyLeafAggregator> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public EnergyLeafAggregator(
        ILogger<EnergyLeafAggregator> logger,
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

    /// <summary>
    /// 計算 sid 在 [hourStart, hourStart+1hr) 的 kWh 增量。
    /// 三段語意：
    ///   兩邊有 → Q=1 算 delta
    ///   只缺一邊 → Q=0, Delta=0（掉線 transition）
    ///   兩邊都缺 → 回傳 null（sparse storage，不寫該列）
    /// </summary>
    public async Task<EnergyLeafHourlyModel?> ComputeAsync(
        string szSid, DateTime dtHourStart, double? dMaxKwh, int nMaxStalenessHours, string szLeafName = "")
    {
        await EnsureConnectionStringAsync();

        var dtHourEnd = dtHourStart.AddHours(1);
        double? fStart;
        double? fEnd;
        using (var conn = new SqlConnection(_szConnectionString))
        {
            await conn.OpenAsync();
            (fStart, fEnd) = await GetBoundaryValuesAsync(conn, szSid, dtHourStart, dtHourEnd, nMaxStalenessHours);
        }

        // 兩邊都缺 → sparse storage，不寫
        if (fStart == null && fEnd == null)
            return null;

        // 只缺一邊 → 掉線 transition，寫 Q=0 Delta=0
        if (fStart == null || fEnd == null)
        {
            return new EnergyLeafHourlyModel
            {
                szSID = szSid,
                dtHourStart = dtHourStart,
                dDeltaKwh = 0,
                nQuality = 0,
                isRolledOver = false
            };
        }

        // 兩邊都有 → 正常 delta，套溢位規則
        var (dDelta, isRolledOver) = CalcDeltaWithRollover(fStart.Value, fEnd.Value, dMaxKwh, szSid, szLeafName);
        return new EnergyLeafHourlyModel
        {
            szSID = szSid,
            dtHourStart = dtHourStart,
            dDeltaKwh = dDelta,
            nQuality = 1,
            isRolledOver = isRolledOver
        };
    }

    /// <summary>
    /// 取 sid 在 t0 與 t1 兩個時點各自的「最近一筆」HistoryData 值。
    /// 套 staleness window：source Timestamp 距 boundary &gt; maxStalenessHours 視為 null。
    /// 與 Web EnergyReportService.GetBoundaryValuesAsync 行為一致。
    /// </summary>
    private static async Task<(double? fStart, double? fEnd)> GetBoundaryValuesAsync(
        SqlConnection conn, string szSid, DateTime dtT0, DateTime dtT1, int nMaxStalenessHours)
    {
        const string szSql = @"
            SELECT b.idx, ba.Value FROM (VALUES (0, @t0), (1, @t1)) AS b(idx, BoundaryTime)
            OUTER APPLY (
                SELECT TOP 1 Value FROM HistoryData WITH (NOLOCK)
                WHERE  SID = @sid
                   AND Timestamp <= b.BoundaryTime
                   AND Timestamp >= DATEADD(HOUR, -@maxStalenessHours, b.BoundaryTime)
                   AND Quality = 1
                ORDER BY Timestamp DESC
            ) ba
            ORDER BY b.idx";

        var rows = await conn.QueryAsync<(int idx, double? Value)>(szSql, new
        {
            sid = szSid,
            t0 = dtT0,
            t1 = dtT1,
            maxStalenessHours = nMaxStalenessHours
        });
        double? f0 = null, f1 = null;
        foreach (var r in rows)
        {
            if (r.idx == 0) f0 = r.Value;
            else if (r.idx == 1) f1 = r.Value;
        }
        return (f0, f1);
    }

    /// <summary>
    /// kWh 溢位/重置 delta — 與 EnergyReportService.CalcDeltaWithRollover 必須語意一致。
    /// V_end &gt;= V_start: 正常累計
    /// V_end &lt; V_start &amp;&amp; MaxKwh 有設: (Max - Vs) + Ve
    /// V_end &lt; V_start &amp;&amp; MaxKwh 無設: 視為電表重置/異常，回 0 並警告
    /// </summary>
    private (double dDelta, bool isRolledOver) CalcDeltaWithRollover(
        double dStart, double dEnd, double? dMaxKwh, string szSid, string szLeafName)
    {
        if (dEnd >= dStart)
            return (dEnd - dStart, false);

        if (dMaxKwh.HasValue && dMaxKwh.Value > 0)
            return ((dMaxKwh.Value - dStart) + dEnd, true);

        _logger.LogWarning(
            "葉子層聚合 {SID} ({Name}) 偵測到累積值倒退（{Start} → {End}）但未設定 MaxKwh，該段 delta 視為 0",
            szSid, szLeafName, dStart, dEnd);
        return (0, false);
    }
}
