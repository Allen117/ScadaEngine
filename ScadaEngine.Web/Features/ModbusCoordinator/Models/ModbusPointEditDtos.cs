namespace ScadaEngine.Web.Features.ModbusCoordinator.Models;

/// <summary>
/// 單一 Modbus 點位欄位 — 對應 Engine Modbus JSON 的 Tags[] 元素，全部字串型別與檔案格式一致
/// </summary>
public class ModbusPointDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Ratio { get; set; } = "1";
    public string Unit { get; set; } = string.Empty;
    public string Min { get; set; } = string.Empty;
    public string Max { get; set; } = string.Empty;
}

/// <summary>
/// GET /ModbusCoordinator/Points/{name} 回應內容 — 設備唯讀資訊 + 點位清單
/// </summary>
public class ModbusPointsFileModel
{
    public string CoordinatorName { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; } = 502;
    public string ModbusId { get; set; } = string.Empty;
    public int ConnectTimeout { get; set; } = 1000;
    public List<ModbusPointDto> Points { get; set; } = new();
}

/// <summary>
/// POST /ModbusCoordinator/UpdatePoints 請求 — 點位陣列必須與檔案內數量、順序完全一致（僅准原地改欄位）
/// </summary>
public class UpdateModbusPointsRequest
{
    public string CoordinatorName { get; set; } = string.Empty;
    public List<ModbusPointDto> Points { get; set; } = new();
}

/// <summary>點位更新失敗原因分類（Controller 據此決定 HTTP 狀態碼與訊息 key）</summary>
public enum ModbusPointsUpdateError
{
    None = 0,
    FileNotFound = 1,
    StructureChanged = 2,
    InvalidPoint = 3,
    WriteFailed = 4,
}

/// <summary>單一點位的變更摘要 — 供 EventLog 稽核（一點位一筆）</summary>
public class ModbusPointChange
{
    /// <summary>0-based 陣列索引，對應 SID 尾碼 -S{nTagIndex+1}</summary>
    public int nTagIndex { get; set; }

    /// <summary>變更後的點位名稱</summary>
    public string szPointName { get; set; } = string.Empty;

    /// <summary>欄位變更摘要，如「Address: 30513 → 30514, Ratio: 1 → 0.1」</summary>
    public string szSummary { get; set; } = string.Empty;
}

/// <summary>點位更新結果</summary>
public class ModbusPointsUpdateResult
{
    public bool isSuccess { get; set; }
    public ModbusPointsUpdateError nError { get; set; } = ModbusPointsUpdateError.None;

    /// <summary>驗證失敗時的 1-based 列號（0 表示與列無關）</summary>
    public int nInvalidRow { get; set; }

    /// <summary>驗證失敗原因（技術欄位名，非 i18n）</summary>
    public string szInvalidReason { get; set; } = string.Empty;

    /// <summary>實際有變更的點位清單（空清單 = 無變更、未寫檔）</summary>
    public List<ModbusPointChange> changes { get; set; } = new();
}
