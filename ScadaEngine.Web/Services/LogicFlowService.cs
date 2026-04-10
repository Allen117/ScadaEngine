using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.LogicFlow.Models;

namespace ScadaEngine.Web.Services;

public class LogicFlowService
{
    private readonly ILogger<LogicFlowService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public LogicFlowService(ILogger<LogicFlowService> logger, DatabaseConfigService configService)
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

    // ============ Tree CRUD ============

    /// <summary>取得所有樹節點（平坦清單，前端自行組裝樹狀結構）</summary>
    public async Task<IEnumerable<LogicFlowTreeNode>> GetAllNodesAsync()
    {
        using var conn = await GetConnectionAsync();
        return await conn.QueryAsync<LogicFlowTreeNode>(
            "SELECT Id, ParentId, Name, NodeType, SortOrder, IsEnabled FROM LogicFlowTree ORDER BY SortOrder");
    }

    /// <summary>新增節點</summary>
    public async Task<int> CreateNodeAsync(int? nParentId, string szName, string szNodeType, int nSortOrder)
    {
        using var conn = await GetConnectionAsync();
        // 邏輯節點預設停用，資料夾預設啟用
        var isEnabled = szNodeType != "logic";
        var nId = await conn.QuerySingleAsync<int>(@"
            INSERT INTO LogicFlowTree (ParentId, Name, NodeType, SortOrder, IsEnabled)
            OUTPUT INSERTED.Id
            VALUES (@ParentId, @Name, @NodeType, @SortOrder, @IsEnabled)",
            new { ParentId = nParentId, Name = szName, NodeType = szNodeType, SortOrder = nSortOrder, IsEnabled = isEnabled });

        // 若為 logic 節點，一併建立空白 Diagram
        if (szNodeType == "logic")
        {
            await conn.ExecuteAsync(
                "INSERT INTO LogicFlowDiagram (TreeId, DiagramJson) VALUES (@TreeId, @Json)",
                new { TreeId = nId, Json = "{\"nodes\":[],\"edges\":[]}" });
        }

        return nId;
    }

    /// <summary>重新命名節點</summary>
    public async Task<bool> RenameNodeAsync(int nId, string szName)
    {
        using var conn = await GetConnectionAsync();
        var nRows = await conn.ExecuteAsync(
            "UPDATE LogicFlowTree SET Name = @Name, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = nId, Name = szName });
        return nRows > 0;
    }

    /// <summary>刪除節點（含所有子節點與對應 Diagram）</summary>
    public async Task<bool> DeleteNodeAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            // 遞迴收集所有子孫 Id
            var allIds = await conn.QueryAsync<int>(@"
                WITH CTE AS (
                    SELECT Id FROM LogicFlowTree WHERE Id = @Id
                    UNION ALL
                    SELECT t.Id FROM LogicFlowTree t INNER JOIN CTE c ON t.ParentId = c.Id
                )
                SELECT Id FROM CTE", new { Id = nId }, tran);

            var idList = allIds.ToList();
            if (idList.Count == 0) { tran.Rollback(); return false; }

            // 刪除 Diagram
            await conn.ExecuteAsync(
                "DELETE FROM LogicFlowDiagram WHERE TreeId IN @Ids",
                new { Ids = idList }, tran);

            // 刪除樹節點
            await conn.ExecuteAsync(
                "DELETE FROM LogicFlowTree WHERE Id IN @Ids",
                new { Ids = idList }, tran);

            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "刪除 LogicFlowTree 節點 {Id} 時發生錯誤", nId);
            return false;
        }
    }

    /// <summary>批次更新排序（前端拖曳排序後整批送回）</summary>
    public async Task<bool> UpdateSortOrderAsync(IEnumerable<(int nId, int nSortOrder)> sortList)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            foreach (var (nId, nSortOrder) in sortList)
            {
                await conn.ExecuteAsync(
                    "UPDATE LogicFlowTree SET SortOrder = @SortOrder, UpdatedAt = GETDATE() WHERE Id = @Id",
                    new { Id = nId, SortOrder = nSortOrder }, tran);
            }
            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "更新 LogicFlowTree 排序時發生錯誤");
            return false;
        }
    }

    /// <summary>切換啟用/停用（含所有子孫節點）</summary>
    public async Task<bool> ToggleEnabledAsync(int nId, bool isEnabled)
    {
        using var conn = await GetConnectionAsync();
        using var tran = conn.BeginTransaction();
        try
        {
            // 遞迴收集自身與所有子孫 Id
            var allIds = await conn.QueryAsync<int>(@"
                WITH CTE AS (
                    SELECT Id FROM LogicFlowTree WHERE Id = @Id
                    UNION ALL
                    SELECT t.Id FROM LogicFlowTree t INNER JOIN CTE c ON t.ParentId = c.Id
                )
                SELECT Id FROM CTE", new { Id = nId }, tran);

            var idList = allIds.ToList();
            if (idList.Count == 0) { tran.Rollback(); return false; }

            await conn.ExecuteAsync(
                "UPDATE LogicFlowTree SET IsEnabled = @IsEnabled, UpdatedAt = GETDATE() WHERE Id IN @Ids",
                new { IsEnabled = isEnabled, Ids = idList }, tran);

            tran.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "切換 LogicFlowTree 節點 {Id} 啟用狀態時發生錯誤", nId);
            return false;
        }
    }

    // ============ Diagram CRUD ============

    /// <summary>取得流程圖 JSON</summary>
    public async Task<LogicFlowDiagramDto?> GetDiagramAsync(int nTreeId)
    {
        using var conn = await GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<LogicFlowDiagramDto>(
            "SELECT TreeId, DiagramJson, Version FROM LogicFlowDiagram WHERE TreeId = @TreeId",
            new { TreeId = nTreeId });
    }

    /// <summary>儲存流程圖 JSON（樂觀鎖）</summary>
    public async Task<bool> SaveDiagramAsync(int nTreeId, string szDiagramJson, int nExpectedVersion)
    {
        using var conn = await GetConnectionAsync();
        var nRows = await conn.ExecuteAsync(@"
            UPDATE LogicFlowDiagram
            SET DiagramJson = @Json, Version = Version + 1, UpdatedAt = GETDATE()
            WHERE TreeId = @TreeId AND Version = @ExpectedVersion",
            new { TreeId = nTreeId, Json = szDiagramJson, ExpectedVersion = nExpectedVersion });
        return nRows > 0;
    }
}
