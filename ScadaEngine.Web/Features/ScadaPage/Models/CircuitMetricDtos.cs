namespace ScadaEngine.Web.Features.ScadaPage.Models;

/// <summary>
/// ScadaPage 迴路指標查詢請求（批次）— 一頁上所有迴路指標 cell/widget 一次查
/// </summary>
public class CircuitMetricRequestDto
{
    public List<CircuitMetricQueryItem> items { get; set; } = new();
}

/// <summary>
/// 單一迴路指標查詢項
/// </summary>
public class CircuitMetricQueryItem
{
    /// <summary>EnergyCircuit.Id（虛擬迴路亦可）</summary>
    public int nCircuitId { get; set; }

    /// <summary>指標：day_kwh（本日度數，曆日）| month_kwh（本月度數，曆月）| period_kwh（本月電度，期別）| period_cost（本月電費，期別）</summary>
    public string szMetric { get; set; } = "day_kwh";
}

/// <summary>
/// 單一迴路指標計算結果
/// </summary>
public class CircuitMetricResultDto
{
    public int nCircuitId { get; set; }

    public string szMetric { get; set; } = "day_kwh";

    /// <summary>指標值；null = 無法計算</summary>
    public double? dValue { get; set; }

    /// <summary>ok | no_data（迴路不存在/無葉子/邊界值缺）| stale（部分 bucket 斷線）| no_plan（電費指標無有效方案）</summary>
    public string szStatus { get; set; } = "ok";

    /// <summary>單位：kWh 指標固定 kWh；電費指標為空字串（貨幣單位由前端 i18n 決定）</summary>
    public string szUnit { get; set; } = string.Empty;

    /// <summary>period_cost 專用：金額是否為子迴路占比分攤估算（同 EMS 電費卡 isEstimated）</summary>
    public bool isEstimated { get; set; }

    /// <summary>計算時間</summary>
    public DateTime dtCalcTime { get; set; }
}
