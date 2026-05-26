using System.Text.Json;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端通知訊息 i18n — 讀取 Resources/notification.{language}.json 字典，
/// 提供以 {placeholder} 為佔位符的字串模板套用。
/// Line / Email 共用同一份字典，依群組 Language 欄位（'zh-TW' / 'en'）選擇對應字典。
///
/// 設計重點：
///   1. 啟動時一次載入所有支援語系字典；找不到檔案不致命，缺 key 時 fallback 至 zh-TW，再 fallback 至 key 本身
///   2. 模板採 `{name}` 風格，避免與 string.Format 的 `{0}` 衝突（既有 alarm.message 模板已用 `{0}`）
///   3. Thread-safe：載入後字典為唯讀，可被 Singleton service 多執行緒共用
/// </summary>
public class NotificationLocalizer
{
    private const string c_szDefaultLanguage = "zh-TW";
    private static readonly string[] c_aSupportedLanguages = new[] { "zh-TW", "en" };

    private readonly ILogger<NotificationLocalizer> _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _dicts = new();

    public NotificationLocalizer(ILogger<NotificationLocalizer> logger)
    {
        _logger = logger;
        LoadDictionaries();
    }

    /// <summary>
    /// 取得指定語系、key 對應的字串模板（含 {placeholder} 佔位符未替換）。
    /// 找不到時 fallback：指定語系 → zh-TW → key 本身。
    /// </summary>
    public string Get(string szLanguage, string szKey)
    {
        if (string.IsNullOrEmpty(szLanguage)) szLanguage = c_szDefaultLanguage;

        if (_dicts.TryGetValue(szLanguage, out var dict) && dict.TryGetValue(szKey, out var val))
            return val;
        if (szLanguage != c_szDefaultLanguage
            && _dicts.TryGetValue(c_szDefaultLanguage, out var fallback)
            && fallback.TryGetValue(szKey, out var fallbackVal))
            return fallbackVal;
        return szKey;
    }

    /// <summary>
    /// 取得指定語系、key 對應的字串並套用 args 字典。args 中的 key 對應 {key} 佔位符。
    /// </summary>
    public string Format(string szLanguage, string szKey, IDictionary<string, string?>? args = null)
    {
        var szTemplate = Get(szLanguage, szKey);
        if (args == null || args.Count == 0) return szTemplate;

        var sb = new System.Text.StringBuilder(szTemplate);
        foreach (var kv in args)
        {
            sb.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty);
        }
        return sb.ToString();
    }

    /// <summary>嚴重度標籤（緊急 / 高 / 中 / 低）</summary>
    public string SeverityLabel(string szLanguage, byte n) => Get(szLanguage, $"notify.severity.{n}");

    /// <summary>嚴重度圖示</summary>
    public string SeverityIcon(string szLanguage, byte n) => Get(szLanguage, $"notify.icon.{n}");

    /// <summary>嚴重度色碼（HTML 用）</summary>
    public string SeverityColor(string szLanguage, byte n) => Get(szLanguage, $"notify.color.{n}");

    private void LoadDictionaries()
    {
        var szBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
        if (!Directory.Exists(szBaseDir))
        {
            // 開發模式 fallback
            szBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
        }

        foreach (var szLang in c_aSupportedLanguages)
        {
            var szPath = Path.Combine(szBaseDir, $"notification.{szLang}.json");
            if (!File.Exists(szPath))
            {
                _logger.LogWarning("找不到通知字典: {Path}", szPath);
                _dicts[szLang] = new Dictionary<string, string>();
                continue;
            }
            try
            {
                var szJson = File.ReadAllText(szPath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(szJson)
                             ?? new Dictionary<string, string>();
                _dicts[szLang] = parsed;
                _logger.LogInformation("已載入通知字典 {Language}: {Count} 個 key", szLang, parsed.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析通知字典失敗: {Path}", szPath);
                _dicts[szLang] = new Dictionary<string, string>();
            }
        }
    }
}
