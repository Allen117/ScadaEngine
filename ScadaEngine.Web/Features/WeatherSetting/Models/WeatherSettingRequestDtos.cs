namespace ScadaEngine.Web.Features.WeatherSetting.Models;

/// <summary>氣象資料設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class WeatherSettingViewModel
{
}

/// <summary>POST api/setting — 儲存設定</summary>
public class SaveWeatherSettingRequest
{
    public string apiKey { get; set; } = string.Empty;
    public string datasetId { get; set; } = string.Empty;
    public string stationId { get; set; } = string.Empty;
    public string stationName { get; set; } = string.Empty;
    public string county { get; set; } = string.Empty;
    public int pollIntervalMinutes { get; set; } = 10;
    public bool isEnabled { get; set; }
}

/// <summary>POST api/stations — 載入測站清單（用當下輸入的 key，未存檔也能選站）</summary>
public class WeatherStationsRequest
{
    public string apiKey { get; set; } = string.Empty;
}

/// <summary>POST api/test — 測試連線（抓一次所選測站觀測）</summary>
public class WeatherTestRequest
{
    public string apiKey { get; set; } = string.Empty;
    public string datasetId { get; set; } = string.Empty;
    public string stationId { get; set; } = string.Empty;
}
