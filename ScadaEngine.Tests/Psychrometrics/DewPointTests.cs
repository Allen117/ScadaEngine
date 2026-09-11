using Psy = ScadaEngine.Common.Algorithms.Psychrometrics;

namespace ScadaEngine.Tests.Psychrometrics;

/// <summary>
/// 鎖住 Psychrometrics.DewPoint：Magnus 式反解露點 Td = 243.12γ/(17.62−γ)。
/// 對錯 = CalcPoint 露點公式與 Weather S5 外氣露點溫度算錯。
/// </summary>
public class DewPointTests
{
    [Theory]
    [InlineData(25, 50, 13.9, 0.5)]   // 標準錨點
    [InlineData(30, 70, 24.0, 0.5)]
    public void 錨點值_對照標準值(double t, double rh, double expected, double tol)
    {
        Assert.Equal(expected, Psy.DewPoint(t, rh), tol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    public void RH為100_露點等於乾球(double t)
    {
        Assert.Equal(t, Psy.DewPoint(t, 100), 1e-9);
    }

    [Fact]
    public void 露點恆不超過乾球()
    {
        for (var t = 0; t <= 50; t += 5)
            for (var rh = 1; rh <= 100; rh += 9)
                Assert.True(Psy.DewPoint(t, rh) <= t, $"T={t}, RH={rh} 露點超過乾球");
    }

    [Fact]
    public void 固定溫度下_露點隨RH遞增()
    {
        var dPrev = double.MinValue;
        for (var rh = 1; rh <= 100; rh += 3)
        {
            var dTd = Psy.DewPoint(30, rh);
            Assert.True(dTd > dPrev, $"RH={rh} 時露點 {dTd} 未大於前值 {dPrev}");
            dPrev = dTd;
        }
    }

    [Theory]
    [InlineData(-0.1, 50)]   // 溫度低於下限
    [InlineData(50.1, 50)]   // 溫度高於上限
    [InlineData(25, 0.9)]    // RH 低於下限
    [InlineData(25, 100.1)]  // RH 高於上限
    [InlineData(double.NaN, 50)]
    [InlineData(25, double.NaN)]
    public void 範圍外或NaN輸入_回傳NaN(double t, double rh)
    {
        Assert.True(double.IsNaN(Psy.DewPoint(t, rh)));
    }
}
