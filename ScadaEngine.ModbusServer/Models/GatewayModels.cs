using System.Text.Json.Serialization;

namespace ScadaEngine.ModbusServer.Models;

/// <summary>點位來源類型 — 決定落在哪個位址段</summary>
public enum PointSource
{
    Modbus,
    Calc,
    Db,
    OpcUa,
}

/// <summary>掃描四類點位表得到的目錄項目</summary>
public class CatalogPointModel
{
    public string szSID { get; set; } = "";
    public string szName { get; set; } = "";
    public string szUnit { get; set; } = "";
    public PointSource Source { get; set; }

    /// <summary>來源端原始位址（Modbus=Address、OPC UA=TagName、DB=DB{CoordinatorId}#{Sequence}、Calc=空）</summary>
    public string szRawAddress { get; set; } = "";
}

/// <summary>AddressMap.json 持久化根結構</summary>
public class AddressMapModel
{
    [JsonPropertyName("version")]
    public int nVersion { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public DateTime dtUpdatedAt { get; set; }

    [JsonPropertyName("entries")]
    public List<AddressMapEntryModel> Entries { get; set; } = new();
}

/// <summary>
/// 一筆 SID ↔ Modbus 位址對照。
/// szSID / szSource / nAddress 一經配定永不變更（append-only）；
/// szName / szUnit / szRawAddress 為輔助資訊，每次掃描時刷新。
/// </summary>
public class AddressMapEntryModel
{
    [JsonPropertyName("sid")]
    public string szSID { get; set; } = "";

    [JsonPropertyName("source")]
    public string szSource { get; set; } = "";

    [JsonPropertyName("address")]
    public int nAddress { get; set; }

    [JsonPropertyName("name")]
    public string szName { get; set; } = "";

    [JsonPropertyName("unit")]
    public string szUnit { get; set; } = "";

    [JsonPropertyName("rawAddress")]
    public string szRawAddress { get; set; } = "";
}

/// <summary>即時值快取項目（RealtimeCacheService 內部）</summary>
public class RealtimeValueModel
{
    public double dValue { get; set; }
    public string szQuality { get; set; } = "NO_DATA";
    public DateTime dtTimestamp { get; set; } = DateTime.MinValue;
    public bool hasData { get; set; }

    /// <summary>DB 來源：SQL 讀取成功即視為新鮮，不做 stale 判斷</summary>
    public bool isFreshBypass { get; set; }
}
