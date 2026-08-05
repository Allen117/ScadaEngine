namespace ScadaEngine.Web.Features.GasCostReport.Models;

/// <summary>氣費報表頁 ViewModel（帶今日供前端預設區間）</summary>
public class GasCostReportViewModel
{
    public DateTime dtToday { get; set; } = DateTime.Today;
}

/// <summary>級距明細一列 — 該期用氣量落在此級距的度數與金額（1 度 = 1 m³）</summary>
public class GasCostTierRowDto
{
    public int from { get; set; }

    /// <summary>級距上限（含），null = 「以上」</summary>
    public int? to { get; set; }

    /// <summary>每度單價（元/度）</summary>
    public double price { get; set; }

    /// <summary>落在此級距的度數（m³）</summary>
    public double sliceM3 { get; set; }

    /// <summary>此級距金額（元）</summary>
    public double sliceCost { get; set; }
}

/// <summary>本期氣費狀態（EMS 氣費狀態卡用）</summary>
public class GasCostStatusDto
{
    public bool hasPlan { get; set; }
    public int circuitId { get; set; }
    public string circuitName { get; set; } = string.Empty;

    public string periodLabel { get; set; } = string.Empty;
    public DateTime periodStart { get; set; }
    public DateTime periodEndExclusive { get; set; }

    public double totalM3 { get; set; }
    public double totalCost { get; set; }

    public string planId { get; set; } = string.Empty;
    public string planName { get; set; } = string.Empty;
    public string effectiveDate { get; set; } = string.Empty;

    public List<GasCostTierRowDto> tiers { get; set; } = new();

    /// <summary>用氣量彙總含品質警示（來源 GasUsageReportService isHasWarning）</summary>
    public bool isStale { get; set; }
}

/// <summary>氣費報表一期一列 — 期別用氣量套當期生效方案分段累進</summary>
public class GasCostPeriodRow
{
    public int periodYear { get; set; }
    public int periodMonth { get; set; }
    public string periodLabel { get; set; } = string.Empty;

    public DateTime periodStart { get; set; }

    /// <summary>期別結束日（含，顯示用）</summary>
    public DateTime periodEnd { get; set; }

    public double totalM3 { get; set; }
    public double totalCost { get; set; }

    public string planId { get; set; } = string.Empty;
    public string planName { get; set; } = string.Empty;

    /// <summary>該期用氣量彙總含品質警示</summary>
    public bool isStale { get; set; }

    public List<GasCostTierRowDto> tiers { get; set; } = new();
}

/// <summary>氣費報表查詢 / 匯出條件（fromYm / toYm 格式 yyyy-MM）</summary>
public class GasCostReportRequestDto
{
    public int circuitId { get; set; }
    public string fromYm { get; set; } = string.Empty;
    public string toYm { get; set; } = string.Empty;
}
