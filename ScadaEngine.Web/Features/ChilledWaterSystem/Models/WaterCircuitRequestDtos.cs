namespace ScadaEngine.Web.Features.ChilledWaterSystem.Models;

/// <summary>新增水系統迴路節點請求</summary>
public class CreateWaterCircuitDto
{
    public int? parentId { get; set; }
    public string name { get; set; } = "新迴路";
    public string? sid { get; set; }
    public string? description { get; set; }
}

/// <summary>更新水系統迴路節點請求</summary>
public class UpdateWaterCircuitDto
{
    public string name { get; set; } = string.Empty;
    public string? sid { get; set; }
    public string? description { get; set; }
}

/// <summary>批次排序資訊（拖曳完成後整批送回）</summary>
public class WaterCircuitSortDto
{
    public int id { get; set; }
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
}

/// <summary>SID 下拉清單項目（與 EnergyMeter 同結構，但 source/單位過濾規則不同）</summary>
public class WaterCircuitSidOptionDto
{
    public string sid { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string unit { get; set; } = string.Empty;
    public string source { get; set; } = string.Empty; // "Modbus" / "Calculated" / "DB"
    public string deviceName { get; set; } = string.Empty;
}
