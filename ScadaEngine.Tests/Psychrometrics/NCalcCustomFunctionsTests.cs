using NCalc;
using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.Psychrometrics;

/// <summary>
/// 鎖住 NCalcCustomFunctions：公式字串經 Register 後可呼叫三個專業函數（不分大小寫），
/// 且保留字檢查涵蓋全部函數名。對錯 = CalcPoint 公式解析不到函數或變數名撞名破壞公式。
/// </summary>
public class NCalcCustomFunctionsTests
{
    [Theory]
    [InlineData("WetBulb(25, 50)", 18.0, 0.5)]
    [InlineData("Enthalpy(25, 50)", 50.3, 0.5)]
    [InlineData("DewPoint(25, 50)", 13.9, 0.5)]
    [InlineData("enthalpy(25, 50)", 50.3, 0.5)]   // 函數名不分大小寫
    [InlineData("DEWPOINT(25, 50)", 13.9, 0.5)]
    public void 公式字串可呼叫自訂函數(string formula, double expected, double tol)
    {
        var expr = new Expression(formula);
        NCalcCustomFunctions.Register(expr);
        Assert.Equal(expected, Convert.ToDouble(expr.Evaluate()), tol);
    }

    [Theory]
    [InlineData("WetBulb")]
    [InlineData("Enthalpy")]
    [InlineData("DewPoint")]
    [InlineData("enthalpy")]
    [InlineData("DEWPOINT")]
    public void 函數名為保留字(string name)
    {
        Assert.True(NCalcCustomFunctions.IsReservedName(name));
    }

    [Fact]
    public void 一般變數名非保留字()
    {
        Assert.False(NCalcCustomFunctions.IsReservedName("Flow"));
    }
}
