namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 能源申報報表設定 — 對應 EnergyDeclarationReport 資料表。
/// 一列 = 一份申報報表，成對綁定用電迴路（EnergyCircuit）與水系統迴路（WaterCircuit）。
/// </summary>
public class EnergyDeclarationModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    /// <summary>用電度數來源 — EnergyCircuit.Id</summary>
    public int nEnergyCircuitId { get; set; }
    /// <summary>冷凍噸數來源 — WaterCircuit.Id</summary>
    public int nWaterCircuitId { get; set; }
    public int nSortOrder { get; set; }
    public string? szDescription { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
