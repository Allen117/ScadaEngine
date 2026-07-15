namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// EnPI / 節能量報告結果 — 報告期逐 bucket「實際 vs 基線預測」比較。
/// 預測一律用凍結基線係數（EnergyBaseline.Status = frozen），不重算。
/// </summary>
public class EnPIReportResult
{
    public int nBaselineId { get; set; }
    public string szBaselineName { get; set; } = string.Empty;
    public string szTargetLabel { get; set; } = string.Empty;
    public string? szTargetUnit { get; set; }

    /// <summary>day / month（承基線模型粒度）</summary>
    public string szGranularity { get; set; } = "day";

    public DateTime dtStart { get; set; }
    public DateTime dtEnd { get; set; }

    /// <summary>各 X 變數顯示名稱（順序同 bucket.xValues；匯出欄名用）</summary>
    public List<string> variableLabels { get; set; } = new();

    public List<EnPIReportBucket> buckets { get; set; } = new();

    /// <summary>Σ 實際（僅計入資料完整的 bucket）</summary>
    public double dTotalActual { get; set; }

    /// <summary>Σ 基線預測（僅計入資料完整的 bucket）</summary>
    public double dTotalPredicted { get; set; }

    /// <summary>累計節能量 = Σ(預測 − 實際)，正值 = 優於基線</summary>
    public double dTotalSavings { get; set; }

    /// <summary>整體 EnPI = Σ實際 ÷ Σ預測（&lt;1 = 優於基線；預測為 0 時 null）</summary>
    public double? dOverallEnpi { get; set; }

    /// <summary>因缺資料（Y 或任一 X 缺值）被排除的 bucket 數</summary>
    public int nMissingCount { get; set; }
}

/// <summary>EnPI 報告單一 bucket 列</summary>
public class EnPIReportBucket
{
    public DateTime dtBucketStart { get; set; }
    public string szLabel { get; set; } = string.Empty;

    /// <summary>實際能耗（缺資料時 null）</summary>
    public double? dActual { get; set; }

    /// <summary>基線預測能耗（缺任一 X 時 null）</summary>
    public double? dPredicted { get; set; }

    /// <summary>節能量 = 預測 − 實際（正值 = 優於基線）</summary>
    public double? dSavings { get; set; }

    /// <summary>累計節能量（缺值 bucket 不累計，沿用前值）</summary>
    public double? dCumulativeSavings { get; set; }

    /// <summary>EnPI = 實際 ÷ 預測（預測 ≤ 0 時 null）</summary>
    public double? dEnpi { get; set; }

    /// <summary>該 bucket 因缺 Y 或任一 X 而未參與統計</summary>
    public bool isMissing { get; set; }

    /// <summary>各 X 變數在該 bucket 的取樣值（順序同基線變數 Sequence；缺值為 null）— 匯出/除錯用</summary>
    public List<double?> xValues { get; set; } = new();
}
