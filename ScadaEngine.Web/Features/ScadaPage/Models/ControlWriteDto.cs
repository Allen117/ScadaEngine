namespace ScadaEngine.Web.Features.ScadaPage.Models;

public class ControlWriteDto
{
    public string cid   { get; set; } = string.Empty;
    public double value { get; set; } = 1;
    public string mode  { get; set; } = string.Empty;

    // 別名，方便 log
    public string szCid  => cid;
    public double nValue => value;
}
