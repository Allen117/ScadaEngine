namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 冷凍噸報表查詢結果 — 對應某水系統迴路在某粒度下的 N 個 bucket。
/// 純資料結構，由 RefrigerationTonReportService 產出，View / Excel 匯出使用。
/// 對標 <see cref="EnergyReportResult"/>，差異：
///   - 數值欄位 dKwh / dTotalKwh → dRtHour / dTotalRtHour（冷量 RT·h）
///   - 無 sign：水系統 WaterCircuit 表本身無 Sign 欄位，階層加總純正向
/// </summary>
public class RefrigerationTonReportResult
{
    public int nCircuitId { get; set; }

    public string szCircuitName { get; set; } = string.Empty;

    /// <summary>粒度：hour / day / month / year</summary>
    public string szGranularity { get; set; } = string.Empty;

    public DateTime dtStart { get; set; }

    public DateTime dtEnd { get; set; }

    public List<RefrigerationTonReportBucket> buckets { get; set; } = new();

    /// <summary>區間總冷量（所有 bucket RT·h 加總）</summary>
    public double dTotalRtHour { get; set; }

    /// <summary>是否任一葉子有缺資料警告（sample 數不足、無 WaterLeafHourly 列等）</summary>
    public bool isHasWarning { get; set; }

    /// <summary>
    /// 直接子迴路的拆解（僅 Excel 匯出使用，預設為空）。
    /// 查詢 API 不會填這個欄位；只有 GetReportWithChildrenAsync 會展開。
    /// </summary>
    public List<RefrigerationTonReportChildSeries> children { get; set; } = new();
}

/// <summary>單一 bucket 的時間 + RT·h（冷量）</summary>
public class RefrigerationTonReportBucket
{
    public DateTime dtBucketStart { get; set; }

    /// <summary>顯示用標籤，例如 "2026-05-05 13:00"、"2026-05"、"2026"</summary>
    public string szLabel { get; set; } = string.Empty;

    /// <summary>該 bucket 冷量（RT·h）</summary>
    public double dRtHour { get; set; }
}

/// <summary>
/// 父迴路下的單一直接子節點 series — 給 Excel 匯出多欄展開用。
/// dRtHourPerBucket 與父 result.buckets 同 index 對齊，順序一致。
/// </summary>
public class RefrigerationTonReportChildSeries
{
    public int nCircuitId { get; set; }

    public string szName { get; set; } = string.Empty;

    /// <summary>各 bucket RT·h，與父 buckets 同 index 對齊</summary>
    public List<double> dRtHourPerBucket { get; set; } = new();

    /// <summary>該子迴路在區間內的合計</summary>
    public double dTotalRtHour { get; set; }
}
