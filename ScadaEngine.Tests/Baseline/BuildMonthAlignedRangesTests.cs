using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Baseline;

/// <summary>
/// 鎖住 EnergyBaselineService.BuildMonthAlignedRanges：把 [start, endExclusive) 切成曆月對齊 chunk（首尾可為部分月）。
/// 切錯 = 逐月聚合抓錯資料範圍。純日期邏輯，期望值由「曆月邊界」定義直接推導。
/// </summary>
public class BuildMonthAlignedRangesTests
{
    [Fact]
    public void 跨三個月_首尾為部分月()
    {
        var ranges = EnergyBaselineService.BuildMonthAlignedRanges(
            new DateTime(2026, 1, 15), new DateTime(2026, 3, 10));

        Assert.Equal(3, ranges.Count);
        Assert.Equal((new DateTime(2026, 1, 15), new DateTime(2026, 2, 1)), ranges[0]); // 部分月：1/15→2/1
        Assert.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 3, 1)), ranges[1]);  // 完整 2 月
        Assert.Equal((new DateTime(2026, 3, 1), new DateTime(2026, 3, 10)), ranges[2]); // 部分月：3/1→3/10
    }

    [Fact]
    public void 完全落在單月內_只切一段()
    {
        var ranges = EnergyBaselineService.BuildMonthAlignedRanges(
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 20));

        Assert.Single(ranges);
        Assert.Equal((new DateTime(2026, 5, 5), new DateTime(2026, 5, 20)), ranges[0]);
    }

    [Fact]
    public void 起訖剛好貼齊月首_皆為完整月()
    {
        var ranges = EnergyBaselineService.BuildMonthAlignedRanges(
            new DateTime(2026, 6, 1), new DateTime(2026, 8, 1));

        Assert.Equal(2, ranges.Count);
        Assert.Equal((new DateTime(2026, 6, 1), new DateTime(2026, 7, 1)), ranges[0]);
        Assert.Equal((new DateTime(2026, 7, 1), new DateTime(2026, 8, 1)), ranges[1]);
    }

    [Fact]
    public void 跨年邊界()
    {
        var ranges = EnergyBaselineService.BuildMonthAlignedRanges(
            new DateTime(2025, 12, 20), new DateTime(2026, 1, 10));

        Assert.Equal(2, ranges.Count);
        Assert.Equal((new DateTime(2025, 12, 20), new DateTime(2026, 1, 1)), ranges[0]);
        Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2026, 1, 10)), ranges[1]);
    }

    [Fact]
    public void 起訖相同_回空清單()
    {
        var ranges = EnergyBaselineService.BuildMonthAlignedRanges(
            new DateTime(2026, 5, 10), new DateTime(2026, 5, 10));
        Assert.Empty(ranges);
    }
}
