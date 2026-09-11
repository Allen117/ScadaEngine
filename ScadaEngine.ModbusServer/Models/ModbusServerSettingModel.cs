using System.Text.Json.Serialization;

namespace ScadaEngine.ModbusServer.Models;

/// <summary>
/// 對應 Setting/ModbusServerSetting.json
/// </summary>
public class ModbusServerSettingModel
{
    /// <summary>Modbus TCP 監聽位址（0.0.0.0 = 全部介面）</summary>
    [JsonPropertyName("ModbusListenAddress")]
    public string szModbusListenAddress { get; set; } = "0.0.0.0";

    /// <summary>Modbus TCP 監聽埠（標準 502）</summary>
    [JsonPropertyName("ModbusListenPort")]
    public int nModbusListenPort { get; set; } = 502;

    /// <summary>Modbus Unit ID（TCP 模式下多數客戶端可任填，仍保留設定）</summary>
    [JsonPropertyName("UnitId")]
    public int nUnitId { get; set; } = 1;

    /// <summary>float32 word order："CDAB"（ModScan Floating Pt，預設）或 "ABCD"（ModScan Swapped FP）</summary>
    [JsonPropertyName("WordOrder")]
    public string szWordOrder { get; set; } = "CDAB";

    /// <summary>品質不良時的值表示："NaN"（預設，0x7FC00000）或 "HoldLast"（保持最後值）</summary>
    [JsonPropertyName("BadQualityMode")]
    public string szBadQualityMode { get; set; } = "NaN";

    /// <summary>內建瀏覽網頁 HTTP 埠（預設 5041 — 5040 在 Win10/11 幾乎必被內建 CDPSvc 占用）</summary>
    [JsonPropertyName("WebPort")]
    public int nWebPort { get; set; } = 5041;

    /// <summary>DBLatestData 輪詢間隔（秒）— DB 來源點位不走 MQTT</summary>
    [JsonPropertyName("DbPollSeconds")]
    public int nDbPollSeconds { get; set; } = 2;

    /// <summary>MQTT 來源點位超過此秒數未更新視為斷線（值轉 NaN；DB 來源不適用）</summary>
    [JsonPropertyName("StaleSeconds")]
    public int nStaleSeconds { get; set; } = 300;

    /// <summary>快取掃入 register buffer 的週期（毫秒）</summary>
    [JsonPropertyName("RegisterSweepMs")]
    public int nRegisterSweepMs { get; set; } = 1000;

    /// <summary>各來源類型的位址段配置（key = Modbus / Calc / Db / OpcUa）</summary>
    [JsonPropertyName("Segments")]
    public Dictionary<string, AddressSegmentModel> Segments { get; set; } = new()
    {
        ["Modbus"] = new AddressSegmentModel { nStart = 0, nCapacityPoints = 5000 },
        ["Calc"] = new AddressSegmentModel { nStart = 10000, nCapacityPoints = 2000 },
        ["Db"] = new AddressSegmentModel { nStart = 14000, nCapacityPoints = 3000 },
        ["OpcUa"] = new AddressSegmentModel { nStart = 20000, nCapacityPoints = 3000 },
    };

    public bool IsHoldLast => string.Equals(szBadQualityMode, "HoldLast", StringComparison.OrdinalIgnoreCase);
}

/// <summary>位址段：起始 register 位址（0-based，偶數）與可容納點數（每點 2 registers）</summary>
public class AddressSegmentModel
{
    [JsonPropertyName("Start")]
    public int nStart { get; set; }

    [JsonPropertyName("CapacityPoints")]
    public int nCapacityPoints { get; set; }

    /// <summary>段尾（不含）的 register 位址</summary>
    [JsonIgnore]
    public int nEndExclusive => nStart + nCapacityPoints * 2;
}
