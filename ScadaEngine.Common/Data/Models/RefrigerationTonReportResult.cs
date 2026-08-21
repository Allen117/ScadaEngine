namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 冷凍噸報表查詢結果 — 對應某空調水系統迴路在某粒度下的 N 個 bucket。
/// 純資料結構，由 RefrigerationTonReportService 產出，View / Excel 匯出使用。
/// 對標 <see cref="EnergyReportResult"/>，差異：
///   - 數值欄位 dKwh / dTotalKwh → dRtHour / dTotalRtHour（冷量 RT·h）
///   - 無 sign：空調水系統 WaterCircuit 表本身無 Sign 欄位，階層加總純正向
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
    /// 資料覆蓋率明細 — <see cref="isHasWarning"/> 的「為什麼」。
    /// isHasWarning 只是 bool，使用者看到警告卻不知道是差 1% 還是差 90%；
    /// 此物件帶出實際數字與當下門檻，供 UI 給出可行動的提示。
    /// </summary>
    public RefrigerationTonCoverage coverage { get; set; } = new();

    /// <summary>
    /// 直接子迴路的拆解（僅 Excel 匯出使用，預設為空）。
    /// 查詢 API 不會填這個欄位；只有 GetReportWithChildrenAsync 會展開。
    /// </summary>
    public List<RefrigerationTonReportChildSeries> children { get; set; } = new();
}

/// <summary>
/// 迴路在查詢區間內的 WaterLeafHourly 資料覆蓋率 — 取「最差的那個葉子」為代表
/// （任一葉子缺資料，總量就不完整，報最好的那個會誤導）。
/// 覆蓋率 = 該葉子實際取得的 hourly 列數 ÷ 區間應有小時數（未來時段不計入分母）。
/// </summary>
public class RefrigerationTonCoverage
{
    /// <summary>區間應有小時數（每葉子相同；未來時段已夾除）。0 = 不適用（迴路下無葉子 / 區間為零長）</summary>
    public int nExpectedHours { get; set; }

    /// <summary>最差葉子實際取得的小時數</summary>
    public int nActualHours { get; set; }

    /// <summary>最差葉子的覆蓋率百分比（0~100，一位小數）。nExpectedHours = 0 時為 100</summary>
    public double dCoveragePercent { get; set; } = 100;

    /// <summary>最差葉子缺漏的小時數 = nExpectedHours - nActualHours（下限 0）</summary>
    public int nMissingHours { get; set; }

    /// <summary>最差葉子的點位名稱（顯示用，讓使用者知道該去查哪一顆表）</summary>
    public string szWorstLeafName { get; set; } = string.Empty;

    /// <summary>判定當下採用的門檻百分比（來自 appsettings，預設 90）— 提示要能講出「低於幾 %」</summary>
    public int nThresholdPercent { get; set; }

    /// <summary>覆蓋率是否低於門檻（= isHasWarning 中「資料不全」那一半的成因）</summary>
    public bool isBelowThreshold { get; set; }
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
