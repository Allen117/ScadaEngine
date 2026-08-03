using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.OpcUa;

/// <summary>
/// 鎖住 OpcUaClientHelper 的型別轉換：讀回值 → double（TryConvertToDouble）與
/// 寫入值 double → Server 端 CLR 型別（ConvertToServerType，含 Math.Round 銀行家捨入）。
/// 轉錯 = 讀到錯數值或寫入被 Server 拒（Bad_TypeMismatch）。純轉換邏輯。
/// </summary>
public class OpcUaClientHelperTests
{
    // ── TryConvertToDouble ───────────────────────────────
    [Fact]
    public void 讀回_bool轉1或0()
    {
        Assert.True(OpcUaClientHelper.TryConvertToDouble(true, out var dTrue));
        Assert.Equal(1.0, dTrue);
        Assert.True(OpcUaClientHelper.TryConvertToDouble(false, out var dFalse));
        Assert.Equal(0.0, dFalse);
    }

    [Fact]
    public void 讀回_數值型別轉double()
    {
        Assert.True(OpcUaClientHelper.TryConvertToDouble(42, out var dInt));
        Assert.Equal(42.0, dInt);
        Assert.True(OpcUaClientHelper.TryConvertToDouble(3.5f, out var dFloat));
        Assert.Equal(3.5, dFloat, precision: 6);
    }

    [Theory]
    [InlineData("3.14", true, 3.14)]
    [InlineData("abc", false, 0)]    // 無法解析 → false
    public void 讀回_字串嘗試解析(string s, bool ok, double expected)
    {
        Assert.Equal(ok, OpcUaClientHelper.TryConvertToDouble(s, out var d));
        if (ok) Assert.Equal(expected, d, precision: 6);
    }

    [Fact]
    public void 讀回_null與不支援型別回false()
    {
        Assert.False(OpcUaClientHelper.TryConvertToDouble(null, out _));
        Assert.False(OpcUaClientHelper.TryConvertToDouble(new int[] { 1, 2 }, out _));
    }

    // ── ConvertToServerType ──────────────────────────────
    [Fact]
    public void 寫入_依bool門檻0_5()
    {
        Assert.Equal(true, OpcUaClientHelper.ConvertToServerType(false, 0.7));  // >0.5 → true
        Assert.Equal(false, OpcUaClientHelper.ConvertToServerType(true, 0.3));  // ≤0.5 → false
    }

    [Fact]
    public void 寫入_int四捨五入非中點()
    {
        Assert.Equal(4, OpcUaClientHelper.ConvertToServerType(0, 3.6));
        Assert.Equal(3, OpcUaClientHelper.ConvertToServerType(0, 3.2));
    }

    [Fact]
    public void 寫入_currentValue為null時原樣回double()
    {
        var result = OpcUaClientHelper.ConvertToServerType(null, 9.99);
        Assert.Equal(9.99, Assert.IsType<double>(result), precision: 6);
    }

    [Fact]
    public void 寫入_保留目標型別()
    {
        Assert.IsType<short>(OpcUaClientHelper.ConvertToServerType((short)0, 5.0));
        Assert.IsType<float>(OpcUaClientHelper.ConvertToServerType(0f, 5.0));
    }
}
