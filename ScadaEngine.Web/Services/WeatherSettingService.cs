using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.WeatherSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 氣象資料來源設定 — WeatherSetting 單列表（Id=1）讀寫 + 觀測值寫入 DBLatestData。
/// 寫值以 DBCoordinator.Name='Weather' + DBPoints.Sequence JOIN 定位 SID
/// （同 UpdateTimeSim.sql 慣例 — CoordinatorId 由 IDENTITY 配發，換環境會變，不可寫死 DB{n}-S{m}）。
/// S1 = 外氣溫度、S2 = 外氣相對濕度（Sequence 由 Weather.json 陣列順序決定，永不插隊）。
/// </summary>
public class WeatherSettingService
{
    /// <summary>DB 來源 Coordinator 名稱（對應 DBPoint/Weather.json 的 Name）</summary>
    public const string CoordinatorName = "Weather";

    public const int SeqTemperature = 1;
    public const int SeqHumidity = 2;

    private readonly ILogger<WeatherSettingService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public WeatherSettingService(ILogger<WeatherSettingService> logger, DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>讀取設定；表無資料列時回傳預設值（未啟用、間隔 10 分）</summary>
    public async Task<WeatherSettingModel> GetAsync()
    {
        using var conn = await GetConnectionAsync();
        var setting = await conn.QueryFirstOrDefaultAsync<WeatherSettingModel>(@"
            SELECT ApiKey              AS szApiKey,
                   DatasetId           AS szDatasetId,
                   StationId           AS szStationId,
                   StationName         AS szStationName,
                   County              AS szCounty,
                   PollIntervalMinutes AS nPollIntervalMinutes,
                   IsEnabled           AS isEnabled,
                   LastFetchTime       AS dtLastFetchTime,
                   LastFetchOk         AS isLastFetchOk,
                   LastFetchMessage    AS szLastFetchMessage
            FROM   WeatherSetting WHERE Id = 1");
        return setting ?? new WeatherSettingModel();
    }

    /// <summary>儲存設定（UPSERT Id=1；不動 LastFetch* 狀態欄）</summary>
    public async Task SaveAsync(WeatherSettingModel m)
    {
        using var conn = await GetConnectionAsync();
        var nAffected = await conn.ExecuteAsync(@"
            UPDATE WeatherSetting
            SET    ApiKey = @szApiKey, DatasetId = @szDatasetId, StationId = @szStationId,
                   StationName = @szStationName, County = @szCounty,
                   PollIntervalMinutes = @nPollIntervalMinutes, IsEnabled = @isEnabled,
                   UpdatedAt = GETDATE()
            WHERE  Id = 1", m);

        if (nAffected == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO WeatherSetting
                       (Id, ApiKey, DatasetId, StationId, StationName, County,
                        PollIntervalMinutes, IsEnabled, UpdatedAt)
                VALUES (1, @szApiKey, @szDatasetId, @szStationId, @szStationName, @szCounty,
                        @nPollIntervalMinutes, @isEnabled, GETDATE())", m);
        }

        _logger.LogInformation(
            "氣象資料設定已更新：Enabled={Enabled}, Station={Station}({Id}), Interval={Interval}min",
            m.isEnabled, m.szStationName, m.szStationId, m.nPollIntervalMinutes);
    }

    /// <summary>回寫最近一次抓取結果（設定列不存在時忽略 — 尚未儲存過設定不會有抓取）</summary>
    public async Task UpdateFetchStatusAsync(bool isOk, string szMessage)
    {
        if (szMessage.Length > 200) szMessage = szMessage[..200];
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE WeatherSetting
            SET    LastFetchTime = GETDATE(), LastFetchOk = @isOk, LastFetchMessage = @szMessage
            WHERE  Id = 1", new { isOk, szMessage });
    }

    /// <summary>
    /// 觀測值寫入 DBLatestData（Weather S1/S2）。
    /// 好值：寫 Value + Timestamp（觀測時間，DB 來源語意＝外部真實時間）+ Quality=1；
    /// 壞值（缺測/過舊/抓取失敗）：只降 Quality=0，保留最近成功值與時間。
    /// 回傳實際更新列數 — 0 表示 Engine 尚未載入 Weather.json（DBLatestData 無 seed 列）。
    /// </summary>
    public async Task<int> WriteObservationAsync(double? fTemperature, double? fHumidity, DateTime? dtObsTime, bool isFresh)
    {
        using var conn = await GetConnectionAsync();
        var nUpdated = 0;
        nUpdated += await WritePointAsync(conn, SeqTemperature, fTemperature, dtObsTime, isFresh);
        nUpdated += await WritePointAsync(conn, SeqHumidity, fHumidity, dtObsTime, isFresh);
        return nUpdated;
    }

    private static async Task<int> WritePointAsync(
        SqlConnection conn, int nSequence, double? fValue, DateTime? dtObsTime, bool isFresh)
    {
        var isGood = isFresh && fValue != null && dtObsTime != null;
        if (isGood)
        {
            return await conn.ExecuteAsync(@"
                UPDATE d
                SET    d.Value = @fValue, d.[Timestamp] = @dtObsTime, d.Quality = 1
                FROM   DBLatestData d
                JOIN   DBPoints p ON p.SID = d.SID
                JOIN   DBCoordinator c ON c.Id = p.CoordinatorId
                WHERE  c.Name = @szName AND p.Sequence = @nSequence",
                new { fValue, dtObsTime, szName = CoordinatorName, nSequence });
        }

        return await conn.ExecuteAsync(@"
            UPDATE d
            SET    d.Quality = 0
            FROM   DBLatestData d
            JOIN   DBPoints p ON p.SID = d.SID
            JOIN   DBCoordinator c ON c.Id = p.CoordinatorId
            WHERE  c.Name = @szName AND p.Sequence = @nSequence",
            new { szName = CoordinatorName, nSequence });
    }
}
