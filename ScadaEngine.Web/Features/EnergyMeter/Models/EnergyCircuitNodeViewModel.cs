namespace ScadaEngine.Web.Features.EnergyMeter.Models;

/// <summary>
/// 樹狀節點 — 給前端組樹用的 DTO（與 DB 模型欄位一致，但純粹資料無 Hungarian 前綴的 JSON 欄位由前端使用）
/// </summary>
public class EnergyCircuitNodeViewModel
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
    public string? sid { get; set; }
    public double? maxKwh { get; set; }
    public int sign { get; set; } = 1;
    public string? demandSid { get; set; }
    public string? description { get; set; }
}
