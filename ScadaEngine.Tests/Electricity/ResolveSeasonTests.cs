using ScadaEngine.Web.Features.TariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住 ElectricityCostService.ResolveSeason 的夏月判定規則：
/// MM-dd 含頭尾；當「起 &gt; 訖」時視為跨年區間。
/// 夏月/非夏月套錯 → 整段電價套錯，屬核心計費邏輯。
/// </summary>
public class ResolveSeasonTests
{
    // 台電典型夏月：6/1 ~ 9/30
    private static TariffPlan SummerPlan() => new()
    {
        szSummerStart = "06-01",
        szSummerEnd = "09-30",
    };

    // 跨年區間範例：11/01 ~ 02/28（起 > 訖）
    private static TariffPlan CrossYearPlan() => new()
    {
        szSummerStart = "11-01",
        szSummerEnd = "02-28",
    };

    [Theory]
    [InlineData("2026-06-01", true)]  // 邊界：起日含
    [InlineData("2026-09-30", true)]  // 邊界：訖日含
    [InlineData("2026-07-15", true)]  // 區間中
    [InlineData("2026-05-31", false)] // 起日前一天
    [InlineData("2026-10-01", false)] // 訖日後一天
    [InlineData("2026-01-15", false)] // 冬季
    public void 一般區間_夏月判定(string szDate, bool expected)
    {
        var result = ElectricityCostService.ResolveSeason(SummerPlan(), DateTime.Parse(szDate));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2026-12-25", true)]  // 跨年區間內（年底側）
    [InlineData("2026-01-10", true)]  // 跨年區間內（年初側）
    [InlineData("2026-11-01", true)]  // 邊界：起日含
    [InlineData("2026-02-28", true)]  // 邊界：訖日含
    [InlineData("2026-06-15", false)] // 區間外
    public void 跨年區間_夏月判定(string szDate, bool expected)
    {
        var result = ElectricityCostService.ResolveSeason(CrossYearPlan(), DateTime.Parse(szDate));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void 方案未設夏月字串_一律非夏月()
    {
        var plan = new TariffPlan { szSummerStart = "", szSummerEnd = "" };
        Assert.False(ElectricityCostService.ResolveSeason(plan, new DateTime(2026, 7, 1)));
    }
}
