namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 計算點位資料模型 — 對應 CalculatedPoints 資料表
/// </summary>
public class CalculatedPointModel
{
    public string szSID { get; set; } = "";
    public string szName { get; set; } = "";
    public string szUnit { get; set; } = "";
    public string szGroupName { get; set; } = "";
    public string szFormula { get; set; } = "";
    public string szInputMappings { get; set; } = "{}";
    public bool isEnabled { get; set; } = true;
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
