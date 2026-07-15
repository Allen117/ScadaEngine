namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// ISO 50001 能源基線模型主檔（EnergyBaseline 表）。
/// 一列 = 一個基線回歸模型 Y = Intercept + Σ Coefficient·Xi，
/// 變數列見 <see cref="EnergyBaselineVariableModel"/>。
/// Status：draft 可重算重存 / frozen 係數凍結（EnPI 報告一律用凍結係數預測）。
/// </summary>
public class EnergyBaselineModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;

    /// <summary>Y 來源型別：circuit / point</summary>
    public string szTargetType { get; set; } = "circuit";

    /// <summary>TargetType=point 時的點位 SID</summary>
    public string? szTargetSID { get; set; }

    /// <summary>TargetType=circuit 時的 EnergyCircuit.Id</summary>
    public int? nTargetCircuitId { get; set; }

    /// <summary>Y 取樣方式：cumulative（期初期末相減）/ average（期間均值）。circuit 一律 cumulative</summary>
    public string szTargetMode { get; set; } = "cumulative";

    /// <summary>Y 顯示名稱快照</summary>
    public string szTargetLabel { get; set; } = string.Empty;

    /// <summary>Y 單位快照</summary>
    public string? szTargetUnit { get; set; }

    /// <summary>取樣粒度：day（曆日）/ month（曆月）</summary>
    public string szGranularity { get; set; } = "day";

    /// <summary>基線期起（含）</summary>
    public DateTime dtBaselineStart { get; set; }

    /// <summary>基線期訖（含當日/當月）</summary>
    public DateTime dtBaselineEnd { get; set; }

    /// <summary>draft / frozen</summary>
    public string szStatus { get; set; } = "draft";

    /// <summary>回歸截距 β0（未跑回歸前 null）</summary>
    public double? dIntercept { get; set; }

    public double? dR2 { get; set; }
    public double? dAdjR2 { get; set; }

    /// <summary>CV(RMSE) = RMSE / mean(Y)</summary>
    public double? dCvRmse { get; set; }

    /// <summary>回歸實際使用樣本數（已剔除缺值 bucket）</summary>
    public int? nSampleCount { get; set; }

    public DateTime? dtFrozenAt { get; set; }
    public DateTime dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
    public string? szDescription { get; set; }

    /// <summary>變數子檔（查詢時由 Service 組裝，依 Sequence 排序）</summary>
    public List<EnergyBaselineVariableModel> variables { get; set; } = new();
}

/// <summary>
/// 能源基線相關變數 X 子檔（EnergyBaselineVariable 表）。一模型最多 5 列。
/// </summary>
public class EnergyBaselineVariableModel
{
    public int nId { get; set; }
    public int nBaselineId { get; set; }

    /// <summary>變數順序 1..5（對應 β1..β5）</summary>
    public int nSequence { get; set; }

    /// <summary>X 來源型別：point（HistoryData 期間均值）/ circuit（bucket kWh）</summary>
    public string szVarType { get; set; } = "point";

    public string? szSourceSID { get; set; }
    public int? nSourceCircuitId { get; set; }

    /// <summary>X 顯示名稱快照</summary>
    public string szLabel { get; set; } = string.Empty;

    public string? szUnit { get; set; }

    /// <summary>回歸係數 βi（未跑回歸前 null）</summary>
    public double? dCoefficient { get; set; }

    /// <summary>係數顯著性 p-value</summary>
    public double? dPValue { get; set; }
}

/// <summary>
/// 一次 OLS 複回歸的計算結果（純記憶體 DTO，不落 DB —
/// Service 將其中係數/統計量回填 EnergyBaseline / EnergyBaselineVariable）。
/// </summary>
public class BaselineRegressionResult
{
    /// <summary>截距 β0</summary>
    public double dIntercept { get; set; }

    /// <summary>各 X 係數 β1..βn（順序同輸入變數）</summary>
    public double[] dCoefficients { get; set; } = Array.Empty<double>();

    /// <summary>各係數 p-value（順序同 dCoefficients；樣本自由度不足時為 NaN）</summary>
    public double[] dPValues { get; set; } = Array.Empty<double>();

    public double dR2 { get; set; }
    public double dAdjR2 { get; set; }

    /// <summary>CV(RMSE) = RMSE / mean(Y)</summary>
    public double dCvRmse { get; set; }

    /// <summary>實際參與回歸的樣本數</summary>
    public int nSampleCount { get; set; }
}
