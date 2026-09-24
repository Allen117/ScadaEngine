using ScadaEngine.Web.Features.ShiftSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.ShiftSchedule;

/// <summary>
/// 鎖住 ShiftScheduleService 的純邏輯：
/// 驗證（名稱/整點/星期/一週 168 小時不重疊，含跨日與週末 wrap）、
/// 展開器（跨日班歸屬起始日、首日凌晨歸前一日、非班別補桶完整性、標籤前綴規則）、
/// MergeByGroup（多段 flatRange 併回 display bucket）。
/// 這些是電/水/氣三報表「班別」粒度共用的切分核心，錯了三個報表數字一起錯。
/// </summary>
public class ShiftScheduleServiceTests
{
    // 2026-09-21 = 週一、09-22 = 週二、09-20 = 週日（測試用固定日期）
    private static readonly DateTime Mon = new(2026, 9, 21);
    private static readonly DateTime Tue = new(2026, 9, 22);
    private static readonly DateTime Sun = new(2026, 9, 20);

    private static ShiftDefinition Shift(string szName, int nStart, int nEnd, params int[] weekdays) =>
        new() { szName = szName, nStartHour = nStart, nEndHour = nEnd, weekdays = weekdays.ToList() };

    private static ShiftScheduleConfig Config(params ShiftDefinition[] shifts) =>
        new() { shifts = shifts.ToList() };

    // ---------- ValidateCore ----------

