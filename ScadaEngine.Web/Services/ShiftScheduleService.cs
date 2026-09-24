using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Localization;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.ShiftSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 班別設定 — 讀寫 SystemSettings.shift_schedule（整份 JSON）+ 驗證 + 日期範圍展開器。
/// 展開器把「日期範圍 × 班別定義」轉成三個用量報表（電/水/氣）共用的 ShiftBucketPlan：
/// 每日依定義順序展開各適用班次（跨日班歸屬起始日），未被涵蓋時段補「非班別」bucket
/// （可能多段不連續 → 多 flatRange 對映單一 display bucket，計算後以 MergeByGroup 合併）。
/// 班界限整點：水/氣報表資料源 *MeterLeafHourly 僅整點粒度（見 plan 決策 1）。
/// </summary>
public class ShiftScheduleService
{
    private const string SettingKey = "shift_schedule";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly ILogger<ShiftScheduleService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly IStringLocalizer<ShiftScheduleService> _l;
    private string _szConnectionString = string.Empty;

    public ShiftScheduleService(
        ILogger<ShiftScheduleService> logger,
        DatabaseConfigService configService,
        IStringLocalizer<ShiftScheduleService> localizer)
    {
        _logger = logger;
        _configService = configService;
        _l = localizer;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ---------- 讀寫 ----------

    /// <summary>取得整份班別設定 — DB 無值/損毀回空設定（無 seed，班表由使用者自建）</summary>
    public async Task<ShiftScheduleConfig> GetConfigAsync()
    {
        string? szJson;
        using (var conn = await GetConnectionAsync())
        {
            szJson = await conn.QueryFirstOrDefaultAsync<string?>(
                "SELECT SettingValue FROM SystemSettings WHERE SettingKey = @SettingKey",
                new { SettingKey });
        }

        if (string.IsNullOrWhiteSpace(szJson))
            return new ShiftScheduleConfig();

        try
        {
            return JsonSerializer.Deserialize<ShiftScheduleConfig>(szJson, _jsonOptions) ?? new ShiftScheduleConfig();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SystemSettings.{Key} JSON 解析失敗，改用空班別設定", SettingKey);
            return new ShiftScheduleConfig();
        }
    }

    /// <summary>儲存整份班別設定 — 驗證失敗丟 ArgumentException（訊息已本地化）。空清單允許（= 清空班表）。</summary>
    public async Task SaveConfigAsync(ShiftScheduleConfig config)
    {
        config.shifts ??= [];
        var error = ValidateCore(config);
        if (error != null)
            throw new ArgumentException(_l[error.Value.szKey, error.Value.args].Value);

        var szJson = JsonSerializer.Serialize(config, _jsonOptions);
        const string szSql = @"
            IF EXISTS (SELECT 1 FROM SystemSettings WHERE SettingKey = @SettingKey)
                UPDATE SystemSettings SET SettingValue = @szJson, UpdatedAt = GETDATE() WHERE SettingKey = @SettingKey;
            ELSE
                INSERT INTO SystemSettings (SettingKey, SettingValue, UpdatedAt) VALUES (@SettingKey, @szJson, GETDATE());";
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(szSql, new { SettingKey, szJson });
        _logger.LogInformation("班別設定已儲存（{Count} 個班別）", config.shifts.Count);
    }

    // ---------- 報表用展開 ----------

    /// <summary>
    /// 依現行班別設定展開日期範圍為 bucket 計畫（起訖皆為含日，時間部分忽略）。
    /// 未設定任何班別 → InvalidOperationException（訊息已本地化，報表端直接顯示給使用者）。
    /// </summary>
    public async Task<ShiftBucketPlan> BuildBucketPlanAsync(DateTime dtStartDate, DateTime dtEndDate)
    {
        var config = await GetConfigAsync();
        if (config.shifts.Count == 0)
            throw new InvalidOperationException(_l["shift.error.not_configured"].Value);
        return BuildBucketPlan(config, dtStartDate, dtEndDate, _l["shift.offshift_label"].Value);
    }

    // ---------- static 純邏輯（單元測試用） ----------

    /// <summary>
    /// 驗證整份設定；合法回 null，否則回 (resx key, 格式化參數)。
    /// 規則：名稱必填且唯一、時數 0~23、起訖不可相同（跨日以訖 &lt;= 起表達，0 = 隔日 00:00）、
    /// 適用星期必填且值域 0~6、同一週時間軸（168 小時）上任兩班次不可重疊（避免用量重複計算）。
    /// </summary>
    public static (string szKey, object[] args)? ValidateCore(ShiftScheduleConfig config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in config.shifts)
        {
            s.szName = (s.szName ?? string.Empty).Trim();
            if (s.szName.Length == 0)
                return ("shift.error.name_required", []);
            if (!names.Add(s.szName))
                return ("shift.error.name_duplicate", [s.szName]);
            if (s.nStartHour is < 0 or > 23 || s.nEndHour is < 0 or > 23)
                return ("shift.error.hour_range", [s.szName]);
            if (s.nStartHour == s.nEndHour)
                return ("shift.error.zero_length", [s.szName]);
            if (s.weekdays == null || s.weekdays.Count == 0)
                return ("shift.error.weekday_required", [s.szName]);
            if (s.weekdays.Any(w => w is < 0 or > 6) || s.weekdays.Distinct().Count() != s.weekdays.Count)
                return ("shift.error.weekday_invalid", [s.szName]);
        }

        // 一週 168 小時佔用表 — 跨日班溢入隔日、週日晚班溢入週一（wrap）皆在此攤平檢查
        var occupiedBy = new string?[168];
        foreach (var s in config.shifts)
        {
            var nDuration = DurationHours(s);
            foreach (var w in s.weekdays)
            {
                for (var k = 0; k < nDuration; k++)
                {
                    var nSlot = (w * 24 + s.nStartHour + k) % 168;
                    if (occupiedBy[nSlot] != null && occupiedBy[nSlot] != s.szName)
                        return ("shift.error.overlap", [occupiedBy[nSlot]!, s.szName]);
                    occupiedBy[nSlot] = s.szName;
                }
            }
        }
        return null;
    }

