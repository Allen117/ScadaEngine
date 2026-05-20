using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 電表/迴路階層 CRUD — 自參照樹結構，葉子綁 SID。
/// </summary>
public class EnergyCircuitService
{
    private readonly ILogger<EnergyCircuitService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public EnergyCircuitService(ILogger<EnergyCircuitService> logger, DatabaseConfigService configService)
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

    /// <summary>取得所有節點（平坦清單，前端組樹）</summary>
    public async Task<IEnumerable<EnergyCircuitModel>> GetAllAsync()
    {
        using var conn = await GetConnectionAsync();
        return await conn.QueryAsync<EnergyCircuitModel>(@"
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    MaxKwh      AS dMaxKwh,
                    [Sign]      AS nSign,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    EnergyCircuit
            ORDER BY ParentId, SortOrder");
    }

    /// <summary>取得單筆節點</summary>
    public async Task<EnergyCircuitModel?> GetByIdAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<EnergyCircuitModel>(@"
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    MaxKwh      AS dMaxKwh,
                    [Sign]      AS nSign,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    EnergyCircuit
            WHERE   Id = @Id", new { Id = nId });
    }

    /// <summary>新增節點。若未指定 SortOrder，自動排到同層末端。根節點強制 Sign=1。</summary>
    public async Task<int> CreateAsync(EnergyCircuitModel model)
    {
        using var conn = await GetConnectionAsync();
        var nNextSort = await conn.ExecuteScalarAsync<int>(@"
            SELECT ISNULL(MAX(SortOrder), -1) + 1
            FROM   EnergyCircuit
            WHERE  (ParentId = @ParentId) OR (@ParentId IS NULL AND ParentId IS NULL)",
            new { ParentId = model.nParentId });

        var nSign = NormalizeSign(model.nSign, model.nParentId);

        return await conn.QuerySingleAsync<int>(@"
            INSERT INTO EnergyCircuit (Name, ParentId, SortOrder, SID, MaxKwh, [Sign], Description, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Name, @ParentId, @SortOrder, @SID, @MaxKwh, @Sign, @Description, GETDATE())",
            new
            {
                Name = model.szName,
                ParentId = model.nParentId,
                SortOrder = nNextSort,
                SID = model.szSID,
                MaxKwh = model.dMaxKwh,
                Sign = nSign,
                Description = model.szDescription
            });
    }

    /// <summary>更新節點（不動 ParentId / SortOrder，改用 MoveAsync）。根節點強制 Sign=1。</summary>
    public async Task<bool> UpdateAsync(int nId, string szName, string? szSID, double? dMaxKwh, int nSign, string? szDescription)
    {
        using var conn = await GetConnectionAsync();
        var nParentId = await conn.ExecuteScalarAsync<int?>(
            "SELECT ParentId FROM EnergyCircuit WHERE Id = @Id", new { Id = nId });
        var nSignNormalized = NormalizeSign(nSign, nParentId);

        var nRows = await conn.ExecuteAsync(@"
            UPDATE  EnergyCircuit
            SET     Name = @Name,
                    SID = @SID,
                    MaxKwh = @MaxKwh,
                    [Sign] = @Sign,
                    Description = @Description,
                    UpdatedAt = GETDATE()
            WHERE   Id = @Id",
            new { Id = nId, Name = szName, SID = szSID, MaxKwh = dMaxKwh, Sign = nSignNormalized, Description = szDescription });
        return nRows > 0;
    }

    /// <summary>規格化 sign：限定 ±1，根節點強制為 +1。</summary>
    private static int NormalizeSign(int nSign, int? nParentId)
    {
        if (nParentId == null) return 1;
        return nSign == -1 ? -1 : 1;
    }

    /// <summary>刪除節點（含所有子孫）</summary>
    public async Task<bool> DeleteAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            var allIds = (await conn.QueryAsync<int>(@"
                WITH CTE AS (
                    SELECT Id FROM EnergyCircuit WHERE Id = @Id
                    UNION ALL
                    SELECT t.Id FROM EnergyCircuit t INNER JOIN CTE c ON t.ParentId = c.Id
                )
                SELECT Id FROM CTE", new { Id = nId }, tran)).ToList();

            if (allIds.Count == 0) { tran.Rollback(); return false; }

            await conn.ExecuteAsync("DELETE FROM EnergyCircuit WHERE Id IN @Ids",
                new { Ids = allIds }, tran);
            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "刪除 EnergyCircuit 節點 {Id} 失敗", nId);
            return false;
        }
    }

