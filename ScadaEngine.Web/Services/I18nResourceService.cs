using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 把已編譯的 .resx 資源（含衛星組件）打包成 JSON dictionary 給前端 i18n.js 用。
/// 使用反射列舉主組件中所有 *.resources，避免硬編碼資源檔列表。
/// 結果以 culture 為 key 快取，避免每次請求重新反射。
/// </summary>
public class I18nResourceService
{
    private readonly ILogger<I18nResourceService> _logger;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new();
    private readonly Lazy<List<string>> _resourceBaseNames;

    public I18nResourceService(ILogger<I18nResourceService> logger)
    {
        _logger = logger;
        _resourceBaseNames = new Lazy<List<string>>(DiscoverResourceBaseNames);
    }

    /// <summary>
    /// 列舉所有可翻譯資源 base name（去掉 .resources 副檔名）。
    /// </summary>
    private List<string> DiscoverResourceBaseNames()
    {
        var asm = typeof(Resources.SharedResource).Assembly;
        var aResNames = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".resources", StringComparison.Ordinal))
            .Where(n => !n.EndsWith(".g.resources", StringComparison.Ordinal))   // 排除 Razor 編譯產物
            .Select(n => n[..^".resources".Length])
            .ToList();

        _logger.LogInformation("I18n discovered {Count} resource base names: {Names}",
            aResNames.Count, string.Join(", ", aResNames));
        return aResNames;
    }

    /// <summary>
    /// 取得指定 culture 的 key/value 字典，所有已知資源檔合併。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetDictionary(string szCulture)
    {
        szCulture = string.IsNullOrWhiteSpace(szCulture) ? "zh-TW" : szCulture.Trim();

        return _cache.GetOrAdd(szCulture, c =>
        {
            var ci = new CultureInfo(c);
            var asm = typeof(Resources.SharedResource).Assembly;
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var szBaseName in _resourceBaseNames.Value)
            {
                try
                {
                    var rm = new ResourceManager(szBaseName, asm);
                    var rs = rm.GetResourceSet(ci, createIfNotExists: true, tryParents: true);
                    if (rs == null) continue;
                    foreach (DictionaryEntry entry in rs)
                    {
                        var szKey = entry.Key?.ToString();
                        if (string.IsNullOrEmpty(szKey)) continue;
                        // 多個 .resx 含同 key 時以最後讀到的為準，理論上不該重複
                        dict[szKey] = entry.Value?.ToString() ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load resource set {Base} for {Culture}", szBaseName, c);
                }
            }

            _logger.LogInformation("I18n built dictionary for {Culture}: {Count} keys", c, dict.Count);
            return dict;
        });
    }
}
