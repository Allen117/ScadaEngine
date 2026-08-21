namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 自來水表迴路階層 — 對應 WaterMeterCircuit 資料表（累積式水表 m³/L）。
/// 與 WaterCircuitModel（空調水系統冷凍噸 RT）無關。
/// </summary>
public class WaterMeterCircuitModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    public int? nParentId { get; set; }
    public int nSortOrder { get; set; }
    public string? szSID { get; set; }

    /// <summary>點位原始單位 → m³ 的換算係數（m³ 系=1、L 系=0.001）。綁定時依點位單位定案</summary>
    public double dUnitScale { get; set; } = 1.0;

    /// <summary>水表累積最大值（以點位原始單位計），用於溢位/歸零判定。葉子才有意義</summary>
    public double? dMaxVolume { get; set; }

    /// <summary>對父節點的貢獻方向：+1=正向加入、-1=反向扣減。根節點固定 +1</summary>
    public int nSign { get; set; } = 1;

    public string? szDescription { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
