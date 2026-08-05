namespace ScadaEngine.Web.Features.GasUsageReport.Models;

/// <summary>頁面初始狀態</summary>
public class GasUsageReportViewModel
{
    public string szDefaultGranularity { get; set; } = "day";
    public DateTime dtToday { get; set; } = DateTime.Today;
}
