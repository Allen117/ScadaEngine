using ScadaEngine.Web.Features.DailyReport.Models;

namespace ScadaEngine.Tests.DailyReport;

/// <summary>SectionFlags bitmask 判定</summary>
public class DailyReportSectionFlagsTests
{
    [Fact]
    public void 全開預設值_八個區塊皆命中()
    {
        Assert.Equal(255, DailyReportSections.All);
        foreach (var nSection in new[]
        {
            DailyReportSections.Alarm, DailyReportSections.Electricity, DailyReportSections.Water,
            DailyReportSections.Gas, DailyReportSections.Rth, DailyReportSections.DayCompare,
            DailyReportSections.MtdCompare, DailyReportSections.Insights,
        })
        {
            Assert.True(DailyReportSections.Has(DailyReportSections.All, nSection));
        }
    }

    [Fact]
    public void 單獨關閉一區塊_其餘不受影響()
    {
        var nFlags = DailyReportSections.All & ~DailyReportSections.Rth;
        Assert.False(DailyReportSections.Has(nFlags, DailyReportSections.Rth));
        Assert.True(DailyReportSections.Has(nFlags, DailyReportSections.Alarm));
        Assert.True(DailyReportSections.Has(nFlags, DailyReportSections.Insights));
    }

    [Fact]
    public void 位元值互不重疊()
    {
        var aSections = new[]
        {
            DailyReportSections.Alarm, DailyReportSections.Electricity, DailyReportSections.Water,
            DailyReportSections.Gas, DailyReportSections.Rth, DailyReportSections.DayCompare,
            DailyReportSections.MtdCompare, DailyReportSections.Insights,
        };
        var nSum = aSections.Sum();
        Assert.Equal(DailyReportSections.All, nSum);  // 各位元恰好覆蓋 255
    }
}
