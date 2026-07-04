namespace ScadaEngine.Web.Features.OpcUaCoordinator.Models;

/// <summary>
/// 新增/編輯 OPC UA Server 請求（Id=0 為新增；編輯時 Name 不可變更）
/// Password 空字串 = 保留原密碼（新增時空字串 = 無密碼/Anonymous 搭配空 Username）
/// </summary>
public class SaveOpcUaServerRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PollingInterval { get; set; } = 1000;
    public int ConnectTimeout { get; set; } = 5000;
    public bool MonitorEnabled { get; set; } = true;
}

/// <summary>
/// 刪除 OPC UA Server 請求
/// </summary>
public class DeleteOpcUaServerRequest
{
    public int Id { get; set; }
}

/// <summary>
/// 全量儲存指定 Server 的 Devices + 點位（Seq=0 表示新點位，由後端配號）
/// </summary>
public class SaveOpcUaPointsRequest
{
    public int Id { get; set; }
    public List<OpcUaDeviceEditDto> Devices { get; set; } = new();
}

public class OpcUaDeviceEditDto
{
    public string Name { get; set; } = string.Empty;
    public List<OpcUaTagEditDto> Tags { get; set; } = new();
}

public class OpcUaTagEditDto
{
    /// <summary>0 = 新點位（後端配號）；>0 = 既有點位（Seq 不變，保 SID 穩定）</summary>
    public int Seq { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string ControlType { get; set; } = string.Empty;
    public float Ratio { get; set; } = 1.0f;
    public string Unit { get; set; } = string.Empty;
    public float? Min { get; set; }
    public float? Max { get; set; }
}

/// <summary>
/// 測試讀取 NodeId 請求 — Password 空且 ServerId>0 時使用該 Server 已儲存的密碼
/// </summary>
public class TestReadOpcUaRequest
{
    public int ServerId { get; set; }
    public string EndpointUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public float Ratio { get; set; } = 1.0f;
}
