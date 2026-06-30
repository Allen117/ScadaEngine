namespace ScadaEngine.Web.Features.Ems.Models;

public class EmsCircuitEnergyDto
{
    public List<string> labels { get; set; } = new();
    public List<double> values { get; set; } = new();
}
