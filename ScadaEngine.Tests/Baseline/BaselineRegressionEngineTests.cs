using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Baseline;

/// <summary>
/// 鎖住 BaselineRegressionEngine.Predict 的線性預測：y = 截距 + Σ(係數ᵢ × Xᵢ)。
/// EnPI 報告期用凍結係數預測基線用電，算錯 = 節能率算錯。純數學，期望值由公式直接推導。
/// </summary>
public class BaselineRegressionEngineTests
{
    [Fact]
    public void 多變數線性組合()
    {
        // y = 10 + 2*5 + 3*4 = 32
        var d = BaselineRegressionEngine.Predict(
            dIntercept: 10,
            dCoefficients: new double[] { 2, 3 },
            dXValues: new double[] { 5, 4 });
        Assert.Equal(32, d, precision: 10);
    }

    [Fact]
    public void 無係數_只回截距()
    {
        var d = BaselineRegressionEngine.Predict(5.5, Array.Empty<double>(), Array.Empty<double>());
        Assert.Equal(5.5, d, precision: 10);
    }

    [Fact]
    public void 負係數與負輸入()
    {
        // y = 0 + (-1.5)*(-2) = 3
        var d = BaselineRegressionEngine.Predict(0, new double[] { -1.5 }, new double[] { -2 });
        Assert.Equal(3, d, precision: 10);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]   // y = 0 + 1*0 = 0
    [InlineData(100, 0, 999, 100)] // 係數 0 → X 不影響，只剩截距
    public void 單變數各情境(double intercept, double coef, double x, double expected)
    {
        var d = BaselineRegressionEngine.Predict(intercept, new[] { coef }, new[] { x });
        Assert.Equal(expected, d, precision: 10);
    }
}