    /// <summary>班次時長（小時）：訖 &lt;= 起視為跨日</summary>
    public static int DurationHours(ShiftDefinition s) => ((s.nEndHour - s.nStartHour) + 24) % 24;

    /// <summary>班次在指定日期的實際 [起, 訖) 時刻（跨日班訖點落隔日）</summary>
    public static (DateTime dtStart, DateTime dtEnd) InstanceOf(ShiftDefinition s, DateTime dtDay)
    {
        var dtStart = dtDay.Date.AddHours(s.nStartHour);
        return (dtStart, dtStart.AddHours(DurationHours(s)));
    }

    /// <summary>
    /// 展開日期範圍為 bucket 計畫。
    /// 每個日期：依定義順序列出當日適用班次（各一 display bucket，跨日班整段歸該日），
    /// 再把該曆日 [00:00, 24:00) 未被任何班次（含前一日跨日班溢入段）涵蓋的缺口合併為一個「非班別」bucket。
    /// 因此 首日凌晨被「前一日跨日班」涵蓋的時段不列入本範圍（歸屬前一日）、
    /// 末日跨日班的隔日凌晨段會整段計入（歸屬末日）— 班別歸屬優先於曆日切齊。
    /// 標籤：單日查詢不帶日期前綴；多日帶 MM/dd（跨年 yyyy-MM-dd）。
    /// </summary>
    public static ShiftBucketPlan BuildBucketPlan(
        ShiftScheduleConfig config, DateTime dtStartDate, DateTime dtEndDate, string szOffShiftLabel)
    {
        var plan = new ShiftBucketPlan();
        var dtS = dtStartDate.Date;
        var dtE = dtEndDate.Date;
        if (dtE < dtS) dtE = dtS;

        var bMultiDay = dtS != dtE;
        var szDateFmt = dtS.Year != dtE.Year ? "yyyy-MM-dd" : "MM/dd";

        for (var d = dtS; d <= dtE; d = d.AddDays(1))
        {
            var szPrefix = bMultiDay ? d.ToString(szDateFmt, CultureInfo.InvariantCulture) + " " : string.Empty;

            // 當日各適用班次（定義順序）
            foreach (var s in config.shifts)
            {
                if (!AppliesOn(s, d)) continue;
                var (dtInstStart, dtInstEnd) = InstanceOf(s, d);
                var nGroup = plan.displayLabels.Count;
                plan.displayLabels.Add(szPrefix + s.szName);
                plan.displayRanges.Add((dtInstStart, dtInstEnd));
                plan.flatRanges.Add((dtInstStart, dtInstEnd));
                plan.groupOf.Add(nGroup);
            }

            // 非班別：曆日 [d, d+1) 減去（前一日 + 當日）班次的涵蓋段
            var dtDayEnd = d.AddDays(1);
            var cover = new List<(DateTime dtStart, DateTime dtEnd)>();
            foreach (var dtDay in new[] { d.AddDays(-1), d })
            {
                foreach (var s in config.shifts)
                {
                    if (!AppliesOn(s, dtDay)) continue;
                    var (dtA, dtB) = InstanceOf(s, dtDay);
                    var dtLo = dtA < d ? d : dtA;
                    var dtHi = dtB > dtDayEnd ? dtDayEnd : dtB;
                    if (dtLo < dtHi) cover.Add((dtLo, dtHi));
                }
            }
            var gaps = ComplementWithin(d, dtDayEnd, cover);
            if (gaps.Count > 0)
            {
                var nGroup = plan.displayLabels.Count;
                plan.displayLabels.Add(szPrefix + szOffShiftLabel);
                plan.displayRanges.Add((gaps[0].dtStart, gaps[^1].dtEnd));
                foreach (var gap in gaps)
                {
                    plan.flatRanges.Add(gap);
                    plan.groupOf.Add(nGroup);
                }
            }
        }
        return plan;
    }

