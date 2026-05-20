namespace ScadaEngine.Web.Features.DbCoordinator.Models;

/// <summary>
/// 「DB 來源」頁面 — 序列化用 DTO（避免 Razor 直接吃 Engine Model 造成 namespace 依賴擴散）
/// </summary>
public class DbCoordinatorListItemDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public int pollingInterval { get; set; }
    public int connectTimeout { get; set; }
    public bool monitorEnabled { get; set; }
    public List<DbPointListItemDto> points { get; set; } = new();
}

public class DbPointListItemDto
{
    public string sid { get; set; } = string.Empty;
    public int sequence { get; set; }
    public string name { get; set; } = string.Empty;
    public string unit { get; set; } = string.Empty;
    public float min { get; set; }
    public float max { get; set; }
}
