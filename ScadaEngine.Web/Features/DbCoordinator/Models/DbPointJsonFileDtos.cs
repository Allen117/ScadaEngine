namespace ScadaEngine.Web.Features.DbCoordinator.Models;

/// <summary>
/// DBPoint/{Name}.json 檔案結構（與 Engine DbCoordinatorJsonLoader 的 internal DTO 對齊；
/// Engine 端為 internal 無法共用，Web 端自持一份）。
/// 點位 SID = DB{CoordinatorId}-S{陣列索引+1}，陣列順序即 Sequence，不可增刪重排。
/// </summary>
public class DbPointJsonFile
{
    public string? Name { get; set; }
    public int PollingInterval { get; set; } = 1000;
    public int ConnectTimeout { get; set; } = 1000;
    public bool MonitorEnabled { get; set; } = true;
    public List<DbPointJsonItem>? Points { get; set; }
}

public class DbPointJsonItem
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public float Min { get; set; } = 0.0f;
    public float Max { get; set; } = 100.0f;
}
