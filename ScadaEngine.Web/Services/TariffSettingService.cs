using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.TariffSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 電費設定 — 台電各類電價方案的讀寫與驗證。
/// 台電預設值：Setting/tariff-taipower-defaults.json（唯讀 seed，隨程式部署）；
/// 使用者設定：SystemSettings.electricity_tariff（整份 JSON，整份載入整份儲存）。
/// DB 無值時回傳 seed；DB 有值時以 DB 為準，seed 新增的方案自動補齊（by szPlanId）。
/// 本版只管設定 — 電費計算（接主要電表）留待後續版本。
/// </summary>
public class TariffSettingService
{
    private const string SettingKey = "electricity_tariff";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly ILogger<TariffSettingService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly IWebHostEnvironment _env;
    private string _szConnectionString = string.Empty;

    // seed 檔內容快取（部署後不變，重啟才重讀）
    private static volatile TariffConfig? _cachedSeed;

    public TariffSettingService(
        ILogger<TariffSettingService> logger,
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

    /// <summary>載入台電預設 seed（缺檔明確報錯 — 部署缺漏要立即暴露，不能靜默給空設定）</summary>
    public TariffConfig GetSeed()
    {
        var cached = _cachedSeed;
        if (cached != null) return cached;

        var szPath = Path.Combine(_env.ContentRootPath, "Setting", "tariff-taipower-defaults.json");
        if (!File.Exists(szPath))
            throw new FileNotFoundException($"找不到台電電價預設檔：{szPath}", szPath);

        var seed = JsonSerializer.Deserialize<TariffConfig>(File.ReadAllText(szPath), _jsonOptions)
            ?? throw new InvalidDataException($"台電電價預設檔解析失敗：{szPath}");
        _cachedSeed = seed;
        return seed;
    }

    // ---------- 讀取 ----------

    /// <summary>
    /// 取得整份設定 — DB 無值回 seed；有值以 DB 為準並補齊 seed 新增方案。
    /// </summary>
    public async Task<TariffConfig> GetConfigAsync()
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

        TariffConfig config;
        try
        {
            config = JsonSerializer.Deserialize<TariffConfig>(szJson, _jsonOptions) ?? Clone(seed);
        }
        catch (JsonException ex)
        {
            // DB 內容損毀 → 回 seed（不覆寫 DB，留給使用者儲存時重建）
            _logger.LogError(ex, "SystemSettings.{Key} JSON 解析失敗，改用台電預設", SettingKey);
            return Clone(seed);
        }

        // seed 新增方案（台電新版電價表）自動補齊
        var savedIds = config.plans.Select(p => p.szPlanId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var seedPlan in seed.plans)
        {
            if (!savedIds.Contains(seedPlan.szPlanId))
                config.plans.Add(Clone(seedPlan));
        }
        return config;
    }

    // ---------- 寫入 ----------

    /// <summary>儲存單一方案（整份設定讀出 → 替換 → 存回）。驗證失敗丟 ArgumentException。</summary>
    public async Task SavePlanAsync(TariffPlan plan)
    {
        var szError = ValidatePlan(plan);
        if (szError != null)
            throw new ArgumentException(szError);

        var config = await GetConfigAsync();
        var nIdx = config.plans.FindIndex(p =>
            string.Equals(p.szPlanId, plan.szPlanId, StringComparison.OrdinalIgnoreCase));
        if (nIdx < 0)
            throw new ArgumentException($"方案不存在：{plan.szPlanId}");

        // 類別/型態為結構性欄位，以既有值為準（防前端竄改造成渲染錯亂）
        plan.szCategory = config.plans[nIdx].szCategory;
        plan.szType = config.plans[nIdx].szType;
        config.plans[nIdx] = plan;

        await SaveConfigAsync(config);
        _logger.LogInformation("電費設定已更新方案 {PlanId}", plan.szPlanId);
    }

