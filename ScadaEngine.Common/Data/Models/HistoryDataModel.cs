using System.Text.Json.Serialization;

namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 歷史資料模型類別，對應 HistoryData 資料表結構
/// 依據 DatabaseSchema.json 定義的格式實作
/// </summary>
public class HistoryDataModel
{
    /// <summary>
    /// 唯一識別碼 (自動遞增主鍵)
    /// </summary>
    [JsonPropertyName("id")]
    public int nId { get; set; }

    /// <summary>
    /// 點位系統識別碼，格式為 XXX-SN (XXX=DatabaseId*65536+ModbusId*256+1, N為Tag順序)
    /// 對應外部索引鍵
    /// </summary>
    [JsonPropertyName("sid")]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 點位數值 (經過 Ratio 轉換後的實體物理量)
    /// </summary>
    [JsonPropertyName("value")]
    public float fValue { get; set; }

    /// <summary>
    /// 資料建立時間戳記
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime dtTimestamp { get; set; }

    /// <summary>
    /// 資料品質狀態 (1=Good, 0=Bad)
    /// 對應原始字串狀態："Good", "Bad", "Uncertain"
    /// </summary>
    [JsonPropertyName("quality")]
    public int nQuality { get; set; } = 1;

    /// <summary>
    /// 預設建構函式
    /// </summary>
    public HistoryDataModel()
    {
        // 設定為當前時間
        dtTimestamp = DateTime.Now;
    }

    /// <summary>
    /// 從即時資料建構歷史資料
    /// </summary>
    /// <param name="szSID">點位 SID</param>
    /// <param name="fValue">數值</param>
    /// <param name="szQuality">品質狀態字串 ("Good", "Bad", "Uncertain")</param>
    /// <param name="dtTimestamp">時間戳記 (可選，預設為當前時間)</param>
    public HistoryDataModel(string szSID, float fValue, string szQuality, DateTime? dtTimestamp = null)
    {
        this.szSID = szSID;
        this.fValue = fValue;
        this.nQuality = szQuality?.ToLower() switch
        {
            "good" => 1,
            "bad" => 0,
            "uncertain" => 0,
            _ => 0 // 預設為不良品質
        };
        this.dtTimestamp = dtTimestamp ?? DateTime.Now;
    }

    /// <summary>
    /// 轉換為字串表示
    /// </summary>
    /// <returns>物件的字串描述</returns>
    public override string ToString()
    {
        var szQualityText = nQuality == 1 ? "Good" : "Bad";
        return $"[{dtTimestamp:yyyy-MM-dd HH:mm:ss}] {szSID}: {fValue} (Quality: {szQualityText})";
    }
}