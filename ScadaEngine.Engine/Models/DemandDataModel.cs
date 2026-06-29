namespace ScadaEngine.Engine.Models;

/// <summary>
/// 需量計算結果 POCO，對應 DemandData 資料表
/// </summary>
public class DemandDataModel
{
    public string szSID { get; set; } = string.Empty;

    public DateTime dtTimestamp { get; set; }

    /// <summary>15min 滑動時間加權平均功率（kW）；Quality=0 時為 0</summary>
    public double dDemandKW { get; set; }

    /// <summary>計算窗口起點（= Timestamp - 15min）</summary>
    public DateTime dtWindowStart { get; set; }

    /// <summary>窗口內 Quality=1 的樣本數</summary>
    public int nSampleCount { get; set; }

    /// <summary>1=Good, 0=資料不足（SampleCount &lt; 5）</summary>
    public byte nQuality { get; set; }
}
