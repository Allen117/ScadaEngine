namespace ScadaEngine.Web.Features.EnergyDeclaration.Models;

/// <summary>頁面初始狀態</summary>
public class EnergyDeclarationViewModel
{
    public string szDefaultGranularity { get; set; } = "day";
    public DateTime dtToday { get; set; } = DateTime.Today;
}
