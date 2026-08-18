using System.Text;
using System.Text.RegularExpressions;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// AT 指令協定純函式工具 — 無 IO 依賴，可單元測試。
/// 編碼採 UCS2（AT+CSCS="UCS2"）：中英文通用，單封上限 70 個 UTF-16 字元。
/// </summary>
public static class SmsAtHelper
{
    /// <summary>UCS2 模式下單封簡訊的字元上限（UTF-16 code unit）</summary>
    public const int MAX_UCS2_CHARS = 70;

    /// <summary>
    /// 將字串編為 UCS2 hex（UTF-16BE，每字元 4 個 hex 字元）。
    /// AT+CSCS="UCS2" 模式下，AT+CMGS 的電話號碼與內文都要用此編碼。
    /// </summary>
    public static string EncodeUcs2Hex(string szText)
    {
        if (string.IsNullOrEmpty(szText)) return string.Empty;
        var sb = new StringBuilder(szText.Length * 4);
        foreach (var ch in szText)
            sb.Append(((int)ch).ToString("X4"));
        return sb.ToString();
    }

    /// <summary>
    /// 截斷至單封簡訊上限。超長時以 '…' 結尾；不在 surrogate pair 中間切斷。
    /// </summary>
    public static string TruncateForSms(string szText, int nMaxChars = MAX_UCS2_CHARS)
    {
        if (string.IsNullOrEmpty(szText) || szText.Length <= nMaxChars) return szText ?? string.Empty;

        int nCut = nMaxChars - 1; // 留一格給 '…'
        if (nCut > 0 && char.IsHighSurrogate(szText[nCut - 1]))
            nCut--;
        return szText.Substring(0, nCut) + "…";
    }

    /// <summary>回應是否已包含終結碼（OK / ERROR / +CMS ERROR / +CME ERROR）</summary>
    public static bool IsFinalResponse(string szBuffer)
    {
        if (string.IsNullOrEmpty(szBuffer)) return false;
        return szBuffer.Contains("OK\r") || szBuffer.TrimEnd().EndsWith("OK")
            || szBuffer.Contains("ERROR"); // 涵蓋 ERROR / +CMS ERROR: n / +CME ERROR: n
    }

    /// <summary>回應是否成功（有 OK 且無 ERROR）</summary>
    public static bool IsSuccessResponse(string szBuffer)
    {
        if (string.IsNullOrEmpty(szBuffer)) return false;
        return (szBuffer.Contains("OK\r") || szBuffer.TrimEnd().EndsWith("OK"))
            && !szBuffer.Contains("ERROR");
    }

    /// <summary>
    /// 從失敗回應萃取錯誤描述，例如 "+CMS ERROR: 500"；無法辨識時回傳 "ERROR"
    /// </summary>
    public static string ExtractError(string szBuffer)
    {
        if (string.IsNullOrEmpty(szBuffer)) return "no response";
        var m = Regex.Match(szBuffer, @"\+(CMS|CME) ERROR:\s*\S[^\r\n]*");
        if (m.Success) return m.Value.Trim();
        return szBuffer.Contains("ERROR") ? "ERROR" : "no final response";
    }

    /// <summary>
    /// 解析 AT+CSQ 回應（"+CSQ: 18,0"）→ rssi 0~31；99 或解析失敗回傳 -1
    /// </summary>
    public static int ParseCsq(string szBuffer)
    {
        var m = Regex.Match(szBuffer ?? string.Empty, @"\+CSQ:\s*(\d+)\s*,");
        if (!m.Success) return -1;
        int nRssi = int.Parse(m.Groups[1].Value);
        return nRssi >= 0 && nRssi <= 31 ? nRssi : -1;
    }

    /// <summary>
    /// 解析 AT+CPIN? 回應 → "READY" / "SIM PIN" / "SIM PUK" / "NOT_INSERTED" / "UNKNOWN"
    /// </summary>
    public static string ParseCpin(string szBuffer)
    {
        if (string.IsNullOrEmpty(szBuffer)) return "UNKNOWN";
        var m = Regex.Match(szBuffer, @"\+CPIN:\s*([^\r\n]+)");
        if (m.Success) return m.Groups[1].Value.Trim();
        // 部分 modem SIM 未插時直接回 +CME ERROR: 10 (SIM not inserted)
        if (szBuffer.Contains("ERROR: 10") || szBuffer.Contains("not inserted", StringComparison.OrdinalIgnoreCase))
            return "NOT_INSERTED";
        return "UNKNOWN";
    }
}
