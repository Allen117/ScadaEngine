namespace ScadaEngine.Web.Features.ChilledWaterSystem.Models;

/// <summary>
/// 水系統迴路樹狀節點 DTO（前端組樹用）
/// </summary>
public class WaterCircuitNodeViewModel
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
    public string? sid { get; set; }
    public string? description { get; set; }
}