    [Fact]
    public void 驗證_名稱空白_擋下()
    {
        var error = ShiftScheduleService.ValidateCore(Config(Shift("  ", 8, 16, 1)));
        Assert.Equal("shift.error.name_required", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_名稱重複_擋下()
    {
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("早班", 8, 16, 1), Shift("早班", 16, 20, 2)));
        Assert.Equal("shift.error.name_duplicate", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_起訖相同_擋下()
    {
        var error = ShiftScheduleService.ValidateCore(Config(Shift("A", 8, 8, 1)));
        Assert.Equal("shift.error.zero_length", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_未勾星期_擋下()
    {
        var error = ShiftScheduleService.ValidateCore(Config(Shift("A", 8, 16)));
        Assert.Equal("shift.error.weekday_required", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_同日時段重疊_擋下()
    {
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("早班", 8, 16, 1), Shift("中班", 12, 20, 1)));
        Assert.Equal("shift.error.overlap", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_跨日班溢入隔日與隔日班重疊_擋下()
    {
        // 週一 22~06 溢入週二 00~06，與週二 04~12 重疊
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("晚班", 22, 6, 1), Shift("早班", 4, 12, 2)));
        Assert.Equal("shift.error.overlap", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_週日晚班wrap週一_與週一早班重疊_擋下()
    {
        // 週日 22~06 wrap 到週一 00~06，與週一 00~08 重疊
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("週日晚班", 22, 6, 0), Shift("週一早班", 0, 8, 1)));
        Assert.Equal("shift.error.overlap", error!.Value.szKey);
    }

    [Fact]
    public void 驗證_三班全覆蓋_合法()
    {
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("早班", 8, 16, 1, 2, 3, 4, 5),
            Shift("中班", 16, 0, 1, 2, 3, 4, 5),
            Shift("晚班", 0, 8, 1, 2, 3, 4, 5)));
        Assert.Null(error);
    }

    [Fact]
    public void 驗證_跨日班無重疊_合法()
    {
        var error = ShiftScheduleService.ValidateCore(Config(
            Shift("日班", 6, 18, 1, 2, 3, 4, 5),
            Shift("夜班", 18, 6, 1, 2, 3, 4, 5)));
        Assert.Null(error);
    }

    // ---------- DurationHours / InstanceOf ----------

    [Fact]
    public void 跨日班_時長與實際起訖正確()
    {
        var s = Shift("晚班", 22, 6, 1);
        Assert.Equal(8, ShiftScheduleService.DurationHours(s));
        var (dtStart, dtEnd) = ShiftScheduleService.InstanceOf(s, Mon);
        Assert.Equal(Mon.AddHours(22), dtStart);
        Assert.Equal(Tue.AddHours(6), dtEnd); // 訖點落隔日
    }

    [Fact]
    public void 訖點為0時_視為當日結束24點()
    {
        var s = Shift("中班", 16, 0, 1);
        Assert.Equal(8, ShiftScheduleService.DurationHours(s));
        var (_, dtEnd) = ShiftScheduleService.InstanceOf(s, Mon);
        Assert.Equal(Tue, dtEnd); // = 隔日 00:00，不再吃隔日時數
    }

    // ---------- BuildBucketPlan ----------

    [Fact]
    public void 展開_單日三班全覆蓋_無非班別_標籤無日期前綴()
    {
        var config = Config(
            Shift("早班", 8, 16, 1), Shift("中班", 16, 0, 1), Shift("晚班", 0, 8, 1));
        var plan = ShiftScheduleService.BuildBucketPlan(config, Mon, Mon, "非班別");

        Assert.Equal(new[] { "早班", "中班", "晚班" }, plan.displayLabels);
        Assert.Equal(3, plan.flatRanges.Count);
        // 全覆蓋 → 各段時長總和 = 24h
        Assert.Equal(24, plan.flatRanges.Sum(r => (r.dtEnd - r.dtStart).TotalHours));
    }

    [Fact]
    public void 展開_單日僅早班_非班別為兩段缺口併一桶()
    {
        var config = Config(Shift("早班", 8, 16, 1));
        var plan = ShiftScheduleService.BuildBucketPlan(config, Mon, Mon, "非班別");

        Assert.Equal(new[] { "早班", "非班別" }, plan.displayLabels);
        // 早班 1 段 + 非班別 2 段（00~08、16~24）
        Assert.Equal(3, plan.flatRanges.Count);
        Assert.Equal(new[] { 0, 1, 1 }, plan.groupOf);
        Assert.Equal((Mon, Mon.AddHours(8)), plan.flatRanges[1]);
        Assert.Equal((Mon.AddHours(16), Tue), plan.flatRanges[2]);
        // 班別 + 非班別時長總和 = 24h（可與日粒度對帳）
        Assert.Equal(24, plan.flatRanges.Sum(r => (r.dtEnd - r.dtStart).TotalHours));
    }

    [Fact]
    public void 展開_跨日班歸屬起始日_隔日凌晨不重複列入非班別()
    {
        // 週一晚班 22~06（僅週一）；查 週一~週二
        var config = Config(Shift("晚班", 22, 6, 1));
        var plan = ShiftScheduleService.BuildBucketPlan(config, Mon, Tue, "非班別");

        Assert.Equal(new[] { "09/21 晚班", "09/21 非班別", "09/22 非班別" }, plan.displayLabels);
        // 週一晚班整段（含溢入週二的 00~06）歸週一
        Assert.Equal((Mon.AddHours(22), Tue.AddHours(6)), plan.displayRanges[0]);
        // 週二非班別從 06:00 起（00~06 已被週一晚班涵蓋，不重複）
        var tueOffRanges = plan.groupOf
            .Select((g, i) => (g, i)).Where(x => x.g == 2)
            .Select(x => plan.flatRanges[x.i]).ToList();
        Assert.Single(tueOffRanges);
        Assert.Equal((Tue.AddHours(6), Tue.AddDays(1)), tueOffRanges[0]);
        // 全部 flatRange 時長總和 = 48h（兩天無縫無重）
        Assert.Equal(48, plan.flatRanges.Sum(r => (r.dtEnd - r.dtStart).TotalHours));
    }

    [Fact]
    public void 展開_首日凌晨被前一日跨日班涵蓋_歸前一日不列入()
    {
        // 週一晚班 22~06；只查週二 → 週二 00~06 歸週一（範圍外），非班別從 06:00 起
        var config = Config(Shift("晚班", 22, 6, 1));
        var plan = ShiftScheduleService.BuildBucketPlan(config, Tue, Tue, "非班別");

        Assert.Equal(new[] { "非班別" }, plan.displayLabels);
        Assert.Single(plan.flatRanges);
        Assert.Equal((Tue.AddHours(6), Tue.AddDays(1)), plan.flatRanges[0]);
    }

    [Fact]
    public void 展開_星期不適用日_整日皆非班別()
    {
        var config = Config(Shift("早班", 8, 16, 1, 2, 3, 4, 5)); // 平日班，查週日
        var plan = ShiftScheduleService.BuildBucketPlan(config, Sun, Sun, "非班別");

        Assert.Equal(new[] { "非班別" }, plan.displayLabels);
        Assert.Equal((Sun, Mon), plan.flatRanges[0]);
    }

    [Fact]
    public void 展開_多日標籤帶日期前綴_同日班別依定義順序()
    {
        var config = Config(Shift("早班", 8, 16, 1, 2), Shift("中班", 16, 0, 1, 2));
        var plan = ShiftScheduleService.BuildBucketPlan(config, Mon, Tue, "非班別");

        Assert.Equal(new[]
        {
            "09/21 早班", "09/21 中班", "09/21 非班別",
            "09/22 早班", "09/22 中班", "09/22 非班別"
        }, plan.displayLabels);
    }

    // ---------- MergeByGroup ----------

    [Fact]
    public void 合併_多段flatRange加總_stale取OR()
    {
        var flatSums = new[] { 10.0, 2.5, 3.5 };
        var flatStale = new[] { false, true, false };
        var groupOf = new List<int> { 0, 1, 1 };

        var (sums, stale) = ShiftScheduleService.MergeByGroup(flatSums, flatStale, groupOf, 2);

        Assert.Equal(new[] { 10.0, 6.0 }, sums);
        Assert.Equal(new[] { false, true }, stale);
    }

    // ---------- ComplementWithin ----------

    [Fact]
    public void 缺口計算_未排序涵蓋段_正確找出缺口()
    {
        var gaps = ShiftScheduleService.ComplementWithin(Mon, Tue, new List<(DateTime, DateTime)>
        {
            (Mon.AddHours(16), Mon.AddHours(20)),
            (Mon.AddHours(4), Mon.AddHours(8)),
        });

        Assert.Equal(3, gaps.Count);
        Assert.Equal((Mon, Mon.AddHours(4)), gaps[0]);
        Assert.Equal((Mon.AddHours(8), Mon.AddHours(16)), gaps[1]);
        Assert.Equal((Mon.AddHours(20), Tue), gaps[2]);
    }
}
