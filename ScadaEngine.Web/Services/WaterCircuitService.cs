using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 水系統迴路階層 CRUD — 自參照樹結構，葉子綁 RT 系列點位 SID。
/// 與 EnergyCircuitService 同 pattern，但無 Sign / MaxKwh（水系統為瞬時值）。
/// </summary>
public class WaterCircuitService
{
    private readonly ILogger<WaterCircuitService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public WaterCircuitService(ILogger<WaterCircuitService> logger, DatabaseConfigService configService)
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

    public async Task<IEnumerable<WaterCircuitModel>> GetAllAsync()
    {
        using var conn = await GetConnectionAsync();
        return await conn.QueryAsync<WaterCircuitModel>(@"
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    WaterCircuit
            ORDER BY ParentId, SortOrder");
    }

    public async Task<WaterCircuitModel?> GetByIdAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<WaterCircuitModel>(@"
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    WaterCircuit
            WHERE   Id = @Id", new { Id = nId });
    }

    public async Task<int> CreateAsync(WaterCircuitModel model)
    {
        using var conn = await GetConnectionAsync();
        var nNextSort = await conn.ExecuteScalarAsync<int>(@"
            SELECT ISNULL(MAX(SortOrder), -1) + 1
            FROM   WaterCircuit
            WHERE  (ParentId = @ParentId) OR (@ParentId IS NULL AND ParentId IS NULL)",
            new { ParentId = model.nParentId });

        return await conn.QuerySingleAsync<int>(@"
            INSERT INTO WaterCircuit (Name, ParentId, SortOrder, SID, Description, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Name, @ParentId, @SortOrder, @SID, @Description, GETDATE())",
            new
            {
                Name = model.szName,
                ParentId = model.nParentId,
                SortOrder = nNextSort,
                SID = model.szSID,
                Description = model.szDescription
            });
    }

    public async Task<bool> UpdateAsync(int nId, string szName, string? szSID, string? szDescription)
    {
        using var conn = await GetConnectionAsync();
        var nRows = await conn.ExecuteAsync(@"
            UPDATE  WaterCircuit
            SET     Name = @Name,
                    SID = @SID,
                    Description = @Description,
                    UpdatedAt = GETDATE()
            WHERE   Id = @Id",
            new { Id = nId, Name = szName, SID = szSID, Description = szDescription });
        return nRows > 0;
    }

    public async Task<bool> DeleteAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            var allIds = (await conn.QueryAsync<int>(@"
                WITH CTE AS (
                    SELECT Id FROM WaterCircuit WHERE Id = @Id
                    UNION ALL
                    SELECT t.Id FROM WaterCircuit t INNER JOIN CTE c ON t.ParentId = c.Id
                )
                SELECT Id FROM CTE", new { Id = nId }, tran)).ToList();

            if (allIds.Count == 0) { tran.Rollback(); return false; }

            await conn.ExecuteAsync("DELETE FROM WaterCircuit WHERE Id IN @Ids",
                new { Ids = allIds }, tran);
            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "刪除 WaterCircuit 節點 {Id} 失敗", nId);
            return false;
        }
    }

    public async Task<bool> HasChildrenAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        var nCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM WaterCircuit WHERE ParentId = @Id", new { Id = nId });
        return nCount > 0;
    }

    public async Task<bool> UpdateSortOrderAsync(IEnumerable<(int nId, int? nParentId, int nSortOrder)> sortList)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            foreach (var (nId, nParentId, nSortOrder) in sortList)
            {
                await conn.ExecuteAsync(@"
                    UPDATE WaterCircuit
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
            _logger.LogError(ex, "更新 WaterCircuit 排序失敗");
            return false;
        }
    }

    /// <summary>
    /// 展開指定迴路下的所有葉子（綁 SID 的節點）— 預留給未來 COP / 製冷量報表使用。
    /// 目前本期不做報表，介面先擺著保留呼叫端形狀。
    /// </summary>
    public async Task<List<WaterCircuitModel>> GetLeavesUnderAsync(int nCircuitId)
    {
        using var conn = await GetConnectionAsync();
        var rows = await conn.QueryAsync<WaterCircuitModel>(@"
            ;WITH CTE AS (
                SELECT Id, Name, ParentId, SortOrder, SID, Description, CreatedAt, UpdatedAt
                FROM   WaterCircuit WHERE Id = @Id
                UNION ALL
                SELECT t.Id, t.Name, t.ParentId, t.SortOrder, t.SID, t.Description, t.CreatedAt, t.UpdatedAt
                FROM   WaterCircuit t INNER JOIN CTE c ON t.ParentId = c.Id
            )
            SELECT  Id          AS nId,
                    Name        AS szName,
                    ParentId    AS nParentId,
                    SortOrder   AS nSortOrder,
                    SID         AS szSID,
                    Description AS szDescription,
                    CreatedAt   AS dtCreatedAt,
                    UpdatedAt   AS dtUpdatedAt
            FROM    CTE
            WHERE   SID IS NOT NULL AND LEN(SID) > 0",
            new { Id = nCircuitId });
        return rows.ToList();
    }
}
