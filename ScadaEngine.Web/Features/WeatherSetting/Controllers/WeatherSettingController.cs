using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.WeatherSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.WeatherSetting.Controllers;

/// <summary>
/// 氣象資料來源設定 — CWA 開放資料授權碼 + 縣市/測站選擇 + 抓取間隔/啟用。
/// 實際抓取由 WeatherFetchService 背景執行；本 Controller 另提供
/// 測站清單代理（key 不暴露於瀏覽器直連 CWA）與測試連線。
/// API 錯誤回傳 message 為機器碼（invalid_interval、cwa_auth_failed…），由前端依語系翻譯。
/// </summary>
[Authorize]
[Route("[controller]")]
public class WeatherSettingController : Controller
{
    private readonly WeatherSettingService _settingService;
    private readonly WeatherCwaClient _cwaClient;
    private readonly ILogger<WeatherSettingController> _logger;

    public WeatherSettingController(
        WeatherSettingService settingService,
        WeatherCwaClient cwaClient,
        ILogger<WeatherSettingController> logger)
    {
        _settingService = settingService;
        _cwaClient = cwaClient;
        _logger = logger;
    }

    [HttpGet("/WeatherSetting")]
    public IActionResult Index()
    {
        return View(new WeatherSettingViewModel());
    }

    /// <summary>取得目前設定（含最近抓取狀態）</summary>
    [HttpGet("api/setting")]
    public async Task<IActionResult> GetSetting()
    {
        try
        {
            var s = await _settingService.GetAsync();
            return Ok(new
            {
                apiKey = s.szApiKey,
                datasetId = s.szDatasetId,
                stationId = s.szStationId,
                stationName = s.szStationName,
                county = s.szCounty,
                pollIntervalMinutes = s.nPollIntervalMinutes,
                isEnabled = s.isEnabled,
                lastFetchTime = s.dtLastFetchTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                lastFetchOk = s.isLastFetchOk,
                lastFetchMessage = s.szLastFetchMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣象資料設定載入失敗");
            return StatusCode(500, new { message = "load_failed" });
        }
    }

    /// <summary>儲存設定 — 背景服務最慢 30 秒內套用新設定並立即抓一次</summary>
    [HttpPost("api/setting")]
    public async Task<IActionResult> SaveSetting([FromBody] SaveWeatherSettingRequest dto)
    {
        if (dto.pollIntervalMinutes < WeatherFetchService.MIN_INTERVAL_MINUTES || dto.pollIntervalMinutes > 1440)
            return BadRequest(new { message = "invalid_interval" });
        if (dto.apiKey.Length > 100)
            return BadRequest(new { message = "invalid_api_key" });
        if (dto.isEnabled &&
            (string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.stationId) ||
             string.IsNullOrWhiteSpace(dto.datasetId)))
            return BadRequest(new { message = "enable_requires_key_and_station" });
        if (!string.IsNullOrWhiteSpace(dto.datasetId) && !WeatherCwaClient.DatasetIds.Contains(dto.datasetId))
            return BadRequest(new { message = "invalid_dataset" });

        try
        {
            await _settingService.SaveAsync(new WeatherSettingModel
            {
                szApiKey = dto.apiKey.Trim(),
                szDatasetId = dto.datasetId.Trim(),
                szStationId = dto.stationId.Trim(),
                szStationName = dto.stationName.Trim(),
                szCounty = dto.county.Trim(),
                nPollIntervalMinutes = dto.pollIntervalMinutes,
                isEnabled = dto.isEnabled
            });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "氣象資料設定儲存失敗");
            return StatusCode(500, new { message = "save_failed" });
        }
    }

    /// <summary>
    /// 載入可選測站清單（雙資料集合併、僅溫濕度皆有效的測站；Client 端快取 1 小時）。
    /// 用當下輸入的 key，讓使用者存檔前就能選站。
    /// </summary>
    [HttpPost("api/stations")]
    public async Task<IActionResult> GetStations([FromBody] WeatherStationsRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.apiKey))
            return BadRequest(new { message = "api_key_required" });

        try
        {
            var stations = await _cwaClient.GetStationListAsync(dto.apiKey.Trim());
            return Ok(new
            {
                stations = stations.Select(s => new
                {
                    datasetId = s.szDatasetId,
                    stationId = s.szStationId,
                    stationName = s.szStationName,
                    county = s.szCounty,
                    town = s.szTown
                })
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CWA 測站清單載入失敗（授權/回應異常）");
            return StatusCode(502, new { message = "cwa_auth_failed" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CWA 測站清單載入失敗");
            return StatusCode(502, new { message = "cwa_unreachable" });
        }
    }

    /// <summary>測試連線 — 用表單當下的 key/測站抓一次觀測，回傳讀到的溫濕度</summary>
    [HttpPost("api/test")]
    public async Task<IActionResult> Test([FromBody] WeatherTestRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.stationId) ||
            string.IsNullOrWhiteSpace(dto.datasetId))
            return BadRequest(new { message = "test_requires_key_and_station" });
        if (!WeatherCwaClient.DatasetIds.Contains(dto.datasetId))
            return BadRequest(new { message = "invalid_dataset" });

        try
        {
            var obs = await _cwaClient.GetObservationAsync(dto.apiKey.Trim(), dto.datasetId, dto.stationId.Trim());
            if (obs == null)
                return Ok(new { success = false, message = "station_not_found" });

            return Ok(new
            {
                success = true,
                stationName = obs.szStationName,
                temperature = obs.fTemperature,
                humidity = obs.fHumidity,
                obsTime = obs.dtObsTime?.ToString("yyyy-MM-dd HH:mm")
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CWA 測試連線失敗（授權/回應異常）");
            return StatusCode(502, new { message = "cwa_auth_failed" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CWA 測試連線失敗");
            return StatusCode(502, new { message = "cwa_unreachable" });
        }
    }
}
