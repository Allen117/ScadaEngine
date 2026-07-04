namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// OPC UA 來源 Server（Coordinator）資料模型，對應 OpcUaCoordinator 表。
/// 一個 Coordinator = 一個 OPC UA Server = 一個 OpcUaPoint/*.json 檔。
/// </summary>
public class OpcUaCoordinatorModel
{
    /// <summary>
    /// 自動遞增主鍵（SID 中的 {CoordinatorId} 即為此值）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Coordinator 名稱（= JSON 檔名，UNIQUE，UPSERT by Name 保 Id 穩定）
    /// </summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// OPC UA Endpoint，例 opc.tcp://192.168.1.10:4840
    /// </summary>
    public string szEndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// 連線帳號（空字串 = Anonymous）
    /// </summary>
    public string szUsername { get; set; } = string.Empty;

    /// <summary>
    /// 連線密碼（明文，比照 dbSetting.json 慣例；Web API 不回傳此欄）
    /// </summary>
    public string szPassword { get; set; } = string.Empty;

    /// <summary>
    /// 輪詢間隔（毫秒），下限 200ms（OpcUaCommunicationService 內 clamp）
    /// </summary>
    public int nPollingInterval { get; set; } = 1000;

    /// <summary>
    /// 連線/操作逾時（毫秒）
    /// </summary>
    public int nConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// 是否啟用監控
    /// </summary>
    public bool isMonitorEnabled { get; set; } = true;

    /// <summary>
    /// 建立時間
    /// </summary>
    public DateTime dtCreatedAt { get; set; }

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(szName)
            && !string.IsNullOrWhiteSpace(szEndpointUrl);
    }
}

/// <summary>
/// OPC UA 來源點位資料模型，對應 OpcUaPoints 表。
/// SID 格式：OPC{CoordinatorId}-S{Sequence}，Sequence 由 Web 配號並持久化於 JSON（刪除不回收）。
/// </summary>
public class OpcUaPointModel
{
    /// <summary>
    /// 點位 SID，例 'OPC1-S5'（主鍵）
    /// </summary>
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 所屬 Coordinator Id
    /// </summary>
    public int nCoordinatorId { get; set; }

    /// <summary>
    /// 所屬 Device 分組名稱（顯示分組 + 批次讀取對齊 Device 邊界用）
    /// </summary>
    public string szDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 序號（同 Coordinator 內遞增、刪除不回收），持久化於 JSON 的 Seq
    /// </summary>
    public int nSequence { get; set; }

    /// <summary>
    /// 點位名稱
    /// </summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// OPC UA NodeId 字串，例 'ns=2;s=D1.T'
    /// </summary>
    public string szTagName { get; set; } = string.Empty;

    /// <summary>
    /// 控制類型：''（唯讀）/ 'AO'（類比寫入）/ 'DO'（數位寫入 0/1）
    /// </summary>
    public string szControlType { get; set; } = string.Empty;

    /// <summary>
    /// 倍數：工程值 = 原始值 × Ratio；寫回原始值 = 輸入值 ÷ Ratio
    /// </summary>
    public float fRatio { get; set; } = 1.0f;

    /// <summary>
    /// 物理單位
    /// </summary>
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 顯示下限（選填）
    /// </summary>
    public float? fMin { get; set; }

    /// <summary>
    /// 顯示上限（選填）
    /// </summary>
    public float? fMax { get; set; }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(szSID) || string.IsNullOrWhiteSpace(szName))
            return false;
        if (string.IsNullOrWhiteSpace(szTagName))
            return false;
        if (nSequence < 1)
            return false;
        if (fRatio == 0f)
            return false;
        return true;
    }
}