    /// <summary>設定採用方案（planId 須存在）</summary>
    public async Task SetActivePlanAsync(string szPlanId)
    {
        var config = await GetConfigAsync();
        if (!config.plans.Any(p => string.Equals(p.szPlanId, szPlanId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"方案不存在：{szPlanId}");

        config.szActivePlanId = szPlanId;
        await SaveConfigAsync(config);
        _logger.LogInformation("電費設定採用方案切換為 {PlanId}", szPlanId);
    }

    /// <summary>還原單一方案為台電預設，回傳還原後的方案</summary>
    public async Task<TariffPlan> ResetPlanAsync(string szPlanId)
    {
        var seed = GetSeed();
        var seedPlan = seed.plans.FirstOrDefault(p =>
            string.Equals(p.szPlanId, szPlanId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"方案不存在：{szPlanId}");

        var config = await GetConfigAsync();
        var nIdx = config.plans.FindIndex(p =>
            string.Equals(p.szPlanId, szPlanId, StringComparison.OrdinalIgnoreCase));
        var restored = Clone(seedPlan);
        if (nIdx >= 0) config.plans[nIdx] = restored;
        else config.plans.Add(restored);

        await SaveConfigAsync(config);
        _logger.LogInformation("電費設定方案 {PlanId} 已還原台電預設", szPlanId);
        return restored;
    }

    private async Task SaveConfigAsync(TariffConfig config)
    {
        var szJson = JsonSerializer.Serialize(config, _jsonOptions);
        const string szSql = @"
            IF EXISTS (SELECT * FROM SystemSettings WHERE SettingKey = @SettingKey)
                UPDATE SystemSettings SET SettingValue = @szJson, UpdatedAt = GETDATE() WHERE SettingKey = @SettingKey;
            ELSE
                INSERT INTO SystemSettings (SettingKey, SettingValue, UpdatedAt) VALUES (@SettingKey, @szJson, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new { SettingKey, szJson });
    }

    // ---------- 驗證 ----------

    private static readonly string[] _dayTypes = ["weekday", "sat", "sun_offday"];
    private static readonly string[] _seasons = ["summer", "nonsummer"];

    /// <summary>方案驗證 — 回傳錯誤訊息，null = 通過</summary>
    public string? ValidatePlan(TariffPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.szPlanId))
            return "方案 Id 不可為空";

        if (!TryParseMonthDay(plan.szSummerStart, out _, out _) ||
            !TryParseMonthDay(plan.szSummerEnd, out _, out _))
            return "夏月起訖日期格式不正確（MM-dd）";

        foreach (var fee in plan.baseFees)
        {
            if (fee.dSummer is < 0 || fee.dNonSummer is < 0)
                return "基本電費單價不可為負數";
        }

        if (plan.surcharge != null)
        {
            if (plan.surcharge.nOverKwh <= 0)
                return "超額加價門檻度數必須大於 0";
            if (plan.surcharge.dPrice < 0)
                return "超額加價單價不可為負數";
        }

        return plan.szType switch
        {
            "progressive" => ValidateTiers(plan.tiers),
            "flat" => ValidateFlat(plan.flatRate),
            "tou" => ValidateFlowRates(plan.flowRates),
            _ => $"未知的方案型態：{plan.szType}",
        };
    }

    private static string? ValidateTiers(List<TariffTier> tiers)
    {
        if (tiers.Count == 0)
            return "累進級距不可為空";

        for (var i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (tier.dSummer < 0 || tier.dNonSummer < 0)
                return "級距單價不可為負數";

            var isLast = i == tiers.Count - 1;
            if (isLast)
            {
                if (tier.nTo != null)
                    return "最後一級距上限必須為「以上」（不設上限）";
            }
            else
            {
                if (tier.nTo == null)
                    return "只有最後一級距可以不設上限";
                if (tier.nTo <= tier.nFrom)
                    return $"級距上限必須大於下限（{tier.nFrom}~{tier.nTo}）";
                if (tiers[i + 1].nFrom != tier.nTo + 1)
                    return $"級距必須連續：{tier.nTo} 度之後應接 {tier.nTo + 1} 度";
            }
        }
        return null;
    }

    private static string? ValidateFlat(TariffFlatRate? flatRate)
    {
        if (flatRate == null)
            return "單一費率不可為空";
        if (flatRate.dSummer < 0 || flatRate.dNonSummer < 0)
            return "流動電費單價不可為負數";
        return null;
    }

    /// <summary>
    /// TOU 驗證 — 每（日別 × 季節）組：時段聯集覆蓋 00:00–24:00 且互不重疊（允許跨午夜）。
    /// </summary>
    private static string? ValidateFlowRates(List<TariffFlowRate> flowRates)
    {
        if (flowRates.Count == 0)
            return "時間電價時段不可為空";

        foreach (var rate in flowRates)
        {
            if (rate.dPrice < 0)
                return "流動電費單價不可為負數";
        }

        foreach (var szDayType in _dayTypes)
        {
            foreach (var szSeason in _seasons)
            {
                var group = flowRates
                    .Where(r => r.szDayType == szDayType && r.szSeason == szSeason)
                    .ToList();
                if (group.Count == 0)
                    return $"缺少時段定義：{DayTypeLabel(szDayType)} × {SeasonLabel(szSeason)}";

                var szError = ValidateCoverage(group, szDayType, szSeason);
                if (szError != null) return szError;
            }
        }
        return null;
    }

    private static string? ValidateCoverage(List<TariffFlowRate> group, string szDayType, string szSeason)
    {
        var szWhere = $"{DayTypeLabel(szDayType)} × {SeasonLabel(szSeason)}";
        // 展開為分鐘區間（跨午夜切成兩段）
        var intervals = new List<(int nStart, int nEnd)>();
        foreach (var rate in group)
        {
            if (rate.ranges.Count == 0)
                return $"{szWhere}：時段列缺少時間區間";
            foreach (var szRange in rate.ranges)
            {
                var parts = szRange.Split('-');
                if (parts.Length != 2 ||
                    !TryParseTime(parts[0], out var nStart) ||
                    !TryParseTime(parts[1], out var nEnd))
                    return $"{szWhere}：時間區間格式不正確（{szRange}，應為 HH:mm-HH:mm）";
                if (nStart == nEnd)
                    return $"{szWhere}：時間區間起訖不可相同（{szRange}）";

                if (nStart < nEnd)
                {
                    intervals.Add((nStart, nEnd));
                }
                else
                {
                    // 跨午夜 → 拆兩段
                    intervals.Add((nStart, 1440));
                    if (nEnd > 0) intervals.Add((0, nEnd));
                }
            }
        }

        intervals.Sort((a, b) => a.nStart.CompareTo(b.nStart));
        var nCursor = 0;
        foreach (var (nStart, nEnd) in intervals)
        {
            if (nStart < nCursor)
                return $"{szWhere}：時段重疊（{ToHHmm(nStart)} 前後）";
            if (nStart > nCursor)
                return $"{szWhere}：時段有空隙（{ToHHmm(nCursor)}~{ToHHmm(nStart)} 未涵蓋）";
            nCursor = nEnd;
        }
        if (nCursor != 1440)
            return $"{szWhere}：時段有空隙（{ToHHmm(nCursor)}~24:00 未涵蓋）";
        return null;
    }

    // ---------- 工具 ----------

    /// <summary>解析 "HH:mm" 為當日分鐘數；"24:00" 視為 1440</summary>
    private static bool TryParseTime(string szTime, out int nMinutes)
    {
        nMinutes = 0;
        var parts = szTime.Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var nHour) || !int.TryParse(parts[1], out var nMin)) return false;
        if (nHour == 24 && nMin == 0) { nMinutes = 1440; return true; }
        if (nHour < 0 || nHour > 23 || nMin < 0 || nMin > 59) return false;
        nMinutes = nHour * 60 + nMin;
        return true;
    }

    /// <summary>解析 "MM-dd"（以閏年 2000 驗 2/29 合法）</summary>
    private static bool TryParseMonthDay(string szMonthDay, out int nMonth, out int nDay)
    {
        nMonth = 0; nDay = 0;
        if (string.IsNullOrWhiteSpace(szMonthDay)) return false;
        var parts = szMonthDay.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out nMonth) || !int.TryParse(parts[1], out nDay)) return false;
        if (nMonth < 1 || nMonth > 12) return false;
        return nDay >= 1 && nDay <= DateTime.DaysInMonth(2000, nMonth);
    }

    private static string ToHHmm(int nMinutes) => $"{nMinutes / 60:00}:{nMinutes % 60:00}";

    private static string DayTypeLabel(string szDayType) => szDayType switch
    {
        "weekday" => "週一至週五",
        "sat" => "週六",
        "sun_offday" => "週日及離峰日",
        _ => szDayType,
    };

    private static string SeasonLabel(string szSeason) =>
        szSeason == "summer" ? "夏月" : "非夏月";

    /// <summary>深拷貝（避免 seed 快取被呼叫端修改污染）</summary>
    private static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, _jsonOptions), _jsonOptions)!;
}
