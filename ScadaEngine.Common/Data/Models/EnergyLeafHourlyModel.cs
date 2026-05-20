namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 能源管理 — 葉子層 hourly 預聚合資料模型（對應 EnergyLeafHourly 表）。
/// 一列 = 一個葉子 SID 在某個小時的 kWh 增量（已套 MaxKwh 溢位規則，未套 sign）。
/// Quality=0 表示「掉線事件 transition」（只缺一邊邊界值），DeltaKwh=0。
/// 兩邊都缺 → 不寫該列（sparse storage）。
/// </summary>
public class EnergyLeafHourlyModel
{
    public string szSID { get; set; } = string.Empty;

    /// <summary>小時起點（local time，與 HistoryData.Timestamp 同基準）</summary>
    public DateTime dtHourStart { get; set; }

    /// <summary>該小時 kWh 增量（已套溢位規則，未套 sign）。Quality=0 時恆為 0。</summary>
    public double dDeltaKwh { get; set; }

    /// <summary>1=正常累計、0=掉線 transition（只缺一邊邊界）</summary>
    public int nQuality { get; set; } = 1;

    /// <summary>是否觸發 MaxKwh 溢位修正</summary>
    public bool isRolledOver { get; set; }

    public DateTime dtCreatedAt { get; set; }
}
