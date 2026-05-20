namespace ScadaEngine.Web.Features.EnergyMeter.Models;

/// <summary>新增節點請求</summary>
public class CreateCircuitDto
{
    public int? parentId { get; set; }
    public string name { get; set; } = "新迴路";
    public string? sid { get; set; }
    public double? maxKwh { get; set; }
    /// <summary>對父貢獻方向：+1 / -1，預設 +1。根節點伺服器端會強制覆寫為 +1</summary>
    public int sign { get; set; } = 1;
    public string? description { get; set; }
}

/// <summary>更新節點請求</summary>
public class UpdateCircuitDto
{
    public string name { get; set; } = string.Empty;
    public string? sid { get; set; }
    public double? maxKwh { get; set; }
    /// <summary>對父貢獻方向：+1 / -1。根節點伺服器端會強制覆寫為 +1</summary>
    public int sign { get; set; } = 1;
    public string? description { get; set; }
}

/// <summary>單筆排序資訊（拖曳完成後整批送回）</summary>
public class CircuitSortDto
{
    public int id { get; set; }
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
}

/// <summary>SID 下拉清單項目</summary>
public class CircuitSidOptionDto
{
    public string sid { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string unit { get; set; } = string.Empty;
    public string source { get; set; } = string.Empty; // "Modbus" or "Calculated"
    public string deviceName { get; set; } = string.Empty; // 設備（子設備）名稱，無對應則為協調器名稱或群組
}
