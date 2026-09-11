using ScadaEngine.ModbusServer.Core;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.Tests.ModbusServer;

/// <summary>
/// float32 → register 編碼測試。
/// 記號：float IEEE754 big-endian 四位元組 = A B C D（A=最高位元組）。
/// 1.0f = 0x3F800000 → A=3F B=80 C=00 D=00；123.456f = 0x42F6E979。
/// </summary>
public class FloatRegisterEncoderTests
{
    [Fact]
    public void Encode_ABCD_高字組在前()
    {
        var (r0, r1) = FloatRegisterEncoder.Encode(1.0f, FloatWordOrder.ABCD);
        Assert.Equal(0x3F80, r0);
        Assert.Equal(0x0000, r1);

        (r0, r1) = FloatRegisterEncoder.Encode(123.456f, FloatWordOrder.ABCD);
        Assert.Equal(0x42F6, r0);
        Assert.Equal(0xE979, r1);
    }

    [Fact]
    public void Encode_CDAB_低字組在前_ModScanFloatingPt預設()
    {
        var (r0, r1) = FloatRegisterEncoder.Encode(1.0f, FloatWordOrder.CDAB);
        Assert.Equal(0x0000, r0);
        Assert.Equal(0x3F80, r1);

        (r0, r1) = FloatRegisterEncoder.Encode(123.456f, FloatWordOrder.CDAB);
        Assert.Equal(0xE979, r0);
        Assert.Equal(0x42F6, r1);
    }

    [Fact]
    public void QuietNaN_位元樣式為0x7FC00000()
    {
        Assert.True(float.IsNaN(FloatRegisterEncoder.QuietNaN));
        Assert.Equal(0x7FC00000, BitConverter.SingleToInt32Bits(FloatRegisterEncoder.QuietNaN));

        var (r0, r1) = FloatRegisterEncoder.Encode(FloatRegisterEncoder.QuietNaN, FloatWordOrder.ABCD);
        Assert.Equal(0x7FC0, r0);
        Assert.Equal(0x0000, r1);
    }

    [Fact]
    public void WriteToBuffer_寫入線路位元組序()
    {
        Span<byte> buffer = stackalloc byte[16];

        // 位址 2（= byte offset 4）寫入 123.456f
        FloatRegisterEncoder.WriteToBuffer(buffer, 2, 123.456f, FloatWordOrder.ABCD);
        Assert.Equal(new byte[] { 0x42, 0xF6, 0xE9, 0x79 }, buffer.Slice(4, 4).ToArray());

        FloatRegisterEncoder.WriteToBuffer(buffer, 2, 123.456f, FloatWordOrder.CDAB);
        Assert.Equal(new byte[] { 0xE9, 0x79, 0x42, 0xF6 }, buffer.Slice(4, 4).ToArray());
    }

    [Fact]
    public void ParseWordOrder_預設CDAB_不分大小寫()
    {
        Assert.Equal(FloatWordOrder.CDAB, FloatRegisterEncoder.ParseWordOrder(null));
        Assert.Equal(FloatWordOrder.CDAB, FloatRegisterEncoder.ParseWordOrder("CDAB"));
        Assert.Equal(FloatWordOrder.CDAB, FloatRegisterEncoder.ParseWordOrder("亂填"));
        Assert.Equal(FloatWordOrder.ABCD, FloatRegisterEncoder.ParseWordOrder("abcd"));
    }
}

/// <summary>SID 自然排序測試</summary>
public class NaturalSidComparerTests
{
    [Theory]
    [InlineData("196865-S2", "196865-S10")]
    [InlineData("DB1-S9", "DB1-S10")]
    [InlineData("DB1-S10", "DB2-S1")]
    [InlineData("CALC-S1", "CALC-S2")]
    [InlineData("OPC1-S1", "OPC10-S1")]
    public void 自然排序_前者小於後者(string szSmaller, string szBigger)
    {
        Assert.True(NaturalSidComparer.Instance.Compare(szSmaller, szBigger) < 0);
        Assert.True(NaturalSidComparer.Instance.Compare(szBigger, szSmaller) > 0);
    }

    [Fact]
    public void 相同字串_相等()
    {
        Assert.Equal(0, NaturalSidComparer.Instance.Compare("196865-S1", "196865-S1"));
    }
}

/// <summary>對外供值規則測試（NaN / HoldLast / stale / DB bypass / 停用點）</summary>
public class ServedValueRulesTests
{
    private static readonly DateTime _dtNow = new(2026, 9, 11, 12, 0, 0);

    [Fact]
    public void 從未有值_回NaN()
    {
        Assert.True(float.IsNaN(ServedValueRules.Compute(null, isActive: true, isHoldLast: false, 300, _dtNow)));
        Assert.True(float.IsNaN(ServedValueRules.Compute(
            new RealtimeValueModel { hasData = false }, isActive: true, isHoldLast: true, 300, _dtNow)));
    }

    [Fact]
    public void 品質良好且新鮮_回實際值()
    {
        var item = new RealtimeValueModel { dValue = 42.5, szQuality = "GOOD", dtTimestamp = _dtNow.AddSeconds(-10), hasData = true };
        Assert.Equal(42.5f, ServedValueRules.Compute(item, true, false, 300, _dtNow));

        // MQTT payload 實際是混用大小寫 "Good"
        item.szQuality = "Good";
        Assert.Equal(42.5f, ServedValueRules.Compute(item, true, false, 300, _dtNow));
    }

    [Fact]
    public void 品質不良_預設NaN_HoldLast保持最後值()
    {
        var item = new RealtimeValueModel { dValue = 42.5, szQuality = "BAD", dtTimestamp = _dtNow, hasData = true };
        Assert.True(float.IsNaN(ServedValueRules.Compute(item, true, isHoldLast: false, 300, _dtNow)));
        Assert.Equal(42.5f, ServedValueRules.Compute(item, true, isHoldLast: true, 300, _dtNow));
    }

    [Fact]
    public void 逾時未更新_視為斷線NaN_DB來源bypass不受影響()
    {
        var stale = new RealtimeValueModel { dValue = 1, szQuality = "GOOD", dtTimestamp = _dtNow.AddSeconds(-301), hasData = true };
        Assert.True(float.IsNaN(ServedValueRules.Compute(stale, true, false, 300, _dtNow)));
        Assert.Equal("STALE", ServedValueRules.GetStatusLabel(stale, true, 300, _dtNow));

        var dbItem = new RealtimeValueModel { dValue = 1, szQuality = "GOOD", dtTimestamp = DateTime.MinValue, hasData = true, isFreshBypass = true };
        Assert.Equal(1f, ServedValueRules.Compute(dbItem, true, false, 300, _dtNow));
    }

    [Fact]
    public void 已刪除停用點_預設NaN_狀態標DISABLED()
    {
        var item = new RealtimeValueModel { dValue = 7, szQuality = "GOOD", dtTimestamp = _dtNow, hasData = true };
        Assert.True(float.IsNaN(ServedValueRules.Compute(item, isActive: false, isHoldLast: false, 300, _dtNow)));
        Assert.Equal("DISABLED", ServedValueRules.GetStatusLabel(item, isActive: false, 300, _dtNow));
    }
}
