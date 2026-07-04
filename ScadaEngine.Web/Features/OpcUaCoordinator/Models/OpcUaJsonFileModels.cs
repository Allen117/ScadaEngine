using System.Text.Json.Serialization;

namespace ScadaEngine.Web.Features.OpcUaCoordinator.Models;

/// <summary>
/// OpcUaPoint/*.json 檔案結構（Web 回寫用）— 欄位需與 Engine OpcUaConfigLoader 的 DTO 對齊。
/// Ratio 以字串輸出（比照 Modbus 慣例，Engine loader 字串/數字皆可解析）。
/// </summary>
public class OpcUaJsonFile
{
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PollingInterval { get; set; } = 1000;
    public int ConnectTimeout { get; set; } = 5000;
    public bool MonitorEnabled { get; set; } = true;

    /// <summary>0 = 依 Server OperationLimits（讀不到時 Engine 預設 500）</summary>
    public int MaxNodesPerRead { get; set; } = 0;

    /// <summary>
    /// 下一個可用 Seq（單調遞增、刪除不回收）— Web 配號依據，Engine loader 忽略此欄。
    /// 確保刪掉最大號點位後新增不會重用舊 Seq（SID 被 HistoryData/警報/計算點位引用不可漂移）。
    /// </summary>
    public int NextSeq { get; set; } = 1;

    public List<OpcUaJsonDevice> Devices { get; set; } = new();
}

public class OpcUaJsonDevice
{
    public string Name { get; set; } = string.Empty;
    public List<OpcUaJsonTag> Tags { get; set; } = new();
}

public class OpcUaJsonTag
{
    public int Seq { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string ControlType { get; set; } = string.Empty;

    /// <summary>字串格式，例 "1"、"0.1"（比照 Modbus）</summary>
    public string Ratio { get; set; } = "1";

    public string Unit { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Min { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Max { get; set; }
}