    /// <summary>檢查節點是否有子節點</summary>
    public async Task<bool> HasChildrenAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        var nCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM EnergyCircuit WHERE ParentId = @Id", new { Id = nId });
        return nCount > 0;
    }

    /// <summary>批次更新排序（拖曳完成後整批送回，每筆需含 ParentId 才能正確處理跨層搬動）</summary>
    public async Task<bool> UpdateSortOrderAsync(IEnumerable<(int nId, int? nParentId, int nSortOrder)> sortList)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            foreach (var (nId, nParentId, nSortOrder) in sortList)
            {
                await conn.ExecuteAsync(@"
                    UPDATE EnergyCircuit
                    SET    ParentId = @ParentId, SortOrder = @SortOrder, UpdatedAt = GETDATE()
                    WHERE  Id = @Id",
                    new { Id = nId, ParentId = nParentId, SortOrder = nSortOrder }, tran);
            }
            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "更新 EnergyCircuit 排序失敗");
            return false;
        }
    }

    /// <summary>取得指定節點的直接子節點（不遞迴）— 給 Excel 匯出多欄展開用</summary>
    public async Task<List<EnergyCircuitModel>> GetDirectChildrenAsync(int nParentId)
    {
        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<EnergyCircuitModel>(@"
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    MaxKwh      AS dMaxKwh,
                    [Sign]      AS nSign,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    EnergyCircuit
            WHERE   ParentId = @Id
            ORDER BY SortOrder, Id", new { Id = nParentId });
        return rows.ToList();
    }

    /// <summary>葉子展開結果 — 葉子節點 + 從查詢根到葉子路徑上 sign 的乘積（不含查詢根本身的 sign）。</summary>
    public record LeafWithSign(EnergyCircuitModel Leaf, int nEffectiveSign);

    /// <summary>
    /// 展開指定迴路下的所有葉子（綁 SID 的節點）。虛擬迴路會遞迴展開。
    /// 每筆附帶從查詢根到該葉子的 sign 乘積（不含查詢根自己的 sign — 查詢根對自己沒有方向意義）。
    /// </summary>
    public async Task<List<LeafWithSign>> GetLeavesUnderAsync(int nCircuitId)
    {
        using var conn = await GetConnectionAsync();
        // 遞迴 CTE：anchor 為查詢根，EffectiveSign 起始 1（不含自己的 sign）；
        // 子層 EffectiveSign = 父 EffectiveSign × 自己的 Sign。
        var rows = await conn.QueryAsync<(int nId, string szName, int? nParentId, int nSortOrder,
            string? szSID, double? dMaxKwh, int nSign, string? szDescription,
            DateTime dtCreatedAt, DateTime? dtUpdatedAt, int nEffectiveSign)>(@"
            ;WITH CTE AS (
                SELECT Id, Name, ParentId, SortOrder, SID, MaxKwh, [Sign], Description, CreatedAt, UpdatedAt,
                       CAST(1 AS INT) AS EffectiveSign
                FROM   EnergyCircuit WHERE Id = @Id
                UNION ALL
                SELECT t.Id, t.Name, t.ParentId, t.SortOrder, t.SID, t.MaxKwh, t.[Sign], t.Description, t.CreatedAt, t.UpdatedAt,
                       CAST(c.EffectiveSign * t.[Sign] AS INT) AS EffectiveSign
                FROM   EnergyCircuit t INNER JOIN CTE c ON t.ParentId = c.Id
            )
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    MaxKwh      AS dMaxKwh,
                    [Sign]      AS nSign,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt,
                    EffectiveSign AS nEffectiveSign
            FROM    CTE
            WHERE   SID IS NOT NULL AND LEN(SID) > 0",
            new { Id = nCircuitId });

        return rows.Select(r => new LeafWithSign(
            new EnergyCircuitModel
            {
                nId = r.nId,
                szName = r.szName,
                nParentId = r.nParentId,
                nSortOrder = r.nSortOrder,
                szSID = r.szSID,
                dMaxKwh = r.dMaxKwh,
                nSign = r.nSign,
                szDescription = r.szDescription,
                dtCreatedAt = r.dtCreatedAt,
                dtUpdatedAt = r.dtUpdatedAt
            },
            r.nEffectiveSign
        )).ToList();
    }
}
