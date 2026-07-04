namespace ScadaEngine.Web.Features.OpcUaCoordinator.Models;

/// <summary>
/// OPC UA 來源頁列表項目 DTO（camelCase 直接餵前端 JSON；不含明文密碼，僅 hasPassword 旗標）
/// </summary>
public class OpcUaServerListItemDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string endpointUrl { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public bool hasPassword { get; set; }
    public int pollingInterval { get; set; }
    public int connectTimeout { get; set; }
    public bool monitorEnabled { get; set; }
    public List<OpcUaDeviceListItemDto> devices { get; set; } = new();
}

/// <summary>
/// Device 分組（顯示用）
/// </summary>
public class OpcUaDeviceListItemDto
{
    public string name { get; set; } = string.Empty;
    public List<OpcUaPointListItemDto> tags { get; set; } = new();
}

/// <summary>
/// 點位列表項目 DTO
/// </summary>
public class OpcUaPointListItemDto
{
    public string sid { get; set; } = string.Empty;
    public int seq { get; set; }
    public string name { get; set; } = string.Empty;
    public string tagName { get; set; } = string.Empty;
    public string controlType { get; set; } = string.Empty;
    public float ratio { get; set; } = 1.0f;
    public string unit { get; set; } = string.Empty;
    public float? min { get; set; }
    public float? max { get; set; }
}
