namespace ScadaEngine.Web.Features.EnergyBaseline.Models;

/// <summary>跑回歸後回給前端的完整結果（統計量 + 散布圖資料 + 警告旗標）</summary>
public class RegressionRunResponse
{
    public double intercept { get; set; }
    public double r2 { get; set; }
    public double adjR2 { get; set; }

    /// <summary>CV(RMSE)；NaN 時序列化前轉 null</summary>
    public double? cvRmse { get; set; }

    public int sampleCount { get; set; }

    /// <summary>因 Y 或任一 X 缺值被剔除的 bucket 數</summary>
    public int droppedCount { get; set; }

    /// <summary>基線期內尚未過完（bucket 訖 &gt; 現在）而未取樣的 bucket 數</summary>
    public int incompleteCount { get; set; }

    public List<RegressionVariableResult> variables { get; set; } = new();
    public List<RegressionScatterPoint> scatter { get; set; } = new();

    /// <summary>樣本數 &lt; 變數數 × 5（模型可能不可靠，不阻擋）</summary>
    public bool isSampleLow { get; set; }

    /// <summary>R² &lt; 0.5（解釋力弱，不阻擋）</summary>
    public bool isR2Low { get; set; }
}

/// <summary>單一變數的回歸結果列</summary>
public class RegressionVariableResult
{
    public string label { get; set; } = string.Empty;
    public string? unit { get; set; }
    public double coefficient { get; set; }

    /// <summary>p-value；共線性導致無法估計時 null（前端顯示「—」）</summary>
    public double? pValue { get; set; }
}

/// <summary>實際 vs 預測散布圖單點</summary>
public class RegressionScatterPoint
{
    public string label { get; set; } = string.Empty;
    public double actual { get; set; }
    public double predicted { get; set; }
}

/// <summary>SEU 重大能源使用鑑別結果</summary>
public class SeuAnalysisResult
{
    /// <summary>是否有可分析的迴路來源（主要電表子迴路或根迴路）</summary>
    public bool hasSource { get; set; }

    /// <summary>分析母體名稱（主要電表名，或「全部根迴路」）</summary>
    public string sourceName { get; set; } = string.Empty;

    /// <summary>正值項目 kWh 加總（占比分母）</summary>
    public double totalKwh { get; set; }

    /// <summary>帕累托門檻（%）</summary>
    public double threshold { get; set; }

    public List<SeuItem> items { get; set; } = new();
}

/// <summary>SEU 排名單項（依 kWh 降冪）</summary>
public class SeuItem
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public double kwh { get; set; }

    /// <summary>占比 %（kWh ≤ 0 的項目為 0，不參與分母）</summary>
    public double pct { get; set; }

    /// <summary>累計占比 %</summary>
    public double cumPct { get; set; }

    /// <summary>是否鑑別為重大能源使用（累計占比達門檻前的項目，含跨越門檻那一項）</summary>
    public bool isSeu { get; set; }
}
