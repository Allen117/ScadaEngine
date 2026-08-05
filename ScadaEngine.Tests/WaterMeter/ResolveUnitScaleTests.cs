using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.WaterMeter;

/// <summary>
/// 鎖住 WaterMeterCircuitService.ResolveUnitScale：點位單位字串 → m³ 換算係數。
/// 這是「綁定時定案」的單位判定（寫進 WaterMeterCircuit.UnitScale，之後不追溯），
/// 對錯 = 該水表所有歷史用水量差 1000 倍，或該讓使用者選的點位選不到 / 不該選的選得到。
/// </summary>
public class ResolveUnitScaleTests
{
    [Theory]
    [InlineData("m³")]
    [InlineData("m3")]
    [InlineData("M3")]          // 不分大小寫
    [InlineData("CMD")]
    [InlineData("cms")]
    [InlineData("立方公尺")]
    [InlineData("立方米")]
    [InlineData("米立方")]
    [InlineData(" m3 ")]        // 前後空白先 trim
    public void m3系單位_係數為1(string szUnit)
    {
        Assert.Equal(1.0, WaterMeterCircuitService.ResolveUnitScale(szUnit));
    }

    [Theory]
    [InlineData("L")]
    [InlineData("l")]
    [InlineData("Liter")]
    [InlineData("litre")]
    [InlineData("公升")]
    [InlineData("升")]
    public void 公升系單位_係數為千分之一(string szUnit)
    {
        Assert.Equal(0.001, WaterMeterCircuitService.ResolveUnitScale(szUnit));
    }

    [Theory]
    [InlineData("度")]          // 刻意排除 — 與電度混淆
    [InlineData("kWh")]
    [InlineData("kW")]
    [InlineData("RT")]
    [InlineData("°C")]
    [InlineData("m³/h")]        // 流量（瞬時）非累積量
    [InlineData("CMH")]         // 流量（m³/hr）非累積量
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void 非水量累積單位_回null(string? szUnit)
    {
        Assert.Null(WaterMeterCircuitService.ResolveUnitScale(szUnit));
    }
}
