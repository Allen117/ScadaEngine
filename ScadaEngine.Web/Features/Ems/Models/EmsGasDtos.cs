namespace ScadaEngine.Web.Features.Ems.Models;

/// <summary>用氣量長條圖 API 回應（/EMS/api/gas-usage）</summary>
public class EmsGasUsageDto
{
    public List<string> labels { get; set; } = new();
    public List<double> values { get; set; } = new();

    /// <summary>任一葉子在區間內有缺資料/斷線警告</summary>
    public bool hasWarning { get; set; }
}

/// <summary>根迴路直接子迴路用氣拆解 API 回應（/EMS/api/gas-breakdown，圓餅圖用）</summary>
public class EmsGasBreakdownDto
{
    /// <summary>是否已建立氣表根迴路</summary>
    public bool hasRoot { get; set; }

    /// <summary>任一子迴路在區間內有缺資料/斷線警告</summary>
    public bool hasWarning { get; set; }

    public List<EmsGasBreakdownItemDto> items { get; set; } = new();
}

/// <summary>拆解明細單筆（子迴路區間總 m³）</summary>
public class EmsGasBreakdownItemDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public double m3 { get; set; }
}
