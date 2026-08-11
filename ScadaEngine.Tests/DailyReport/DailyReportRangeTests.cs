using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.DailyReport;

/// <summary>日報比較日期推導 — D-7 跨月、MTD 跨年、2/29 邊界、每月 1 日單日 MTD</summary>
public class DailyReportRangeTests
{
    [Fact]
    public void 比較區間_五組順序與邊界正確()
    {
        // Arrange
        var dtReportDate = new DateTime(2026, 8, 5);

        // Act
        var ranges = DailyReportService.BuildComparisonRanges(dtReportDate);

        // Assert
        Assert.Equal(5, ranges.Count);
        Assert.Equal((new DateTime(2026, 8, 5), new DateTime(2026, 8, 6)), ranges[0]);   // D
        Assert.Equal((new DateTime(2026, 8, 4), new DateTime(2026, 8, 5)), ranges[1]);   // D-1
        Assert.Equal((new DateTime(2026, 7, 29), new DateTime(2026, 7, 30)), ranges[2]); // D-7
        Assert.Equal((new DateTime(2026, 8, 1), new DateTime(2026, 8, 6)), ranges[3]);   // MTD
        Assert.Equal((new DateTime(2025, 8, 1), new DateTime(2025, 8, 6)), ranges[4]);   // MTD 去年
    }

    [Fact]
    public void 上週同星期_跨月正確推導()
    {
        var ranges = DailyReportService.BuildComparisonRanges(new DateTime(2026, 8, 3));
        Assert.Equal(new DateTime(2026, 7, 27), ranges[2].dtStart);
        // 星期不變
        Assert.Equal(new DateTime(2026, 8, 3).DayOfWeek, ranges[2].dtStart.DayOfWeek);
    }

    [Fact]
    public void MTD_每月一日為單日區間()
    {
        var (dtStart, dtEnd) = DailyReportService.BuildMtdRange(new DateTime(2026, 8, 1));
        Assert.Equal(new DateTime(2026, 8, 1), dtStart);
        Assert.Equal(new DateTime(2026, 8, 2), dtEnd);
    }

    [Fact]
    public void MTD年同期_跨年正確()
    {
        var (dtStart, dtEnd) = DailyReportService.BuildMtdLastYearRange(new DateTime(2026, 1, 15));
        Assert.Equal(new DateTime(2025, 1, 1), dtStart);
        Assert.Equal(new DateTime(2025, 1, 16), dtEnd);  // 同 15 天
    }

    [Fact]
    public void MTD年同期_閏年229_去年只有28天時取min()
    {
        // 2028 閏年 2/29 → 2027 年 2 月只有 28 天 → 取整月
        var (dtStart, dtEnd) = DailyReportService.BuildMtdLastYearRange(new DateTime(2028, 2, 29));
        Assert.Equal(new DateTime(2027, 2, 1), dtStart);
        Assert.Equal(new DateTime(2027, 3, 1), dtEnd);
    }

    [Fact]
    public void MTD年同期_平年228_去年閏年時仍取28天()
    {
        // 2025/2/28（平年）→ 2024 閏年 2 月有 29 天，但只取 28 天對齊日數
        var (dtStart, dtEnd) = DailyReportService.BuildMtdLastYearRange(new DateTime(2025, 2, 28));
        Assert.Equal(new DateTime(2024, 2, 1), dtStart);
        Assert.Equal(new DateTime(2024, 2, 29), dtEnd);  // 28 天 → 訖 = 2/29 00:00（exclusive）
    }

    [Theory]
    [InlineData(115, 100, 15.0)]
    [InlineData(85, 100, -15.0)]
    [InlineData(100, 100, 0.0)]
    public void 差異百分比_一般情況(double dCurrent, double dBase, double dExpected)
    {
        Assert.Equal(dExpected, DailyReportService.CalcDiffPercent(dCurrent, dBase));
    }

    [Fact]
    public void 差異百分比_基準為零回null()
    {
        Assert.Null(DailyReportService.CalcDiffPercent(100, 0));
    }
}
