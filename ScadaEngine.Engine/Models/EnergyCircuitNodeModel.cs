namespace ScadaEngine.Engine.Models;

/// <summary>
/// EnergyCircuit 全樹節點（迴路用電虛擬點位計算用）。
/// Engine 端一次撈全樹後在記憶體展開各迴路葉子（比照 Web GetLeavesUnderAsync 的 CTE 語意），
/// 減少每迴路一次遞迴 CTE 的 round-trip。
/// </summary>
public class EnergyCircuitNodeModel
{
    public int nId { get; set; }
    public int? nParentId { get; set; }
    public string szName { get; set; } = "";
    /// <summary>綁定的 kWh 點位 SID；虛擬節點為 null/空字串</summary>
    public string? szSID { get; set; }
    /// <summary>電表累積值溢位上限（kWh）；未設定為 null</summary>
    public double? dMaxKwh { get; set; }
    /// <summary>正逆相（+1 / -1），葉子有效 sign = 路徑上各層 Sign 乘積</summary>
    public int nSign { get; set; } = 1;
}
