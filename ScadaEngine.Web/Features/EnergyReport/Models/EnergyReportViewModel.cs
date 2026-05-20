namespace ScadaEngine.Web.Features.EnergyReport.Models;

/// <summary>頁面初始狀態</summary>
public class EnergyReportViewModel
{
    public string szDefaultGranularity { get; set; } = "day";
    public DateTime dtToday { get; set; } = DateTime.Today;
}
