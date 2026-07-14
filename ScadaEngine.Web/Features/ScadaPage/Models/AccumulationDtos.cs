namespace ScadaEngine.Web.Features.ScadaPage.Models;

/// <summary>
/// ScadaPage 累積量查詢請求（批次）— 一頁上所有累積模式的 AI 點位元件一次查
/// </summary>
public class AccumulationRequestDto
{
    public List<AccumulationQueryItem> items { get; set; } = new();
}

/// <summary>
/// 單一累積量查詢項
/// </summary>
public class AccumulationQueryItem
{
    /// <summary>點位 SID</summary>
    public string szSid { get; set; } = string.Empty;

    /// <summary>累積期別：day（當日）| month（當月）</summary>
    public string szAccMode { get; set; } = "day";

    /// <summary>點位性質：meter（累積讀值差值）| integrate（瞬時值時間積分）</summary>
    public string szAccKind { get; set; } = "meter";

    /// <summary>meter 溢位上限（如電錶最大讀值），null = 未設定（倒退視為歸零）</summary>
    public double? dMaxValue { get; set; }
}

/// <summary>
/// 單一累積量計算結果
/// </summary>
public class AccumulationResultDto
{
    public string szSid { get; set; } = string.Empty;

    public string szAccMode { get; set; } = "day";

    /// <summary>累積量；null = 無法計算</summary>
    public double? dValue { get; set; }

    /// <summary>ok | no_data（期初無資料/期內無樣本）| stale（點位資料過舊，值為最後可算值或 null）</summary>
    public string szStatus { get; set; } = "ok";

    /// <summary>期初時間（今日 00:00 / 本月 1 號 00:00）</summary>
    public DateTime dtPeriodStart { get; set; }

    /// <summary>計算時間</summary>
    public DateTime dtCalcTime { get; set; }
}
