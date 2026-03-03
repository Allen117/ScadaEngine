using System.Text.Json.Serialization;

namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 最新資料模型類別，對應 LatestData 資料表結構
/// 依據 DatabaseSchema.json 定義的格式實作
/// </summary>
public class LatestDataModel
{
    /// <summary>
    /// 點位系統識別碼，格式為 XXX-SN (XXX=DatabaseId*65536+ModbusId*256+1, N為Tag順序)
    /// 對應資料表主鍵
    /// </summary>
    [JsonPropertyName("sid")]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 點位數值 (經過 Ratio 轉換後的實體物理量)
    /// </summary>
    [JsonPropertyName("value")]
    public float fValue { get; set; }

    /// <summary>
    /// 資料更新時間戳記
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime dtTimestamp { get; set; }

    /// <summary>
    /// 資料品質狀態 (1=Good, 0=Bad)
    /// </summary>
    [JsonPropertyName("quality")]
    public int nQuality { get; set; } = 1;

    /// <summary>
    /// 預設建構函式
    /// </summary>
    public LatestDataModel() 
    { 
        // 設定為當前時間，去除毫秒部分
        var now = DateTime.Now;
        dtTimestamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
    }

    /// <summary>
    /// 從即時資料模型建立最新資料模型
    /// </summary>
    /// <param name="realtimeData">即時資料模型</param>
    public LatestDataModel(RealtimeDataModel realtimeData)
    {
        szSID = realtimeData.szSID;
        fValue = realtimeData.fValue;
        nQuality = realtimeData.szQuality?.ToLower() == "good" ? 1 : 0;
        
        // 設定為秒級別時間戳（去除毫秒）
        var sourceTime = realtimeData.dtTimestamp;
        dtTimestamp = new DateTime(sourceTime.Year, sourceTime.Month, sourceTime.Day, 
                                 sourceTime.Hour, sourceTime.Minute, sourceTime.Second);
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