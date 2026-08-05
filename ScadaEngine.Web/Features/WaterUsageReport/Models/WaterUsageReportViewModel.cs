namespace ScadaEngine.Web.Features.WaterUsageReport.Models;

/// <summary>頁面初始狀態</summary>
public class WaterUsageReportViewModel
{
    public string szDefaultGranularity { get; set; } = "day";
    public DateTime dtToday { get; set; } = DateTime.Today;
}
