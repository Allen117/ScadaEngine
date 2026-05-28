namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 水系統迴路階層 — 對應 WaterCircuit 資料表（獨立於 EnergyCircuit）
/// </summary>
public class WaterCircuitModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    public int? nParentId { get; set; }
    public int nSortOrder { get; set; }
    public string? szSID { get; set; }
    public string? szDescription { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
