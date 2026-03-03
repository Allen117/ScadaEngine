using System.Text.Json.Serialization;

namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 即時資料模型類別，用於 SCADA 系統的即時資料存取與 MQTT 發布
/// 依據 SCADA 架構規範設計，支援 Modbus 通訊與即時監控
/// </summary>
public class RealtimeDataModel
{
    /// <summary>
    /// 資料時間戳記
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime dtTimestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 點位唯一識別碼 (SID)，格式為 XXX-SN
    /// </summary>
    [JsonPropertyName("sid")]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 點位名稱 (對應 Modbus 設定檔中的 Name 欄位)
    /// </summary>
    [JsonPropertyName("tagName")]
    public string szTagName { get; set; } = string.Empty;

    /// <summary>
    /// 計算後的物理量數值 (經過 Ratio 縮放)
    /// </summary>
    [JsonPropertyName("value")]
    public float fValue { get; set; }

    /// <summary>
    /// 物理單位 (如 ℃, %, V 等)
    /// </summary>
    [JsonPropertyName("unit")]
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 資料品質狀態 (Good, Bad, Uncertain)
    /// </summary>
    [JsonPropertyName("quality")]
    public string szQuality { get; set; } = "Good";

    /// <summary>
    /// 設備 IP 地址 (用於識別資料來源)
    /// </summary>
    [JsonPropertyName("deviceIP")]
    public string szDeviceIP { get; set; } = string.Empty;

    /// <summary>
    /// Modbus 原始地址 (用於 MQTT 發布與除錯)
    /// </summary>
    [JsonPropertyName("address")]
    public int nAddress { get; set; } = 0;

    /// <summary>
    /// Coordinator 名稱 (對應 ModbusCoordinator.Name，即 JSON 設定檔名稱)
    /// </summary>
    [JsonPropertyName("coordinatorName")]
    public string szCoordinatorName { get; set; } = string.Empty;

    /// <summary>
    /// 指示資料讀取是否成功 (用於統計成功率，不包含在 MQTT 發布中)
    /// </summary>
    [JsonIgnore]
    public bool IsReadSuccess { get; set; } = true;

    /// <summary>
    /// 預設建構函式
    /// </summary>
    public RealtimeDataModel()
    {
        dtTimestamp = DateTime.Now;
    }

    /// <summary>
    /// 將即時資料轉換為 MQTT 發布用的物件
    /// </summary>
    /// <returns>符合 MQTT 發布格式的匿名物件</returns>
    public object ToMqttPayload()
    {
        return new
        {
            sid = szSID,
            coordinatorName = szCoordinatorName,
            name = szTagName,
            value = (double)fValue,
            unit = szUnit,
            quality = szQuality,
            timestamp = new DateTimeOffset(dtTimestamp).ToUnixTimeMilliseconds(),
            address = nAddress
        };
    }

    /// <summary>
    /// 轉換為字串表示 (用於日誌與除錯)
    /// </summary>
    /// <returns>物件的字串描述</returns>
    public override string ToString()
    {
        return $"[{dtTimestamp:yyyy-MM-dd HH:mm:ss}] {szSID} ({szTagName}): {fValue} {szUnit} [Quality: {szQuality}]";
    }
}