namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 用水報表查詢結果 — 對應某水表迴路在某粒度下的 N 個 bucket。
/// 純資料結構，由 WaterUsageReportService 產出，View / Excel 匯出使用。
/// </summary>
public class WaterUsageReportResult
{
    /// <summary>查詢的迴路 Id</summary>
    public int nCircuitId { get; set; }

    /// <summary>迴路名稱（顯示用）</summary>
    public string szCircuitName { get; set; } = string.Empty;

    /// <summary>粒度：day / month / year</summary>
    public string szGranularity { get; set; } = string.Empty;

    /// <summary>查詢起點（含）</summary>
    public DateTime dtStart { get; set; }

    /// <summary>查詢終點（含），用於顯示</summary>
    public DateTime dtEnd { get; set; }

    /// <summary>各 bucket 的時間標籤與用水量（m³）</summary>
    public List<WaterUsageReportBucket> buckets { get; set; } = new();

    /// <summary>區間總用水量（m³，所有 bucket 加總）</summary>
    public double dTotalM3 { get; set; }

    /// <summary>是否任一葉子有缺資料警告</summary>
    public bool isHasWarning { get; set; }

    /// <summary>
    /// 直接子迴路的拆解（僅 Excel 匯出使用，預設為空）。
    /// 查詢 API 不會填這個欄位；只有 GetReportWithChildrenAsync 會展開。
    /// </summary>
    public List<WaterUsageReportChildSeries> children { get; set; } = new();
}

/// <summary>單一 bucket 的時間 + 用水量</summary>
public class WaterUsageReportBucket
{
    /// <summary>bucket 起始時間（含）</summary>
    public DateTime dtBucketStart { get; set; }

    /// <summary>顯示用標籤，例如 "2026-05-05"、"2026-05"、"2026"</summary>
    public string szLabel { get; set; } = string.Empty;

    /// <summary>該 bucket 用水量（m³）</summary>
    public double dM3 { get; set; }

    /// <summary>
    /// 該 bucket 任一葉子的邊界值抓不到（staleness window 內無 Quality=1 資料 / 缺資料）→ true。
    /// 前端據此在該柱/格 hover 提示「水表資料不完整、可能斷線」。
    /// </summary>
    public bool isStale { get; set; }
}

/// <summary>
/// 父迴路下的單一直接子節點 series — 給 Excel 匯出多欄展開用。
/// dM3PerBucket 與父 result.buckets 同 index 對齊，順序一致。
/// </summary>
public class WaterUsageReportChildSeries
{
    /// <summary>子迴路 Id</summary>
    public int nCircuitId { get; set; }

    /// <summary>子迴路名稱（顯示用）</summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>各 bucket 用水量（m³），與父 buckets 同 index 對齊</summary>
    public List<double> dM3PerBucket { get; set; } = new();

    /// <summary>該子迴路在區間內的合計（m³）</summary>
    public double dTotalM3 { get; set; }
}