    /// <summary>班次是否適用於指定日期（依星期）</summary>
    public static bool AppliesOn(ShiftDefinition s, DateTime dtDay) =>
        s.weekdays.Contains((int)dtDay.DayOfWeek);

    /// <summary>[dtLo, dtHi) 內未被 cover 涵蓋的缺口（cover 可未排序，會先合併）</summary>
    public static List<(DateTime dtStart, DateTime dtEnd)> ComplementWithin(
        DateTime dtLo, DateTime dtHi, List<(DateTime dtStart, DateTime dtEnd)> cover)
    {
        var gaps = new List<(DateTime, DateTime)>();
        var dtCursor = dtLo;
        foreach (var (dtA, dtB) in cover.OrderBy(c => c.dtStart))
        {
            if (dtA > dtCursor) gaps.Add((dtCursor, dtA < dtHi ? dtA : dtHi));
            if (dtB > dtCursor) dtCursor = dtB;
            if (dtCursor >= dtHi) break;
        }
        if (dtCursor < dtHi) gaps.Add((dtCursor, dtHi));
        return gaps;
    }

    /// <summary>把逐 flatRange 的計算結果依 groupOf 合併為 display bucket 序列（stale 取 OR）</summary>
    public static (double[] sums, bool[] staleFlags) MergeByGroup(
        double[] flatSums, bool[] flatStale, IReadOnlyList<int> groupOf, int nGroups)
    {
        var sums = new double[nGroups];
        var stale = new bool[nGroups];
        for (var i = 0; i < groupOf.Count; i++)
        {
            sums[groupOf[i]] += flatSums[i];
            stale[groupOf[i]] |= flatStale[i];
        }
        return (sums, stale);
    }
}
