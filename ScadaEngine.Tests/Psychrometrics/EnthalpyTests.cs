using Psy = ScadaEngine.Common.Algorithms.Psychrometrics;

namespace ScadaEngine.Tests.Psychrometrics;

/// <summary>
/// 鎖住 Psychrometrics.Enthalpy：ASHRAE 濕空氣比焓 h = 1.006T + W(2501 + 1.86T)。
/// 對錯 = CalcPoint 焓值公式與 Weather S4 外氣焓值算錯。錨點值取 ASHRAE 濕空氣線圖標準值。
/// </summary>
public class EnthalpyTests
{
    [Theory]
    [InlineData(25, 50, 50.3, 0.5)]   // 線圖標準錨點
    [InlineData(30, 70, 78.0, 1.0)]
    [InlineData(0, 100, 9.47, 0.5)]
    public void 錨點值_對照ASHRAE線圖(double t, double rh, double expected, double tol)
    {
        Assert.Equal(expected, Psy.Enthalpy(t, rh), tol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    public void RH為0_退化為乾空氣顯熱(double t)
    {
        Assert.Equal(1.006 * t, Psy.Enthalpy(t, 0), 1e-9);
    }

    [Fact]
    public void 固定溫度下_焓值隨RH遞增()
    {
        var dPrev = double.MinValue;
        for (var rh = 0; rh <= 100; rh += 5)
        {
            var dH = Psy.Enthalpy(30, rh);
            Assert.True(dH > dPrev, $"RH={rh} 時焓值 {dH} 未大於前值 {dPrev}");
            dPrev = dH;
        }
    }

    [Theory]
    [InlineData(-0.1, 50)]   // 溫度低於下限
    [InlineData(50.1, 50)]   // 溫度高於上限
    [InlineData(25, -0.1)]   // RH 低於下限
    [InlineData(25, 100.1)]  // RH 高於上限
    [InlineData(double.NaN, 50)]
    [InlineData(25, double.NaN)]
    public void 範圍外或NaN輸入_回傳NaN(double t, double rh)
    {
        Assert.True(double.IsNaN(Psy.Enthalpy(t, rh)));
    }
}
