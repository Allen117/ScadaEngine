namespace ScadaEngine.Engine.Models;

public class TodayDemandModel
{
    public double? dCurrentKW { get; set; }
    public DateTime? dtTimestamp { get; set; }
    public byte nQuality { get; set; }
    public double? dMaxKW { get; set; }
    public DateTime? dtMaxAt { get; set; }
}

public class DemandTrendPoint
{
    public DateTime dtTimestamp { get; set; }
    public double dDemandKW { get; set; }
    public byte nQuality { get; set; }
}
