using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Tests.Modbus;

/// <summary>
/// Modbus 資料型態解碼測試 — 聚焦本次新增的 DEC10K3 / BCD / BIT0–BIT15。
///
/// DEC10K3 = base-10000 十進位分段（非 BCD）：R0 整數高位段 ×10000、R1 整數低位段 ×1、R2 小數四位 ×0.0001。
/// BCD     = 單一 word 的 4 個 nibble 各存一位十進位數字（0x1234 → 1234）。
/// BITn    = 取單一 word 的第 n 個位元，回 0 或 1。
/// </summary>
public class ModbusDataTypeTests
{
    /// <summary>建立已完成 ParseAddress / ParseRatioAndRegisterCount 的點位（預設 Holding 40001）</summary>
    private static ModbusTagModel MakeTag(string szDataType, string szRatio = "1", string szAddress = "40001")
    {
        var tag = new ModbusTagModel
        {
            szName = "TEST",
            szAddress = szAddress,
            szDataType = szDataType,
            szRatio = szRatio
        };
        Assert.True(tag.Validate());
        return tag;
    }

    // ── 暫存器數量 ──────────────────────────────────────────────

    [Theory]
    [InlineData("DEC10K3", 3)]
    [InlineData("BCD", 1)]
    [InlineData("BIT7", 1)]
    [InlineData("BIT0", 1)]
    [InlineData("BIT15", 1)]
    public void 暫存器數量_新型別(string szDataType, int nExpected)
    {
        Assert.Equal(nExpected, MakeTag(szDataType).nRegisterCount);
    }

    // ── DEC10K3 ────────────────────────────────────────────────

    [Fact]
    public void DEC10K3_整數與四位小數合併()
    {
        var tag = MakeTag("DEC10K3");

        // 1×10000 + 2345 = 12345，小數 6789 × 0.0001 = 0.6789
        Assert.Equal(12345.6789f, tag.CalculatePhysicalValue(new ushort[] { 1, 2345, 6789 }), 3);
    }

    [Fact]
    public void DEC10K3_只有最小小數位()
    {
        var tag = MakeTag("DEC10K3");
        Assert.Equal(0.0001f, tag.CalculatePhysicalValue(new ushort[] { 0, 0, 1 }), 6);
    }

    [Fact]
    public void DEC10K3_全零()
    {
        var tag = MakeTag("DEC10K3");
        Assert.Equal(0.0f, tag.CalculatePhysicalValue(new ushort[] { 0, 0, 0 }));
    }

    [Fact]
    public void DEC10K3_乘上Ratio()
    {
        var tag = MakeTag("DEC10K3", szRatio: "0.1");
        Assert.Equal(10.0f, tag.CalculatePhysicalValue(new ushort[] { 0, 100, 0 }), 4);
    }

    [Fact]
    public void DEC10K3_暫存器不足時回零()
    {
        var tag = MakeTag("DEC10K3");
        Assert.Equal(0.0f, tag.CalculatePhysicalValue(new ushort[] { 1, 2345 }));
    }

    [Fact]
    public void DEC10K3_整數超過single精確上限時登記一次警告()
    {
        var tag = MakeTag("DEC10K3");

        // 900 × 10000 = 9,000,000 > 2^23 (8,388,608)
        tag.CalculatePhysicalValue(new ushort[] { 900, 0, 5000 });
        var szFirst = tag.TakeDecodeWarning();
        Assert.NotNull(szFirst);
        Assert.Contains("DEC10K3", szFirst);

        // 同一點位同一種警告只回報一次，避免採集迴圈刷 log
        tag.CalculatePhysicalValue(new ushort[] { 900, 0, 5000 });
        Assert.Null(tag.TakeDecodeWarning());
    }

    [Fact]
    public void DEC10K3_整數未超界時不發警告()
    {
        var tag = MakeTag("DEC10K3");
        tag.CalculatePhysicalValue(new ushort[] { 1, 2345, 6789 });
        Assert.Null(tag.TakeDecodeWarning());
    }

    // ── BCD ────────────────────────────────────────────────────

