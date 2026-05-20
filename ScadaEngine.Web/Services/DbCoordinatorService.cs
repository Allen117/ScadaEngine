using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Web 端 DB 來源查詢服務 — Phase 1 唯讀，編輯由 Excel + 巨集
/// </summary>
public class DbCoordinatorService
{
    private readonly IDataRepository _repository;
    private readonly ILogger<DbCoordinatorService> _logger;

    public DbCoordinatorService(IDataRepository repository, ILogger<DbCoordinatorService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// 取得所有 DB Coordinator 連同其點位（依 Coordinator 群組）
    /// </summary>
    public async Task<List<DbCoordinatorWithPointsDto>> GetAllAsync()
    {
        var coordinators = (await _repository.GetAllDbCoordinatorsAsync()).ToList();
        var allPoints = (await _repository.GetAllDbPointsAsync()).ToList();

        var pointsByCoord = allPoints.GroupBy(p => p.nCoordinatorId)
                                     .ToDictionary(g => g.Key, g => g.OrderBy(p => p.nSequence).ToList());

        var result = new List<DbCoordinatorWithPointsDto>();
        foreach (var c in coordinators)
        {
            pointsByCoord.TryGetValue(c.Id, out var points);
            result.Add(new DbCoordinatorWithPointsDto
            {
                Coordinator = c,
                Points = points ?? new List<DbPointModel>()
            });
        }
        return result;
    }
}

public class DbCoordinatorWithPointsDto
{
    public DbCoordinatorModel Coordinator { get; set; } = new();
    public List<DbPointModel> Points { get; set; } = new();
}
