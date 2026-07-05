namespace ScadaEngine.Web.Features.Ems.Models;

/// <summary>GET /EMS/api/main-meter 回應 — 主要電表基本資訊</summary>
public class EmsMainMeterDto
{
    public bool hasMainMeter { get; set; }
    public int? id { get; set; }
    public string? name { get; set; }
    public bool hasChildren { get; set; }
}

/// <summary>GET /EMS/api/main-meter-breakdown 回應 — 子迴路用電占比（圓餅圖用）</summary>
public class EmsMainMeterBreakdownDto
{
    public bool hasMainMeter { get; set; }
    public string meterName { get; set; } = string.Empty;
    /// <summary>各直接子迴路（已乘子迴路 Sign）；無子迴路時只有主要電表自己一筆</summary>
    public List<EmsBreakdownItemDto> items { get; set; } = new();
}

public class EmsBreakdownItemDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public double kwh { get; set; }
}

/// <summary>GET /EMS/api/main-meter-yoy 回應 — 去年同期比較表</summary>
public class EmsMainMeterYoyDto
{
    public bool hasMainMeter { get; set; }
    /// <summary>首列為主要電表，其後為各直接子迴路</summary>
    public List<EmsYoyRowDto> rows { get; set; } = new();
}

public class EmsYoyRowDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    /// <summary>是否為主要電表列（前端首列標示用）</summary>
    public bool isMainMeter { get; set; }
    public double currentKwh { get; set; }
    public double lastYearKwh { get; set; }
    public double diffKwh { get; set; }
    /// <summary>增減 %（相對去年同期）；去年為 0 或無資料時為 null，前端顯示 --</summary>
    public double? pctChange { get; set; }
}
