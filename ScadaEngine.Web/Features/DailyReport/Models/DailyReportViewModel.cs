namespace ScadaEngine.Web.Features.DailyReport.Models;

/// <summary>日報瀏覽頁 ViewModel</summary>
public class DailyReportViewModel
{
    /// <summary>預設檢視日 = 昨日（yyyy-MM-dd）</summary>
    public string szDefaultDate { get; set; } = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
    /// <summary>可選最大日 = 昨日（今日資料未完整不提供）</summary>
    public string szMaxDate { get; set; } = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
}
