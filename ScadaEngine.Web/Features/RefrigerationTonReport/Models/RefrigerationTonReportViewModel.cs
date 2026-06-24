namespace ScadaEngine.Web.Features.RefrigerationTonReport.Models;

/// <summary>頁面初始狀態</summary>
public class RefrigerationTonReportViewModel
{
    public string szDefaultGranularity { get; set; } = "day";
    public DateTime dtToday { get; set; } = DateTime.Today;
}
