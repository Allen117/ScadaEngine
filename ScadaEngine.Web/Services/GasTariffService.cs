using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.GasTariffSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 氣費設定 — 天然氣流動氣費方案（分段累進，含生效日多版本）的讀寫與驗證。
/// 預設值：Setting/gas-tariff-defaults.json（唯讀 seed，**空白單一級距範本、單價 0**，隨程式部署）；
/// 使用者設定：SystemSettings.gas_tariff（整份 JSON，整份載入整份儲存）。
/// DB 無值時回傳 seed；DB 有值時以 DB 為準，seed 新增的方案自動補齊（by szPlanId）。
/// 計算某期氣費時依「期別起日」選版（SelectPlanForDate）— 生效日 &lt;= 期別起日 中最新者。
/// 結構與 WaterTariffService 對稱但完全獨立（改氣費費率不可能弄壞水費）。
/// </summary>
public class GasTariffService
{
    private const string SettingKey = "gas_tariff";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly ILogger<GasTariffService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly IWebHostEnvironment _env;
    private string _szConnectionString = string.Empty;

    // seed 檔內容快取（部署後不變，重啟才重讀）
    private static volatile GasTariffConfig? _cachedSeed;

    public GasTariffService(
        ILogger<GasTariffService> logger,
        DatabaseConfigService configService,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _configService = configService;
        _env = env;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ---------- seed ----------

    /// <summary>載入氣價預設 seed（缺檔明確報錯 — 部署缺漏要立即暴露，不能靜默給空設定）</summary>
    public GasTariffConfig GetSeed()
    {
        var cached = _cachedSeed;
        if (cached != null) return cached;

        var szPath = Path.Combine(_env.ContentRootPath, "Setting", "gas-tariff-defaults.json");
        if (!File.Exists(szPath))
            throw new FileNotFoundException($"找不到天然氣氣價預設檔：{szPath}", szPath);

        var seed = JsonSerializer.Deserialize<GasTariffConfig>(File.ReadAllText(szPath), _jsonOptions)
            ?? throw new InvalidDataException($"天然氣氣價預設檔解析失敗：{szPath}");
        _cachedSeed = seed;
        return seed;
    }

    // ---------- 讀取 ----------

    /// <summary>
    /// 取得整份設定 — DB 無值回 seed；有值以 DB 為準並補齊 seed 新增方案（by szPlanId）。
    /// </summary>
    public async Task<GasTariffConfig> GetConfigAsync()
    {
        var seed = GetSeed();

        string? szJson;
        using (var conn = await GetConnectionAsync())
        {
            szJson = await conn.QueryFirstOrDefaultAsync<string?>(
                "SELECT SettingValue FROM SystemSettings WHERE SettingKey = @SettingKey",
                new { SettingKey });
        }

        if (string.IsNullOrWhiteSpace(szJson))
            return Clone(seed);

        var config = ParseConfig(szJson);
        if (config == null)
        {
            // DB 內容損毀 → 回 seed（不覆寫 DB，留給使用者儲存時重建）
            _logger.LogError("SystemSettings.{Key} JSON 解析失敗，改用氣價預設範本", SettingKey);
            return Clone(seed);
        }

        // seed 新增方案自動補齊
        var savedIds = config.plans.Select(p => p.szPlanId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var seedPlan in seed.plans)
        {
            if (!savedIds.Contains(seedPlan.szPlanId))
                config.plans.Add(Clone(seedPlan));
        }
        return config;
    }

    // ---------- 寫入 ----------

    /// <summary>儲存整份設定（存前逐方案驗證）。驗證失敗丟 ArgumentException。</summary>
    public async Task SaveConfigAsync(GasTariffConfig config)
    {
        if (config.plans.Count == 0)
            throw new ArgumentException("至少須保留一個方案");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in config.plans)
        {
            var (isValid, szError) = ValidatePlan(plan);
            if (!isValid)
                throw new ArgumentException($"{plan.szName}（{plan.szPlanId}）：{szError}");
            if (!ids.Add(plan.szPlanId))
                throw new ArgumentException($"方案 Id 重複：{plan.szPlanId}");
        }

        var szJson = JsonSerializer.Serialize(config, _jsonOptions);
        const string szSql = @"
            IF EXISTS (SELECT * FROM SystemSettings WHERE SettingKey = @SettingKey)
                UPDATE SystemSettings SET SettingValue = @szJson, UpdatedAt = GETDATE() WHERE SettingKey = @SettingKey;
            ELSE
                INSERT INTO SystemSettings (SettingKey, SettingValue, UpdatedAt) VALUES (@SettingKey, @szJson, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new { SettingKey, szJson });
        _logger.LogInformation("氣費設定已更新（{Count} 個方案版本）", config.plans.Count);
    }

