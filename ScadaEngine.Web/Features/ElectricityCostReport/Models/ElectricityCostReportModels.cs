namespace ScadaEngine.Web.Features.ElectricityCostReport.Models;

/// <summary>電費報表頁 ViewModel（帶今日供前端預設區間）</summary>
public class ElectricityCostReportViewModel
{
    public DateTime dtToday { get; set; } = DateTime.Today;
}

/// <summary>電費報表查詢條件（協定同用電報表 EnergyReportRequestDto）</summary>
public class CostReportRequestDto
{
    public int circuitId { get; set; }

    /// <summary>hour / day / month / year（月 = 月結期別）</summary>
    public string granularity { get; set; } = "day";

    /// <summary>期間起點（hour 截整點 / day 截 00:00 / month 截當月 1 日 / year 截 1/1）</summary>
    public DateTime start { get; set; }

    /// <summary>期間終點（含，展開規則同用電報表）</summary>
    public DateTime end { get; set; }
}

/// <summary>
/// 電費報表查詢結果 — 對應某迴路在某粒度下的 N 個 bucket（kWh + 電費雙值）。
/// tou / flat bucket 電費 = ElectricityCostHourly.Cost × EffectiveSign 直接加總（精確）；
/// progressive 以「期別累計 kWh 套級距」後按 bucket kWh 占比分攤（isEstimated 註記）。
/// 金額只含流動電費 — 不含基本電費與簡易型超額加價。
/// </summary>
public class CostReportResult
{
    public int circuitId { get; set; }
    public string circuitName { get; set; } = string.Empty;

    /// <summary>hour / day / month / year</summary>
    public string granularity { get; set; } = string.Empty;

    /// <summary>查詢起點（含，顯示用）</summary>
    public DateTime start { get; set; }

    /// <summary>查詢終點（含，顯示用）</summary>
    public DateTime end { get; set; }

    /// <summary>區間內是否有任何計價資料 — false 時前端顯示「請先重新計算」引導而非全 0 圖</summary>
    public bool hasData { get; set; }

    /// <summary>是否含 progressive 級距分攤金額（非月粒度或子迴路時為估算）</summary>
    public bool isEstimated { get; set; }

    public List<CostReportBucketDto> buckets { get; set; } = new();

    public double totalKwh { get; set; }
    public double totalCost { get; set; }

    /// <summary>直接子迴路拆解（僅 Excel 匯出使用；查詢 API 為空）</summary>
    public List<CostReportChildSeries> children { get; set; } = new();
}

/// <summary>單一 bucket 的時間標籤 + kWh + 電費</summary>
public class CostReportBucketDto
{
    public string label { get; set; } = string.Empty;
    public double kwh { get; set; }
    public double cost { get; set; }
}

/// <summary>父迴路下的單一直接子節點 series — Excel 多欄展開用，與父 buckets 同 index 對齊</summary>
public class CostReportChildSeries
{
    public int circuitId { get; set; }
    public string name { get; set; } = string.Empty;
    public List<double> costPerBucket { get; set; } = new();
    public double totalCost { get; set; }
}
