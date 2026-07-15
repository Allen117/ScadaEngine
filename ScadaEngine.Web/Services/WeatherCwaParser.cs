using System.Globalization;
using System.Text.Json;
using ScadaEngine.Web.Features.WeatherSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// CWA 開放資料觀測 JSON 解析（純函數，無 I/O — 可獨立驗證）。
/// 實測格式要點（2026-07-15 對 O-A0001-001 / O-A0003-001 驗證）：
/// - 數值欄位皆為字串（"AirTemperature":"29.0"）
/// - 缺測哨兵值 -99（字串 "-99"，亦見 "-99.0"）→ 一律視為 null
/// - 觀測時間在 records.Station[].ObsTime.DateTime（ISO 8601 含 +08:00）
/// - success 欄位為字串 "true"（防禦性同時接受 bool）
/// </summary>
public static class WeatherCwaParser
{
    /// <summary>低於此值視為 -99 系哨兵（台灣氣溫/濕度不可能 ≤ -90）</summary>
    private const double SentinelThreshold = -90.0;

    /// <summary>
    /// 解析 datastore 回應為測站觀測清單。
    /// API 回 success=false 或結構不符時擲 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static List<WeatherStationObservation> ParseStations(string szJson, string szDatasetId)
    {
        using var doc = JsonDocument.Parse(szJson);
        var root = doc.RootElement;

        if (!IsSuccess(root))
            throw new InvalidOperationException("CWA API 回應 success != true");

        var result = new List<WeatherStationObservation>();
        if (!root.TryGetProperty("records", out var records) ||
            !records.TryGetProperty("Station", out var stations) ||
            stations.ValueKind != JsonValueKind.Array)
        {
            // 查無資料時 CWA 可能回空 records — 視為空清單而非錯誤
            return result;
        }

        foreach (var st in stations.EnumerateArray())
        {
            var obs = new WeatherStationObservation
            {
                szDatasetId = szDatasetId,
                szStationId = GetString(st, "StationId"),
                szStationName = GetString(st, "StationName"),
                dtObsTime = ParseObsTime(st)
            };

            if (st.TryGetProperty("GeoInfo", out var geo))
            {
                obs.szCounty = GetString(geo, "CountyName");
                obs.szTown = GetString(geo, "TownName");
            }

            if (st.TryGetProperty("WeatherElement", out var we))
            {
                obs.fTemperature = ParseValue(we, "AirTemperature");
                obs.fHumidity = ParseValue(we, "RelativeHumidity");
            }

            result.Add(obs);
        }
        return result;
    }

    private static bool IsSuccess(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success)) return false;
        return success.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(success.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string GetString(JsonElement parent, string szName)
    {
        return parent.TryGetProperty(szName, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>ObsTime.DateTime（ISO 8601 含時區）→ 本地 DateTime；缺欄或解析失敗 = null</summary>
    private static DateTime? ParseObsTime(JsonElement station)
    {
        if (!station.TryGetProperty("ObsTime", out var obsTime) ||
            !obsTime.TryGetProperty("DateTime", out var dtEl) ||
            dtEl.ValueKind != JsonValueKind.String)
            return null;

        var szValue = dtEl.GetString();
        if (string.IsNullOrEmpty(szValue) || szValue.StartsWith("-99", StringComparison.Ordinal))
            return null;

        return DateTimeOffset.TryParse(szValue, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dto)
            ? dto.LocalDateTime
            : null;
    }

    /// <summary>數值欄位（字串或數字皆吃）；-99 系哨兵或解析失敗 = null</summary>
    private static double? ParseValue(JsonElement parent, string szName)
    {
        if (!parent.TryGetProperty(szName, out var el)) return null;

        double fValue;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                fValue = el.GetDouble();
                break;
            case JsonValueKind.String:
                if (!double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out fValue))
                    return null;
                break;
            default:
                return null;
        }
        return fValue <= SentinelThreshold ? null : fValue;
    }
}
