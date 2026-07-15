using ScadaEngine.Web.Features.WeatherSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 氣象資料抓取背景服務（Web 端）。
/// 每 30 秒讀 WeatherSetting 判斷是否到期（設定變更即刻生效），到期就呼叫 CWA API
/// 抓所選測站觀測，寫入 DB 來源 Weather Coordinator 的 DBLatestData（S1 溫度 / S2 濕度），
/// 再由 Engine DbCommunicationService polling 進 SCADA pipeline。
///
/// Quality 規則（防「舊溫度當好值餵基線」）：
/// - API 失敗 / 查無測站 / 觀測時間距今 > 60 分（測站掛掉 API 仍回舊資料）→ Quality=0，保留最近成功值
/// - 個別欄位缺測（-99）→ 該點位 Quality=0，另一點位照常寫
/// - 啟用 → 停用的瞬間補寫一次 Quality=0，讓下游知道資料已失效
///
/// LastFetchMessage 存機器碼格式（ok|…、stale|…、error|…），由設定頁前端依語系翻譯。
/// Singleton BackgroundService — WeatherSettingService 為 Scoped，透過 CreateScope 取用。
/// </summary>
public class WeatherFetchService : BackgroundService
{
    /// <summary>設定檢查週期（秒）— 設定變更最慢此延遲後生效</summary>
    private const int CHECK_INTERVAL_SECONDS = 30;

    /// <summary>觀測時間距今超過此分鐘數視為過舊（CWA 觀測約 10 分鐘~1 小時更新）</summary>
    private const int STALE_MINUTES = 60;

    /// <summary>抓取間隔下限（分鐘）— 觀測更新頻率以下抓再密也沒意義</summary>
    public const int MIN_INTERVAL_MINUTES = 5;

    private readonly ILogger<WeatherFetchService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly WeatherCwaClient _cwaClient;

    private DateTime _dtNextDue = DateTime.MinValue;
    private string _szLastFingerprint = string.Empty;
    private bool _isWasActive;

    public WeatherFetchService(
        ILogger<WeatherFetchService> logger,
        IServiceProvider serviceProvider,
        WeatherCwaClient cwaClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _cwaClient = cwaClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("氣象資料抓取服務啟動，StaleMinutes={Stale}, MinInterval={Min}min",
            STALE_MINUTES, MIN_INTERVAL_MINUTES);

        // 等 Web 啟動完（DB schema 同步等）再進主迴圈
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "氣象資料抓取主迴圈發生錯誤");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("氣象資料抓取服務已停止");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<WeatherSettingService>();
        var setting = await settingService.GetAsync();

        var isActive = setting.isEnabled &&
                       !string.IsNullOrWhiteSpace(setting.szApiKey) &&
                       !string.IsNullOrWhiteSpace(setting.szStationId) &&
                       !string.IsNullOrWhiteSpace(setting.szDatasetId);

        // 設定變更（含啟用/換站/換 key/改間隔）→ 立即重新排程
        var szFingerprint =
            $"{setting.szApiKey}|{setting.szDatasetId}|{setting.szStationId}|{setting.isEnabled}|{setting.nPollIntervalMinutes}";
        if (szFingerprint != _szLastFingerprint)
        {
            _szLastFingerprint = szFingerprint;
            _dtNextDue = DateTime.MinValue;
        }

        if (!isActive)
        {
            // 啟用 → 停用的瞬間：補寫 Quality=0 讓下游知道資料失效
            if (_isWasActive)
            {
                _isWasActive = false;
                try
                {
                    await settingService.WriteObservationAsync(null, null, null, isFresh: false);
                    await settingService.UpdateFetchStatusAsync(false, "disabled");
                    _logger.LogInformation("氣象資料抓取已停用，Weather 點位已標記 Quality=0");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "停用時標記 Weather 點位 Quality=0 失敗");
                }
            }
            return;
        }

        _isWasActive = true;
        if (DateTime.Now < _dtNextDue) return;

        var nIntervalMinutes = Math.Max(MIN_INTERVAL_MINUTES, setting.nPollIntervalMinutes);
        _dtNextDue = DateTime.Now.AddMinutes(nIntervalMinutes);

        await FetchOnceAsync(settingService, setting, ct);
    }

    private async Task FetchOnceAsync(WeatherSettingService settingService, WeatherSettingModel setting, CancellationToken ct)
    {
        try
        {
            var obs = await _cwaClient.GetObservationAsync(
                setting.szApiKey, setting.szDatasetId, setting.szStationId, ct);

            if (obs == null)
            {
                await settingService.WriteObservationAsync(null, null, null, isFresh: false);
                await settingService.UpdateFetchStatusAsync(false, "station_not_found");
                _logger.LogWarning("CWA 查無測站 {StationId}（{Dataset}）", setting.szStationId, setting.szDatasetId);
                return;
            }

            var isFresh = obs.dtObsTime != null &&
                          DateTime.Now - obs.dtObsTime.Value <= TimeSpan.FromMinutes(STALE_MINUTES);

            var nUpdated = await settingService.WriteObservationAsync(
                obs.fTemperature, obs.fHumidity, obs.dtObsTime, isFresh);

            if (nUpdated == 0)
            {
                // Engine 尚未載入 Weather.json（DBLatestData 無 seed 列）
                await settingService.UpdateFetchStatusAsync(false, "coordinator_not_loaded");
                _logger.LogWarning("Weather Coordinator 尚未載入（DBLatestData 無對應列），觀測值未寫入");
                return;
            }

            var szObsTime = obs.dtObsTime?.ToString("yyyy-MM-dd HH:mm") ?? "-";
            var szTemp = obs.fTemperature?.ToString("0.0") ?? "-";
            var szHum = obs.fHumidity?.ToString("0.0") ?? "-";

            if (!isFresh)
            {
                await settingService.UpdateFetchStatusAsync(false, $"stale|{szObsTime}");
                _logger.LogWarning("CWA 測站 {Station} 觀測時間過舊：{ObsTime}", setting.szStationId, szObsTime);
            }
            else if (obs.fTemperature == null || obs.fHumidity == null)
            {
                await settingService.UpdateFetchStatusAsync(false, $"missing|{szTemp}|{szHum}|{szObsTime}");
                _logger.LogWarning("CWA 測站 {Station} 部分欄位缺測：T={Temp}, RH={Hum}",
                    setting.szStationId, szTemp, szHum);
            }
            else
            {
                await settingService.UpdateFetchStatusAsync(true, $"ok|{szTemp}|{szHum}|{szObsTime}");
                _logger.LogDebug("氣象觀測寫入完成：{Station} T={Temp}°C RH={Hum}% @{ObsTime}",
                    setting.szStationId, szTemp, szHum, szObsTime);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CWA 抓取失敗（{Station}）", setting.szStationId);
            try
            {
                await settingService.WriteObservationAsync(null, null, null, isFresh: false);
                await settingService.UpdateFetchStatusAsync(false, $"error|{ex.Message}");
            }
            catch (Exception exInner)
            {
                _logger.LogError(exInner, "回寫氣象抓取失敗狀態時發生錯誤");
            }
        }
    }
}
