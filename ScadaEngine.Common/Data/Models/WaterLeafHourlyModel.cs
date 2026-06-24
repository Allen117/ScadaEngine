namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 冷凍噸報表 — 葉子層 hourly 預聚合資料模型（對應 WaterLeafHourly 表）。
/// 一列 = 一個葉子 SID 在某個小時的 RT·h（冷量，= AVG(RT) × 1h）。
/// 與 EnergyLeafHourly 對稱，但語意差異：水系統 RT 是瞬時值，採 AVG × 1h 積分；
/// 電表 kWh 是累計值，採 boundary 相減。
/// SampleCount &lt; 30/60 視為資料不足，整列不寫（sparse storage）；
/// 後續 catch-up 若 sample 補滿到 30 才會 UPSERT 寫入。
/// </summary>
public class WaterLeafHourlyModel
{
    public string szSID { get; set; } = string.Empty;

    /// <summary>小時起點（local time，與 HistoryData.Timestamp 同基準）</summary>
    public DateTime dtHourStart { get; set; }

    /// <summary>該小時 RT·h（冷量）= AVG(RT samples in hour) × 1h</summary>
    public double dRtHour { get; set; }

    /// <summary>該小時內 HistoryData 樣本數（Quality=1）— 用於資料完整度判定</summary>
    public int nSampleCount { get; set; }

    /// <summary>1=正常（sample 數達標）。保留欄位供未來擴充（例如標示「部分資料」）</summary>
    public int nQuality { get; set; } = 1;

    /// <summary>是否由 backfill 寫入（目前固定 false，預留給未來 MQTT 觸發補算）</summary>
    public bool isBackfilled { get; set; }

    public DateTime dtCreatedAt { get; set; }
}
