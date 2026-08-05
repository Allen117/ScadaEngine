using ScadaEngine.Web.Features.GasTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.GasMeter;

/// <summary>
/// 鎖住 GasCostService.ApplyTiers 的分段累進演算法：
/// lower = max(0, nFrom-1)、upper = nTo ?? +∞、slice = min(total, upper) - lower、逐級累加。
/// 這條算錯 = 每一期氣費金額都算錯（氣費報表 + EMS 氣費狀態卡共用），屬「該補測試」的核心邏輯。
/// 天然氣無全國統一費率，測試改用一組虛構但形狀完整的四級距（邊界值語意才是要鎖的東西）。
/// </summary>
public class GasCostServiceTests
{
    /// <summary>虛構四級距：1–20 度 10、21–50 度 12、51–100 度 15、101 度以上 18（元/度）</summary>
    private static List<GasTariffTier> SampleTiers() => new()
    {
        new GasTariffTier { nFrom = 1, nTo = 20, dPrice = 10 },
        new GasTariffTier { nFrom = 21, nTo = 50, dPrice = 12 },
        new GasTariffTier { nFrom = 51, nTo = 100, dPrice = 15 },
        new GasTariffTier { nFrom = 101, nTo = null, dPrice = 18 },
    };

    [Theory]
    [InlineData(0, 0)]              // 無用氣 = 0 元
    [InlineData(20, 200)]           // 第一級滿：20 × 10
    [InlineData(50, 560)]           // 200 + 30 × 12
    [InlineData(100, 1310)]         // 560 + 50 × 15
    [InlineData(101, 1328)]         // 1310 + 1 × 18
    [InlineData(1000, 17510)]       // 1310 + 900 × 18
    [InlineData(20.5, 206)]         // 小數度數跨級：200 + 0.5 × 12
    public void 四級距_分段累進金額正確(double dTotalM3, double dExpectedCost)
    {
        var (dCost, _) = GasCostService.ApplyTiers(dTotalM3, SampleTiers());
        Assert.Equal(dExpectedCost, dCost, 6);
    }

    [Theory]
    [InlineData(0, 0)]       // 總量 0 → index 0（同水費/電費 ApplyTiers 慣例）
    [InlineData(5, 0)]
    [InlineData(20, 0)]      // 恰好級距上限 → 落在該級
    [InlineData(20.5, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 3)]
    [InlineData(1000, 3)]    // 末級無上限
    public void 落點級距index正確(double dTotalM3, int nExpectedIdx)
    {
        var (_, nTopTierIdx) = GasCostService.ApplyTiers(dTotalM3, SampleTiers());
        Assert.Equal(nExpectedIdx, nTopTierIdx);
    }

    [Fact]
    public void 空級距_金額0不擲例外()
    {
        var (dCost, nTopTierIdx) = GasCostService.ApplyTiers(100, new List<GasTariffTier>());
        Assert.Equal(0, dCost, 10);
        Assert.Equal(0, nTopTierIdx);
    }

    /// <summary>seed 預設為「單一級距、單價 0」的空白範本 — 任何用量都應得 0 元且不例外</summary>
    [Fact]
    public void 空白seed範本_單價0_任何用量皆0元()
    {
        var blank = new List<GasTariffTier> { new() { nFrom = 1, nTo = null, dPrice = 0 } };
        var (dCost, nTopTierIdx) = GasCostService.ApplyTiers(12345.6, blank);
        Assert.Equal(0, dCost, 10);
        Assert.Equal(0, nTopTierIdx);
    }

    [Fact]
    public void 級距明細_各級slice與金額正確_未達級距為0()
    {
        // 60 度：20@10 + 30@12 + 10@15 + 0@18
        var rows = GasCostService.BuildTierRows(60, SampleTiers());

        Assert.Equal(4, rows.Count);
        Assert.Equal(20, rows[0].sliceM3, 10);
        Assert.Equal(200, rows[0].sliceCost, 6);
        Assert.Equal(30, rows[1].sliceM3, 10);
        Assert.Equal(360, rows[1].sliceCost, 6);
        Assert.Equal(10, rows[2].sliceM3, 10);
        Assert.Equal(150, rows[2].sliceCost, 6);
        Assert.Equal(0, rows[3].sliceM3, 10);
        Assert.Equal(0, rows[3].sliceCost, 10);
    }

    /// <summary>
    /// 級距與期別長度解耦（決策 7）：兩月一期時使用者直接填「一期」的級距，
    /// 服務層不做任何倍率換算 — 同一組級距對同一總量恆得同一金額，與期別多長無關。
    /// </summary>
    [Fact]
    public void 級距與期別解耦_不做任何天數倍率換算()
    {
        var (dOnePeriod, _) = GasCostService.ApplyTiers(60, SampleTiers());
        // 若曾誤加「兩月一期 ×2」之類的換算，下面這條會與上面不同
        var (dSameAgain, _) = GasCostService.ApplyTiers(60, SampleTiers());
        Assert.Equal(dOnePeriod, dSameAgain, 10);
        Assert.Equal(710, dOnePeriod, 6);   // 200 + 360 + 150
    }
}
