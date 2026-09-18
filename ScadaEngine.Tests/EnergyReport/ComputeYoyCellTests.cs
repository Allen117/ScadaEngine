using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.EnergyReport;

/// <summary>
/// 鎖住 EnergyReportService.ComputeYoyCell：用電報表「去年同期比較」單格的
/// 去年同期 / 差異 / 增減% 計算規則。
/// 這是月粒度 YOY 的核心計算，若被默默改壞（如去年 0 時算成 -100%、
/// 負底方向反了、四捨五入位數變了）數字會錯且不易第一時間察覺，故鎖規則。
/// </summary>
public class ComputeYoyCellTests
{
    [Fact]
    public void 去年缺該期_三欄皆null()
    {
        var (last, diff, pct) = EnergyReportService.ComputeYoyCell(123.4, null);
        Assert.Null(last);
        Assert.Null(diff);
        Assert.Null(pct);
    }

    [Fact]
    public void 正常增加_差異與增減皆正()
    {
        var (last, diff, pct) = EnergyReportService.ComputeYoyCell(120, 100);
        Assert.Equal(100, last);
        Assert.Equal(20, diff);
        Assert.Equal(20.0, pct);
    }

    [Fact]
    public void 減少_差異與增減皆負()
    {
        var (last, diff, pct) = EnergyReportService.ComputeYoyCell(80, 100);
        Assert.Equal(100, last);
        Assert.Equal(-20, diff);
        Assert.Equal(-20.0, pct);
    }

    [Fact]
    public void 去年為0_增減比回null但差異仍算()
    {
        var (last, diff, pct) = EnergyReportService.ComputeYoyCell(50, 0);
        Assert.Equal(0, last);
        Assert.Equal(50, diff);
        Assert.Null(pct);   // 除以 0 → 無法算增減比
    }

    [Fact]
    public void 去年為負_取絕對值為底且保留增減方向()
    {
        // 虛擬迴路（A−B）可能為負；由 -100 到 -30 屬「增加 70」→ pct 正
        var (last, diff, pct) = EnergyReportService.ComputeYoyCell(-30, -100);
        Assert.Equal(-100, last);
        Assert.Equal(70, diff);
        Assert.Equal(70.0, pct);
    }

    [Fact]
    public void 增減百分比四捨五入至小數一位()
    {
        // 3 / 97 * 100 = 3.0927… → 3.1
        var (_, _, pct) = EnergyReportService.ComputeYoyCell(100, 97);
        Assert.Equal(3.1, pct);
    }

    [Fact]
    public void 去年同期四捨五入至小數三位()
    {
        var (last, _, _) = EnergyReportService.ComputeYoyCell(0, 12.34567);
        Assert.Equal(12.346, last);
    }
}
