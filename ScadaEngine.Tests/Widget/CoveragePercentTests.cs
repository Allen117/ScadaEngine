using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Widget;

/// <summary>
/// 鎖住冷凍噸報表的資料覆蓋率算式 RefrigerationTonReportService.CalcCoveragePercent
/// —— 智慧助理「區間效率分析」據此判定是否低於門檻（appsettings
/// RefrigerationTonAggregation:MinCoveragePercent，預設 90）並改吐 low_coverage 評語。
/// 分母為 0 時若誤回 0 會把「當期尚未到來」誤報成資料全缺，是最容易改壞的一條。
/// </summary>
public class CoveragePercentTests
{
    [Fact]
    public void 分母為0_視為不適用回100()
    {
        // 區間全落在未來 / 零長度 → 不該誤判成「全缺」
        Assert.Equal(100, RefrigerationTonReportService.CalcCoveragePercent(0, 0));
    }

    [Fact]
    public void 完全無資料_回0()
    {
        Assert.Equal(0, RefrigerationTonReportService.CalcCoveragePercent(0, 168));
    }

    [Fact]
    public void 資料齊全_回100()
    {
        Assert.Equal(100, RefrigerationTonReportService.CalcCoveragePercent(168, 168));
    }

    [Fact]
    public void 超過分母_夾到100()
    {
        // 期別重疊時同一 hour 可能計入多個 bucket，覆蓋率不該出現 >100%
        Assert.Equal(100, RefrigerationTonReportService.CalcCoveragePercent(200, 168));
    }

    [Theory]
    [InlineData(84, 168, 50.0)]
    [InlineData(151, 168, 89.880952380952380)]   // 略低於 90% 門檻 → 應觸發 low_coverage
    [InlineData(152, 168, 90.476190476190482)]   // 略高於 90% 門檻 → 不觸發
    public void 一般比例_按實際計算(int nActual, int nExpected, double dExpectedPercent)
    {
        Assert.Equal(dExpectedPercent, RefrigerationTonReportService.CalcCoveragePercent(nActual, nExpected), 6);
    }

    [Theory]
    [InlineData(151, 168, false)]   // 89.88% < 90 → 低於門檻
    [InlineData(152, 168, true)]    // 90.48% ≥ 90 → 未低於門檻
    public void 門檻90_邊界判定(int nActual, int nExpected, bool isExpectedPass)
    {
        const int nThreshold = 90;
        var dPercent = RefrigerationTonReportService.CalcCoveragePercent(nActual, nExpected);
        Assert.Equal(isExpectedPass, dPercent >= nThreshold);
    }

    [Fact]
    public void 負數實際值_防禦性回0()
    {
        Assert.Equal(0, RefrigerationTonReportService.CalcCoveragePercent(-5, 168));
    }
}
