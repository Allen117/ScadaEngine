namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 氣表迴路階層 — 對應 GasMeterCircuit 資料表（累積式天然氣表 m³/Nm³/度）。
/// 結構與 <see cref="WaterMeterCircuitModel"/> 對稱但完全獨立（費率、期別各自一套）。
/// </summary>
public class GasMeterCircuitModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    public int? nParentId { get; set; }
    public int nSortOrder { get; set; }
    public string? szSID { get; set; }

    /// <summary>點位原始單位 → m³ 的換算係數（m³/Nm³/度 系=1、L 系=0.001）。綁定時依點位單位定案</summary>
    public double dUnitScale { get; set; } = 1.0;

    /// <summary>氣表累積最大值（以點位原始單位計），用於溢位/歸零判定。葉子才有意義</summary>
    public double? dMaxVolume { get; set; }

    /// <summary>對父節點的貢獻方向：+1=正向加入、-1=反向扣減。根節點固定 +1</summary>
    public int nSign { get; set; } = 1;

    public string? szDescription { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
