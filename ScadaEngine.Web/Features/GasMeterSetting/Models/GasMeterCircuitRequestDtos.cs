namespace ScadaEngine.Web.Features.GasMeterSetting.Models;

/// <summary>新增氣表迴路節點請求</summary>
public class CreateGasMeterCircuitDto
{
    public int? parentId { get; set; }
    public string name { get; set; } = "新迴路";
    public string? sid { get; set; }
    /// <summary>點位原始單位 → m³ 換算係數；前端綁定點位時依點位單位定案（m³/Nm³/度=1 / L=0.001）</summary>
    public double unitScale { get; set; } = 1.0;
    /// <summary>氣表累積最大值（以點位原始單位計），溢位/歸零判定用；留空表示不處理溢位</summary>
    public double? maxVolume { get; set; }
    /// <summary>對父貢獻方向：+1 / -1，預設 +1。根節點伺服器端會強制覆寫為 +1</summary>
    public int sign { get; set; } = 1;
    public string? description { get; set; }
}

/// <summary>更新氣表迴路節點請求</summary>
public class UpdateGasMeterCircuitDto
{
    public string name { get; set; } = string.Empty;
    public string? sid { get; set; }
    /// <summary>點位原始單位 → m³ 換算係數；前端綁定點位時依點位單位定案（m³/Nm³/度=1 / L=0.001）</summary>
    public double unitScale { get; set; } = 1.0;
    /// <summary>氣表累積最大值（以點位原始單位計），溢位/歸零判定用；留空表示不處理溢位</summary>
    public double? maxVolume { get; set; }
    /// <summary>對父貢獻方向：+1 / -1。根節點伺服器端會強制覆寫為 +1</summary>
    public int sign { get; set; } = 1;
    public string? description { get; set; }
}

/// <summary>單筆排序資訊（拖曳完成後整批送回）</summary>
public class GasMeterCircuitSortDto
{
    public int id { get; set; }
    public int? parentId { get; set; }
    public int sortOrder { get; set; }
}

/// <summary>氣量點位下拉清單項目</summary>
public class GasMeterSidOptionDto
{
    public string sid { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string unit { get; set; } = string.Empty;
    /// <summary>該點位單位 → m³ 換算係數（m³/Nm³/度 系=1、L 系=0.001）</summary>
    public double unitScale { get; set; } = 1.0;
    public string source { get; set; } = string.Empty; // "Modbus" / "Calculated" / "DB"
    public string coordName { get; set; } = string.Empty; // 通訊設備層：協調器名 / 計算群組名 / DB 來源名
    public string deviceName { get; set; } = string.Empty; // 子單元層：多 ID 協調器的子設備名，無子單元則為空字串
}
