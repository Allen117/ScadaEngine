namespace ScadaEngine.Web.Features.WeatherSetting.Models;

/// <summary>
/// CWA 單一測站的一筆觀測（測站清單與單站抓取共用同一結構）。
/// fTemperature / fHumidity 為 null 表示該欄缺測（API 哨兵值 -99）或解析失敗。
/// </summary>
public class WeatherStationObservation
{
    /// <summary>所屬資料集：O-A0001-001（自動站）/ O-A0003-001（署屬有人站）</summary>
    public string szDatasetId { get; set; } = string.Empty;

    public string szStationId { get; set; } = string.Empty;
    public string szStationName { get; set; } = string.Empty;
    public string szCounty { get; set; } = string.Empty;
    public string szTown { get; set; } = string.Empty;

    /// <summary>氣溫（°C）；缺測 = null</summary>
    public double? fTemperature { get; set; }

    /// <summary>相對濕度（%）；缺測 = null</summary>
    public double? fHumidity { get; set; }

    /// <summary>觀測時間（本地時間）；解析失敗 = null</summary>
    public DateTime? dtObsTime { get; set; }
}

/// <summary>WeatherSetting 表單列設定（Id 固定 1）</summary>
public class WeatherSettingModel
{
    public string szApiKey { get; set; } = string.Empty;
    public string szDatasetId { get; set; } = string.Empty;
    public string szStationId { get; set; } = string.Empty;
    public string szStationName { get; set; } = string.Empty;
    public string szCounty { get; set; } = string.Empty;
    public int nPollIntervalMinutes { get; set; } = 10;
    public bool isEnabled { get; set; }
    public DateTime? dtLastFetchTime { get; set; }
    public bool? isLastFetchOk { get; set; }
    public string? szLastFetchMessage { get; set; }
}
