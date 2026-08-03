using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Widget;

/// <summary>
/// 鎖住 WidgetAccumulationCache 的 bucket key 格式（日 / 時桶）。
/// key 格式若被改動會導致累積量對不上舊桶、統計歸零。純字串格式化。
/// </summary>
public class WidgetAccumulationCacheTests
{
    [Fact]
    public void 日桶key格式()
    {
        var szKey = WidgetAccumulationCache.DayBucketKey("SID_A", new DateTime(2026, 8, 3, 14, 30, 0));
        Assert.Equal("SID_A|D|2026-08-03", szKey);
    }

    [Fact]
    public void 時桶key含小時()
    {
        var szKey = WidgetAccumulationCache.HourBucketKey("SID_A", new DateTime(2026, 8, 3, 14, 30, 0));
        Assert.Equal("SID_A|H|2026-08-03-14", szKey);
    }

    [Fact]
    public void 不同小時_桶key不同()
    {
        var szH14 = WidgetAccumulationCache.HourBucketKey("X", new DateTime(2026, 8, 3, 14, 59, 0));
        var szH15 = WidgetAccumulationCache.HourBucketKey("X", new DateTime(2026, 8, 3, 15, 0, 0));
        Assert.NotEqual(szH14, szH15);
    }
}
