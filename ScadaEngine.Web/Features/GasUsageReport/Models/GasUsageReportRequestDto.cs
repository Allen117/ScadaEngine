namespace ScadaEngine.Web.Features.GasUsageReport.Models;

/// <summary>用氣報表查詢條件</summary>
public class GasUsageReportRequestDto
{
    public int circuitId { get; set; }

    /// <summary>hour / day / month / year</summary>
    public string granularity { get; set; } = "day";

    /// <summary>
    /// 期間起點：
    /// - hour: 起時（會被截到整點，分鐘秒歸零）
    /// - day:  起日（會被截到當日 00:00）
    /// - month: 起月（會被截到當月 1 日；月粒度走氣費期別）
    /// - year: 起年（會被截到當年 1/1）
    /// </summary>
    public DateTime start { get; set; }

    /// <summary>
    /// 期間終點：
    /// - hour: 訖時（會被截到整點，產出至訖時隔小時）
    /// - day:  訖日（會被截到當日 00:00，產出至訖日隔日 00:00）
    /// - month: 訖月
    /// - year: 訖年
    /// </summary>
    public DateTime end { get; set; }
}
