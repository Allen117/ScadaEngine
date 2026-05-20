namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 電表/迴路階層 — 對應 EnergyCircuit 資料表
/// </summary>
public class EnergyCircuitModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    public int? nParentId { get; set; }
    public int nSortOrder { get; set; }
    public string? szSID { get; set; }
    public double? dMaxKwh { get; set; }
    /// <summary>對父節點的貢獻方向：+1=正向加入、-1=反向扣減。根節點固定 +1</summary>
    public int nSign { get; set; } = 1;
    public string? szDescription { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