    /// <summary>整份還原預設範本（覆寫 DB），回傳還原後設定</summary>
    public async Task<GasTariffConfig> ResetToSeedAsync()
    {
        var restored = Clone(GetSeed());
        await SaveConfigAsync(restored);
        _logger.LogInformation("氣費設定已整份還原預設範本");
        return restored;
    }

    // ---------- static 純邏輯（單元測試用） ----------

    /// <summary>解析整份設定 JSON — 格式錯誤回 null（呼叫端決定 fallback）</summary>
    public static GasTariffConfig? ParseConfig(string szJson)
    {
        if (string.IsNullOrWhiteSpace(szJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<GasTariffConfig>(szJson, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 依期別起日選用生效方案版本 — 生效日 &lt;= 期別起日 中最新者；
    /// 都不合（期別起日早於所有生效日）則取生效日最早者；無任何方案回 null。
    /// </summary>
    public static GasTariffPlan? SelectPlanForDate(GasTariffConfig config, DateTime dtPeriodStart)
    {
        if (config == null || config.plans.Count == 0) return null;

        GasTariffPlan? best = null;
        var dtBest = DateTime.MinValue;
        GasTariffPlan? earliest = null;
        var dtEarliest = DateTime.MaxValue;

        foreach (var plan in config.plans)
        {
            if (!TryParseDate(plan.szEffectiveDate, out var dtEffective)) continue;
            if (dtEffective <= dtPeriodStart.Date && (best == null || dtEffective > dtBest))
            {
                best = plan;
                dtBest = dtEffective;
            }
            if (earliest == null || dtEffective < dtEarliest)
            {
                earliest = plan;
                dtEarliest = dtEffective;
            }
        }
        // 生效日全數無法解析時退回第一個方案（validate 已擋，防禦性 fallback）
        return best ?? earliest ?? config.plans[0];
    }

    /// <summary>
    /// 方案驗證 — 至少一級距；第一級 nFrom=1；級距連續（下一級 nFrom = 上一級 nTo+1）；
    /// 只有最後一級 nTo 可為 null 且必為 null；單價 &gt;= 0；生效日 yyyy-MM-dd 可解析；szPlanId 非空。
    /// </summary>
    public static (bool isValid, string szError) ValidatePlan(GasTariffPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.szPlanId))
            return (false, "方案 Id 不可為空");

        if (!TryParseDate(plan.szEffectiveDate, out _))
            return (false, $"生效日格式不正確（應為 yyyy-MM-dd）：{plan.szEffectiveDate}");

        if (plan.tiers.Count == 0)
            return (false, "累進級距不可為空");

        if (plan.tiers[0].nFrom != 1)
            return (false, "第一級距下限必須為 1 度");

        for (var i = 0; i < plan.tiers.Count; i++)
        {
            var tier = plan.tiers[i];
            if (tier.dPrice < 0)
                return (false, "級距單價不可為負數");

            var isLast = i == plan.tiers.Count - 1;
            if (isLast)
            {
                if (tier.nTo != null)
                    return (false, "最後一級距上限必須為「以上」（不設上限）");
            }
            else
            {
                if (tier.nTo == null)
                    return (false, "只有最後一級距可以不設上限");
                if (tier.nTo < tier.nFrom)
                    return (false, $"級距上限不可小於下限（{tier.nFrom}~{tier.nTo}）");
                if (plan.tiers[i + 1].nFrom != tier.nTo + 1)
                    return (false, $"級距必須連續：{tier.nTo} 度之後應接 {tier.nTo + 1} 度");
            }
        }
        return (true, string.Empty);
    }

    // ---------- 工具 ----------

    private static bool TryParseDate(string szDate, out DateTime dtDate) =>
        DateTime.TryParseExact(szDate?.Trim() ?? string.Empty, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out dtDate);

    /// <summary>深拷貝（避免 seed 快取被呼叫端修改污染）</summary>
    private static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, _jsonOptions), _jsonOptions)!;
}
