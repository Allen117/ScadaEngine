namespace ScadaEngine.Web.Features.GasMeterSetting.Models;

/// <summary>
/// 氣表迴路樹狀節點 — 給前端組樹用的 DTO（camelCase 無 Hungarian 前綴，直接 JSON 序列化）
/// </summary>
public class GasMeterCircuitNodeViewModel
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
    public string? sid { get; set; }
    /// <summary>點位原始單位 → m³ 換算係數（m³/Nm³/度 系=1、L 系=0.001）</summary>
    public double unitScale { get; set; } = 1.0;
    /// <summary>氣表累積最大值（以點位原始單位計），溢位/歸零判定用；葉子才有意義</summary>
    public double? maxVolume { get; set; }
    public int sign { get; set; } = 1;
    public string? description { get; set; }
}
