using Microsoft.Extensions.Localization;
using System.Text.Json;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 把 (messageKey, messageArgs JSON) 在當前使用者 culture 翻譯成顯示字串。
/// 流程：
///   1. messageKey 為 null → 直接回 fallbackMessage（舊資料相容）
///   2. 從 SharedResource 撈 messageKey 對應模板（zh-TW: "{0} 超過上限 {1}"，en: "{0} exceeds upper limit {1}"）
///   3. 解析 messageArgs JSON，依固定 key 順序（依 alarm 類型）填入 {0} {1}
///
/// 三種警報類型固定 args 順序（與 Engine AlarmMonitorService 一致）：
///   alarm.high_exceed: 0=name, 1=threshold
///   alarm.low_below : 0=name, 1=threshold
///   alarm.di_triggered: 0=name, 1=state
/// </summary>
public class AlarmMessageLocalizer
{
    private readonly IStringLocalizer<Resources.SharedResource> _l;

    public AlarmMessageLocalizer(IStringLocalizer<Resources.SharedResource> localizer)
    {
        _l = localizer;
    }

    /// <summary>
    /// 翻譯警報訊息。messageKey 為 null 時直接回 fallbackMessage（舊資料無痛上線）。
    /// </summary>
    public string Localize(string? szMessageKey, string? szMessageArgsJson, string szFallbackMessage)
    {
        if (string.IsNullOrEmpty(szMessageKey))
            return szFallbackMessage ?? string.Empty;

        var ls = _l[szMessageKey];
        if (ls.ResourceNotFound)
            return szFallbackMessage ?? szMessageKey;

        var aArgs = ParseArgs(szMessageKey, szMessageArgsJson);
        try
        {
            return string.Format(ls.Value, aArgs);
        }
        catch
        {
            // 模板與參數數量不符時 fallback
            return szFallbackMessage ?? ls.Value;
        }
    }

    /// <summary>
    /// 依 messageKey 將 args JSON 轉成位置陣列（{0}, {1} 順序）。
    /// </summary>
    private static object[] ParseArgs(string szMessageKey, string? szArgsJson)
    {
        if (string.IsNullOrEmpty(szArgsJson)) return Array.Empty<object>();

        Dictionary<string, string>? args;
        try
        {
            using var doc = JsonDocument.Parse(szArgsJson);
            args = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String
                    ? (p.Value.GetString() ?? string.Empty)
                    : p.Value.ToString());
        }
        catch
        {
            return Array.Empty<object>();
        }

        // 依 messageKey 決定 args 順序：alarm.* 內建；control.* 委派 ControlEventLogger
        string[] aOrder = szMessageKey switch
        {
            "alarm.high_exceed"  => new[] { "name", "threshold" },
            "alarm.low_below"    => new[] { "name", "threshold" },
            "alarm.di_triggered" => new[] { "name", "state" },
            _ when szMessageKey.StartsWith("control.action.") => ControlEventLogger.ArgOrderForKey(szMessageKey),
            _ => Array.Empty<string>()
        };

        return aOrder
            .Select(k => (object)(args.TryGetValue(k, out var v) ? v : string.Empty))
            .ToArray();
    }
}