    [Theory]
    [InlineData((ushort)0x1234, 1234f)]
    [InlineData((ushort)0x0009, 9f)]
    [InlineData((ushort)0x0000, 0f)]
    [InlineData((ushort)0x9999, 9999f)]
    public void BCD_四個nibble各為一位十進位數字(ushort nRaw, float fExpected)
    {
        var tag = MakeTag("BCD");
        Assert.Equal(fExpected, tag.CalculatePhysicalValue(new[] { nRaw }));
    }

    [Fact]
    public void BCD_非法nibble回零並登記警告()
    {
        var tag = MakeTag("BCD");

        Assert.Equal(0.0f, tag.CalculatePhysicalValue(new ushort[] { 0x12A4 }));

        var szWarning = tag.TakeDecodeWarning();
        Assert.NotNull(szWarning);
        Assert.Contains("BCD", szWarning);
    }

    [Fact]
    public void BCD_乘上Ratio()
    {
        var tag = MakeTag("BCD", szRatio: "0.1");
        Assert.Equal(123.4f, tag.CalculatePhysicalValue(new ushort[] { 0x1234 }), 4);
    }

    // ── BIT0–BIT15 ─────────────────────────────────────────────

    [Theory]
    [InlineData("BIT0", 1f)]
    [InlineData("BIT1", 0f)]
    [InlineData("BIT2", 1f)]
    [InlineData("BIT15", 1f)]
    [InlineData("BIT14", 0f)]
    public void BIT_取指定位元(string szDataType, float fExpected)
    {
        // 0xA5A5 = 1010 0101 1010 0101
        var tag = MakeTag(szDataType);
        Assert.Equal(fExpected, tag.CalculatePhysicalValue(new ushort[] { 0xA5A5 }));
    }

    [Theory]
    [InlineData("BIT0", 0, true)]
    [InlineData("BIT15", 15, true)]
    [InlineData("BIT16", -1, false)]
    [InlineData("BIT", -1, false)]
    [InlineData("BIT+5", -1, false)]
    [InlineData("INTEGER", -1, false)]
    [InlineData(null, -1, false)]
    public void TryParseBitIndex_只接受BIT0到BIT15(string? szDataType, int nExpectedIndex, bool isExpected)
    {
        Assert.Equal(isExpected, ModbusTagModel.TryParseBitIndex(szDataType, out var nIndex));
        Assert.Equal(nExpectedIndex, nIndex);
    }

    [Theory]
    [InlineData("00001")]  // Coil
    [InlineData("10001")]  // Discrete Input
    public void BIT_配Coil或Discrete位址時驗證失敗(string szAddress)
    {
        var tag = new ModbusTagModel
        {
            szName = "TEST",
            szAddress = szAddress,
            szDataType = "BIT5",
            szRatio = "1"
        };

        Assert.False(tag.Validate());
        Assert.NotNull(tag.szValidationError);
    }

    [Theory]
    [InlineData("40001")]  // Holding
    [InlineData("30001")]  // Input
    public void BIT_配Holding或Input位址時驗證通過(string szAddress)
    {
        var tag = new ModbusTagModel
        {
            szName = "TEST",
            szAddress = szAddress,
            szDataType = "BIT5",
            szRatio = "1"
        };

        Assert.True(tag.Validate());
        Assert.Null(tag.szValidationError);
    }

    // ── 白名單 ──────────────────────────────────────────────────

    [Fact]
    public void 白名單含新型別且BIT為0到15共16項()
    {
        var types = ModbusTagModel.SupportedDataTypes;

        Assert.Contains("DEC10K3", types);
        Assert.Contains("BCD", types);
        Assert.Equal(16, types.Count(t => t.StartsWith("BIT")));
        Assert.Contains("BIT0", types);
        Assert.Contains("BIT15", types);
        Assert.DoesNotContain("BIT16", types);

        // 舊有型別不得因改為程式產生而遺失
        Assert.Contains("INTEGER", types);
        Assert.Contains("UINT32BE", types);
        Assert.Contains("SWAPPEDDOUBLE", types);
    }

    [Fact]
    public void 白名單不含xlsm舊下拉的非法字串Bit()
    {
        Assert.DoesNotContain("BIT", ModbusTagModel.SupportedDataTypes);
    }
}
