namespace ScadaEngine.Web.Features.RefrigerationTonReport.Models;

/// <summary>冷凍噸報表查詢條件 — 對標 EnergyReportRequestDto</summary>
public class RefrigerationTonReportRequestDto
{
    public int circuitId { get; set; }

    /// <summary>hour / day / month / year</summary>
    public string granularity { get; set; } = "day";

    public DateTime start { get; set; }

    public DateTime end { get; set; }
}
