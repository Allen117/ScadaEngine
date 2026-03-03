using System.Text.Json.Serialization;

namespace ScadaEngine.Engine.Models;

/// <summary>
/// MQTT 控制指令訊息內容模型
/// </summary>
public class MqttControlMessageModel
{
    /// <summary>
    /// 訊息 ID (UUID-v4 或唯一字串)
    /// </summary>
    [JsonPropertyName("mid")]
    public string szMid { get; set; } = string.Empty;

    /// <summary>
    /// 時間戳記 (Unix 毫秒)
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long nTimestamp { get; set; }

    /// <summary>
    /// 控制數值 (字串格式)
    /// </summary>
    [JsonPropertyName("value")]
    public string szValue { get; set; } = string.Empty;

    /// <summary>
    /// 物理單位
    /// </summary>
    [JsonPropertyName("unit")]
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 取得數值的雙精度表示
    /// </summary>
    /// <returns>轉換後的雙精度數值，若轉換失敗則回傳 0.0</returns>
    public double GetValueAsDouble()
    {
        if (double.TryParse(szValue, out var dResult))
        {
            return dResult;
        }
        return 0.0;
    }

    /// <summary>
    /// 取得時間戳記的 DateTime 表示
    /// </summary>
    /// <returns>轉換後的 DateTime 物件</returns>
    public DateTime GetTimestampAsDateTime()
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(nTimestamp).DateTime;
    }
}

/// <summary>
/// 完整的控制指令資訊 (包含解析後的 CID 資訊)
/// </summary>
public class MqttControlCommandModel
{
    /// <summary>
    /// 原始 CID (等同 SID 格式)
    /// </summary>
    public string szCID { get; set; } = string.Empty;

    /// <summary>
    /// 訊息 ID (UUID-v4 或唯一字串)
    /// </summary>
    public string szMid { get; set; } = string.Empty;

    /// <summary>
    /// 控制數值
    /// </summary>
    public double dValue { get; set; }

    /// <summary>
    /// 原始字串格式的控制數值
    /// </summary>
    public string szOriginalValue { get; set; } = string.Empty;

    /// <summary>
    /// 物理單位
    /// </summary>
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 訊息時間戳記 (Unix 毫秒)
    /// </summary>
    public long nMessageTimestamp { get; set; }

    /// <summary>
    /// 接收時間
    /// </summary>
    public DateTime dtReceived { get; set; } = DateTime.Now;

    /// <summary>
    /// 來源主題
    /// </summary>
    public string szSourceTopic { get; set; } = string.Empty;

    /// <summary>
    /// 解析後的資料庫 ID
    /// </summary>
    public int nDatabaseId { get; set; }

    /// <summary>
    /// 解析後的 Modbus ID
    /// </summary>
    public int nModbusId { get; set; }

    /// <summary>
    /// 解析後的點位索引
    /// </summary>
    public int nTagIndex { get; set; }

    /// <summary>
    /// 是否已成功解析 CID
    /// </summary>
    public bool isParsed { get; set; }

    /// <summary>
    /// 解析錯誤訊息 (如果有)
    /// </summary>
    public string szParseError { get; set; } = string.Empty;

    /// <summary>
    /// 取得訊息時間戳記的 DateTime 表示
    /// </summary>
    /// <returns>轉換後的 DateTime 物件</returns>
    public DateTime GetMessageTimestampAsDateTime()
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(nMessageTimestamp).DateTime;
    }
}