using ScadaEngine.Web.Features.WeatherSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 中央氣象署（CWA）開放資料 API client — 測站清單（雙資料集合併 + 1hr 快取）與單站觀測讀取。
/// Singleton；HTTP 逾時 15 秒。API 錯誤以例外上拋（HttpRequestException / InvalidOperationException），
/// 由呼叫端（WeatherFetchService / Controller）轉為狀態訊息。
/// </summary>
public class WeatherCwaClient
{
    private const string BaseUrl = "https://opendata.cwa.gov.tw/api/v1/rest/datastore/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StationCacheTtl = TimeSpan.FromHours(1);

    /// <summary>觀測資料集：自動氣象站 / 署屬有人站（測站清單取聯集）</summary>
    public static readonly string[] DatasetIds = ["O-A0001-001", "O-A0003-001"];

    private readonly ILogger<WeatherCwaClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // 測站清單快取（清單內容與 key 無關，全域一份即可）
    private readonly SemaphoreSlim _stationLock = new(1, 1);
    private List<WeatherStationObservation>? _cachedStations;
    private DateTime _dtStationCachedAt = DateTime.MinValue;

    public WeatherCwaClient(ILogger<WeatherCwaClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 取得可選測站清單（兩資料集合併，只留溫度與濕度當下皆有效的測站），快取 1 小時。
    /// </summary>
    public async Task<List<WeatherStationObservation>> GetStationListAsync(string szApiKey, CancellationToken ct = default)
    {
        var cached = _cachedStations;
        if (cached != null && DateTime.Now - _dtStationCachedAt < StationCacheTtl)
            return cached;

        await _stationLock.WaitAsync(ct);
        try
        {
            // double-check：等鎖期間可能已有人填好快取
            cached = _cachedStations;
            if (cached != null && DateTime.Now - _dtStationCachedAt < StationCacheTtl)
                return cached;

            var merged = new List<WeatherStationObservation>();
            foreach (var szDatasetId in DatasetIds)
            {
                var szJson = await GetRawAsync(szApiKey, szDatasetId, szStationId: null, ct);
                merged.AddRange(WeatherCwaParser.ParseStations(szJson, szDatasetId));
            }

            var list = merged
                .Where(s => s.fTemperature != null && s.fHumidity != null &&
                            !string.IsNullOrEmpty(s.szStationId))
                .OrderBy(s => s.szCounty).ThenBy(s => s.szStationName)
                .ToList();

            _cachedStations = list;
            _dtStationCachedAt = DateTime.Now;
            _logger.LogInformation("CWA 測站清單已更新：{Count} 站（合併 {Datasets}）",
                list.Count, string.Join(", ", DatasetIds));
            return list;
        }
        finally
        {
            _stationLock.Release();
        }
    }

    /// <summary>讀取單一測站當下觀測；查無該站回 null</summary>
    public async Task<WeatherStationObservation?> GetObservationAsync(
        string szApiKey, string szDatasetId, string szStationId, CancellationToken ct = default)
    {
        if (!DatasetIds.Contains(szDatasetId))
            throw new ArgumentException($"未知的資料集：{szDatasetId}");

        var szJson = await GetRawAsync(szApiKey, szDatasetId, szStationId, ct);
        var stations = WeatherCwaParser.ParseStations(szJson, szDatasetId);
        return stations.FirstOrDefault(s =>
            string.Equals(s.szStationId, szStationId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GetRawAsync(string szApiKey, string szDatasetId, string? szStationId, CancellationToken ct)
    {
        var szUrl = $"{BaseUrl}{szDatasetId}?Authorization={Uri.EscapeDataString(szApiKey)}";
        if (!string.IsNullOrEmpty(szStationId))
            szUrl += $"&StationId={Uri.EscapeDataString(szStationId)}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(szUrl, cts.Token);

        // 401/403 = 授權碼無效，訊息對使用者有意義，特別標示
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("CWA API 授權碼無效或無權限");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"CWA API HTTP {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync(cts.Token);
    }
}
