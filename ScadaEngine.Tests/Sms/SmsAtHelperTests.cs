using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.Sms;

/// <summary>
/// 鎖住 SmsAtHelper：UCS2 編碼 / 70 字截斷 / AT 回應解析。
/// 對錯 = 簡訊亂碼、內文被砍壞、或把 modem 錯誤當成功（漏發警報卻記成功）。
/// </summary>
public class SmsAtHelperTests
{
    // ── UCS2 編碼 ──

    [Fact]
    public void 英文編碼_每字元4hex()
    {
        Assert.Equal("00410042", SmsAtHelper.EncodeUcs2Hex("AB"));
    }

    [Fact]
    public void 中文編碼_UTF16BE()
    {
        // '警' = U+8B66, '報' = U+5831
        Assert.Equal("8B665831", SmsAtHelper.EncodeUcs2Hex("警報"));
    }

    [Fact]
    public void 混合與號碼編碼()
    {
        // '+' = U+002B, '8' = U+0038
        Assert.Equal("002B0038", SmsAtHelper.EncodeUcs2Hex("+8"));
    }

    [Fact]
    public void 空字串編碼_回空()
    {
        Assert.Equal(string.Empty, SmsAtHelper.EncodeUcs2Hex(""));
    }

    // ── 70 字截斷 ──

    [Fact]
    public void 長度70_不截斷()
    {
        var sz = new string('測', 70);
        Assert.Equal(sz, SmsAtHelper.TruncateForSms(sz));
    }

    [Fact]
    public void 長度71_截斷為70且以刪節號結尾()
    {
        var sz = new string('測', 71);
        var result = SmsAtHelper.TruncateForSms(sz);
        Assert.Equal(70, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void 截斷點落在surrogate中間_整組退掉不留半個字()
    {
        // 前 68 字 + 一個 surrogate pair（🚨 = 2 個 UTF-16 code unit，佔位 69-70）
        var sz = new string('a', 68) + "🚨" + "bcd";
        var result = SmsAtHelper.TruncateForSms(sz);
        // 切在 69 會斷開 high surrogate → 應退到 68 + '…'
        Assert.Equal(69, result.Length);
        Assert.EndsWith("…", result);
        Assert.DoesNotContain('\uD83D', result); // 不殘留孤立 high surrogate
    }

    // ── AT 回應解析 ──

    [Theory]
    [InlineData("\r\nOK\r\n", true)]
    [InlineData("+CMGS: 12\r\n\r\nOK\r\n", true)]
    [InlineData("\r\nERROR\r\n", true)]
    [InlineData("+CMS ERROR: 500\r\n", true)]
    [InlineData("+CSQ: 18,0", false)]   // 中間結果，還沒終結
    [InlineData("", false)]
    public void 終結碼判定(string buffer, bool expected)
    {
        Assert.Equal(expected, SmsAtHelper.IsFinalResponse(buffer));
    }

    [Theory]
    [InlineData("\r\nOK\r\n", true)]
    [InlineData("+CMGS: 12\r\n\r\nOK\r\n", true)]
    [InlineData("\r\nERROR\r\n", false)]
    [InlineData("+CMS ERROR: 500\r\n", false)]
    [InlineData("", false)]
    public void 成功判定_有OK且無ERROR(string buffer, bool expected)
    {
        Assert.Equal(expected, SmsAtHelper.IsSuccessResponse(buffer));
    }

    [Fact]
    public void 錯誤萃取_CMS錯誤碼()
    {
        Assert.Equal("+CMS ERROR: 500", SmsAtHelper.ExtractError("AT+CMGS\r\n+CMS ERROR: 500\r\n"));
    }

    [Fact]
    public void 錯誤萃取_無回應()
    {
        Assert.Equal("no response", SmsAtHelper.ExtractError(""));
    }

    // ── CSQ / CPIN 解析 ──

    [Theory]
    [InlineData("+CSQ: 18,0\r\nOK", 18)]
    [InlineData("+CSQ: 31,99\r\nOK", 31)]
    [InlineData("+CSQ: 99,99\r\nOK", -1)]  // 99 = 未知
    [InlineData("garbage", -1)]
    [InlineData("", -1)]
    public void CSQ解析(string buffer, int expected)
    {
        Assert.Equal(expected, SmsAtHelper.ParseCsq(buffer));
    }

    [Theory]
    [InlineData("+CPIN: READY\r\nOK", "READY")]
    [InlineData("+CPIN: SIM PIN\r\nOK", "SIM PIN")]
    [InlineData("+CME ERROR: 10", "NOT_INSERTED")]
    [InlineData("garbage", "UNKNOWN")]
    public void CPIN解析(string buffer, string expected)
    {
        Assert.Equal(expected, SmsAtHelper.ParseCpin(buffer));
    }
}
