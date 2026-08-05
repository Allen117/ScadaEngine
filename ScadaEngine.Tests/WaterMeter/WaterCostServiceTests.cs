using ScadaEngine.Web.Features.WaterTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.WaterMeter;

/// <summary>
/// 鎖住 WaterCostService.ApplyTiers 的台水分段累進演算法：
/// lower = max(0, nFrom-1)、upper = nTo ?? +∞、slice = min(total, upper) - lower、逐級累加。
/// 這條算錯 = 每一期水費金額都算錯（水費報表 + EMS 水費狀態卡共用），屬「該補測試」的核心邏輯。
/// 期望值以台水四級距手算：1–10 度 7.35、11–30 度 9.45、31–50 度 11.55、51 度以上 12.075（元/度）。
/// </summary>
public class WaterCostServiceTests
{
    /// <summary>台水 seed 四級距（與 Setting/water-tariff-taiwater-defaults.json 同值）</summary>
    private static List<WaterTariffTier> TaiwaterTiers() => new()
    {
        new WaterTariffTier { nFrom = 1, nTo = 10, dPrice = 7.35 },
        new WaterTariffTier { nFrom = 11, nTo = 30, dPrice = 9.45 },
        new WaterTariffTier { nFrom = 31, nTo = 50, dPrice = 11.55 },
        new WaterTariffTier { nFrom = 51, nTo = null, dPrice = 12.075 },
    };

    [Theory]
    [InlineData(0, 0)]                    // 無用水 = 0 元
    [InlineData(10, 73.5)]                // 第一級滿：10 × 7.35
    [InlineData(30, 262.5)]               // 73.5 + 20 × 9.45
    [InlineData(50, 493.5)]               // 262.5 + 20 × 11.55
    [InlineData(51, 505.575)]             // 493.5 + 1 × 12.075
    [InlineData(1000, 11964.75)]          // 493.5 + 950 × 12.075
    [InlineData(10.5, 78.225)]            // 小數度數跨級：73.5 + 0.5 × 9.45
    public void 台水四級距_分段累進金額正確(double dTotalM3, double dExpectedCost)
    {
        var (dCost, _) = WaterCostService.ApplyTiers(dTotalM3, TaiwaterTiers());
        Assert.Equal(dExpectedCost, dCost, 6);
    }

    [Theory]
    [InlineData(0, 0)]       // 總量 0 → index 0（同電費 ApplyTiers 慣例）
    [InlineData(5, 0)]
    [InlineData(10, 0)]      // 恰好級距上限 → 落在該級
    [InlineData(10.5, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(1000, 3)]    // 末級無上限
    public void 落點級距index正確(double dTotalM3, int nExpectedIdx)
    {
        var (_, nTopTierIdx) = WaterCostService.ApplyTiers(dTotalM3, TaiwaterTiers());
        Assert.Equal(nExpectedIdx, nTopTierIdx);
    }

    [Fact]
    public void 空級距_金額0不擲例外()
    {
        var (dCost, nTopTierIdx) = WaterCostService.ApplyTiers(100, new List<WaterTariffTier>());
        Assert.Equal(0, dCost, 10);
        Assert.Equal(0, nTopTierIdx);
    }

    [Fact]
    public void 級距明細_各級slice與金額正確_未達級距為0()
    {
        // 35 度：10@7.35 + 20@9.45 + 5@11.55 + 0@12.075
        var rows = WaterCostService.BuildTierRows(35, TaiwaterTiers());

        Assert.Equal(4, rows.Count);
        Assert.Equal(10, rows[0].sliceM3, 10);
        Assert.Equal(73.5, rows[0].sliceCost, 6);
        Assert.Equal(20, rows[1].sliceM3, 10);
        Assert.Equal(189.0, rows[1].sliceCost, 6);
        Assert.Equal(5, rows[2].sliceM3, 10);
        Assert.Equal(57.8, rows[2].sliceCost, 6);   // 5 × 11.55 = 57.75 → 四捨五入 1 位 = 57.8
        Assert.Equal(0, rows[3].sliceM3, 10);
        Assert.Equal(0, rows[3].sliceCost, 10);
    }
}
