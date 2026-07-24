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

/// <summary>GET /EMS/api/main-meter-cost-yoy 回應 — 流動電費本期 vs 去年同期比較表</summary>
public class EmsMainMeterCostYoyDto
{
    public bool hasMainMeter { get; set; }
    /// <summary>首列為主要電表，其後為各直接子迴路</summary>
    public List<EmsCostYoyRowDto> rows { get; set; } = new();
    /// <summary>任一列為 progressive 占比分攤估算值（前端可標註「估算」）</summary>
    public bool isEstimated { get; set; }
}

public class EmsCostYoyRowDto
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    /// <summary>是否為主要電表列（前端首列標示用）</summary>
    public bool isMainMeter { get; set; }
    /// <summary>本期流動電費（元）</summary>
    public double currentCost { get; set; }
    /// <summary>去年同期流動電費（元）</summary>
    public double lastYearCost { get; set; }
    /// <summary>差異（元，本期 − 去年同期）</summary>
    public double diffCost { get; set; }
    /// <summary>增減 %（相對去年同期）；去年為 0 或無資料時為 null，前端顯示 --</summary>
    public double? pctChange { get; set; }
}

/// <summary>GET /EMS/api/main-meter-values 回應 — 虛擬主要電表的 V/I/P/PF 聚合值（實體主表不走此 API）</summary>
public class EmsMainMeterValuesDto
{
    /// <summary>電壓（V）— 取第一顆有 VoltageSID 的葉子；無綁定或全 STALE/BAD 回 null</summary>
    public double? voltage { get; set; }
    /// <summary>電流（A）— Σ (I_i × sign_i)；無有效樣本回 null</summary>
    public double? current { get; set; }
    /// <summary>功率（單位取自子孫葉子 PowerSID 的 unit）— Σ (P_i × sign_i)；無有效樣本回 null</summary>
    public double? power { get; set; }
    /// <summary>功因（0~1）— ΣP_pf / √(ΣP_pf² + ΣQ²)，僅同時有 P/PF 的葉子入計；無有效樣本回 null</summary>
    public double? powerFactor { get; set; }
}
