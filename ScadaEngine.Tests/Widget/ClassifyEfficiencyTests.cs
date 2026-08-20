using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Widget;

/// <summary>
/// 鎖住智慧助理「區間效率分析」的規則式分級 EnergyDeclarationService.ClassifyEfficiency：
/// good ≤ 0.85、0.85 &lt; normal ≤ 1.10、poor &gt; 1.10、null/≤0 → insufficient。
/// 門檻算錯 → 對話窗給錯評語（好壞相反）且不易察覺，屬「該補測試」的核心判斷。
/// </summary>
public class ClassifyEfficiencyTests
{
    [Theory]
    [InlineData(0.0)]      // 零 → 視為資料不足（用了冷量卻無電）
    [InlineData(-0.5)]     // 負值（防禦）
    public void 零或負值_判為insufficient(double dEff)
    {
        Assert.Equal("insufficient", EnergyDeclarationService.ClassifyEfficiency(dEff));
    }

    [Fact]
    public void null_判為insufficient()
    {
        Assert.Equal("insufficient", EnergyDeclarationService.ClassifyEfficiency(null));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.85)]     // 邊界：0.85 含在 good
    public void 小於等於0_85_判為good(double dEff)
    {
        Assert.Equal("good", EnergyDeclarationService.ClassifyEfficiency(dEff));
    }

    [Theory]
    [InlineData(0.851)]    // 剛越過 good 上界
    [InlineData(1.0)]
    [InlineData(1.10)]     // 邊界：1.10 含在 normal
    public void 介於0_85與1_10_判為normal(double dEff)
    {
        Assert.Equal("normal", EnergyDeclarationService.ClassifyEfficiency(dEff));
    }

    [Theory]
    [InlineData(1.101)]    // 剛越過 normal 上界
    [InlineData(1.5)]
    [InlineData(3.0)]
    public void 大於1_10_判為poor(double dEff)
    {
        Assert.Equal("poor", EnergyDeclarationService.ClassifyEfficiency(dEff));
    }
}
