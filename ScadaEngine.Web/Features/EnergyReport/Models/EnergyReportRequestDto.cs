namespace ScadaEngine.Web.Features.EnergyReport.Models;

/// <summary>用電報表查詢條件</summary>
public class EnergyReportRequestDto
{
    public int circuitId { get; set; }

    /// <summary>hour / day / month / year</summary>
    public string granularity { get; set; } = "day";

    /// <summary>
    /// 期間起點：
    /// - hour: 任意時間，會被截到當天 00:00
    /// - day:  起月（會被截到當月 1 日 00:00）
    /// - month: 起月（會被截到當月 1 日）
    /// - year: 起年（會被截到當年 1/1）
    /// </summary>
    public DateTime start { get; set; }

    /// <summary>
    /// 期間終點：
    /// - hour: 不使用（由 start 推 24h）
    /// - day:  訖月（會被截到當月 1 日，產出至訖月隔月 1 日）
    /// - month: 訖月
    /// - year: 訖年
    /// </summary>
    public DateTime end { get; set; }
}
