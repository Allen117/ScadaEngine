using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端 LogicFlow 資料存取 — 讀取啟用的邏輯流程與圖形資料
/// </summary>
public class LogicFlowRepository
{
    private readonly ILogger<LogicFlowRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public LogicFlowRepository(
        ILogger<LogicFlowRepository> logger,
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

    /// <summary>取得所有啟用的 logic 節點 (NodeType='logic', IsEnabled=1)</summary>
    public async Task<IEnumerable<(int nTreeId, string szName)>> GetEnabledLogicNodesAsync()
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                SELECT Id, Name
                FROM LogicFlowTree
                WHERE NodeType = 'logic' AND IsEnabled = 1";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            var rows = await connection.QueryAsync<(int Id, string Name)>(szSql);
            return rows.Select(r => (r.Id, r.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得啟用 LogicFlow 節點失敗");
            return Enumerable.Empty<(int, string)>();
        }
    }

    /// <summary>取得所有啟用的排程</summary>
    public async Task<List<ScheduleRecord>> GetEnabledSchedulesAsync()
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = @"
                SELECT Id, RecurrenceType, RunLength, RestLength, AnchorDateTime,
                       DaysOfWeek, DaysOfMonth, StartTime, EndTime
                FROM TimeSchedules
                WHERE IsEnabled = 1";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            return (await connection.QueryAsync<ScheduleRecord>(szSql)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得排程資料失敗");
            return new List<ScheduleRecord>();
        }
    }

    /// <summary>取得流程圖 JSON</summary>
    public async Task<string?> GetDiagramJsonAsync(int nTreeId)
    {
        await EnsureConnectionStringAsync();
        try
        {
            const string szSql = "SELECT DiagramJson FROM LogicFlowDiagram WHERE TreeId = @TreeId";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            return await connection.QuerySingleOrDefaultAsync<string>(szSql, new { TreeId = nTreeId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得 LogicFlow 圖形資料失敗: TreeId={TreeId}", nTreeId);
            return null;
        }
    }
}

/// <summary>Dapper 映射：TimeSchedules 表（Enabled 的排程記錄）</summary>
public class ScheduleRecord
{
    public int Id { get; set; }
    public int RecurrenceType { get; set; }
    public int? RunLength { get; set; }
    public int? RestLength { get; set; }
    public DateTime? AnchorDateTime { get; set; }
    public string? DaysOfWeek { get; set; }
    public string? DaysOfMonth { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}
