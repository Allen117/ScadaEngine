using ScadaEngine.Web.Features.DailyReport.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.DailyReport;

/// <summary>規則式智慧提示 EvaluateRules — 假日/溫差/警報關聯/效率比值門檻 + IsHolidayHintEnabled 停用 + 中性 fallback</summary>
public class DailyReportInsightRuleTests
{
    private static DailyReportSettingModel Setting(double dThreshold = 15, bool isHolidayHint = true) => new()
    {
        dDiffThresholdPercent = dThreshold,
        isHolidayHintEnabled = isHolidayHint,
    };

    private static DailyReportData Data() => new()
    {
        szReportDate = "2026-08-05",
        alarm = new DailyReportAlarmSummary(),
    };

    private static DailyReportComparisonRow Row(string szEnergy, double? dDiffPrevPct, double? dDiffLastWeekPct) => new()
    {
        szEnergy = szEnergy,
        dDiffPrevPct = dDiffPrevPct,
        dDiffLastWeekPct = dDiffLastWeekPct,
    };

    // ── 假日 ──

    [Fact]
    public void 報告日上班日而上週同星期放假_提示比較基準不同()
    {
        var data = Data();
        data.isLastWeekHoliday = true;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.Single(hits);
        Assert.Equal("insight.holiday.lastweek_offday", hits[0].szKey);
        Assert.Equal("holiday", hits[0].szCategory);
    }

    [Fact]
    public void 報告日放假而上週同星期上班_提示比較基準不同()
    {
        var data = Data();
        data.isReportDateHoliday = true;
        data.isLastWeekHoliday = false;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        // 報告日假日提示 + 上週基準不同提示
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.szKey == "insight.holiday.report_day");
        Assert.Contains(hits, h => h.szKey == "insight.holiday.lastweek_workday");
    }

    [Fact]
    public void 報告日與上週同星期都放假_不提示上週基準不同()
    {
        var data = Data();
        data.isReportDateHoliday = true;
        data.isLastWeekHoliday = true;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        // 連假期間兩天同為假日 → 比較基準一致，只留報告日假日提示
        var single = Assert.Single(hits);
        Assert.Equal("insight.holiday.report_day", single.szKey);
    }

    [Fact]
    public void 報告日與上週同星期都上班_不提示上週基準不同()
    {
        var data = Data();

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szKey.StartsWith("insight.holiday.lastweek"));
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    [InlineData(false, true, "offday")]
    [InlineData(true, false, "workday")]
    public void 上週比較基準判定(bool isReportDateHoliday, bool isLastWeekHoliday, string? szExpected)
    {
        var data = Data();
        data.isReportDateHoliday = isReportDateHoliday;
        data.isLastWeekHoliday = isLastWeekHoliday;

        Assert.Equal(szExpected, DailyReportInsightService.LastWeekBaselineShift(data));
    }

    [Fact]
    public void 假日提示停用_假日類全部不產出()
    {
        var data = Data();
        data.isReportDateHoliday = true;
        data.isPrevDayHoliday = true;
        data.isLastWeekHoliday = true;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting(isHolidayHint: false));

        Assert.DoesNotContain(hits, h => h.szCategory == "holiday");
        Assert.Empty(hits);
    }

    // ── 天氣 ──

    [Fact]
    public void 溫差超過3度_且上週比較超門檻_產出天氣提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 5, 20));  // vs 上週 +20% 超門檻
        data.weather = new DailyReportWeather { dAvgTempDay = 33.2, dAvgTempLastWeek = 29.1 };

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        var weather = Assert.Single(hits, h => h.szCategory == "weather");
        Assert.Equal("insight.weather.higher_lastweek", weather.szKey);
        Assert.Equal("33.2", weather.args[0]);
        Assert.Equal("4.1", weather.args[1]);
    }

    [Fact]
    public void 溫差超過3度_但無能源差異超門檻_不產出天氣提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 5, 8));  // 皆未超門檻
        data.weather = new DailyReportWeather { dAvgTempDay = 33.2, dAvgTempLastWeek = 28.0 };

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szCategory == "weather");
    }

    [Fact]
    public void 溫差未達3度_不產出天氣提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 20, 20));
        data.weather = new DailyReportWeather { dAvgTempDay = 30.0, dAvgTempLastWeek = 28.0, dAvgTempPrevDay = 28.5 };

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szCategory == "weather");
    }

    // ── 警報關聯 ──

    [Fact]
    public void 用電暴增且當日有警報_產出警報關聯提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 18.2, 3));
        data.alarm.nOccurredCount = 3;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        var alarm = Assert.Single(hits, h => h.szCategory == "alarm");
        Assert.Equal("insight.alarm.surge", alarm.szKey);
        Assert.Equal("electricity", alarm.szEnergyKey);
        Assert.Equal("18.2", alarm.args[0]);
        Assert.Equal(3, alarm.args[1]);
    }

    [Fact]
    public void 多能源超門檻_只取差異最大者產出警報關聯()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 16, null));
        data.dayComparisons.Add(Row("water", -30, null));  // 絕對值最大 → 驟減
        data.alarm.nOccurredCount = 1;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        var alarm = Assert.Single(hits, h => h.szCategory == "alarm");
        Assert.Equal("insight.alarm.drop", alarm.szKey);
        Assert.Equal("water", alarm.szEnergyKey);
    }

    [Fact]
    public void 無警報_不產出警報關聯()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 20, null));
        data.alarm.nOccurredCount = 0;

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szCategory == "alarm");
    }

    // ── 效率比值 ──

    [Fact]
    public void 效率比值偏離超門檻_產出效率提示_取偏離較大基準()
    {
        var data = Data();
        data.efficiency = new DailyReportEfficiency
        {
            dDay = 1.32,
            dPrevDay = 1.2,
            dLastWeek = 1.0,
            dDiffPrevPct = 10,
            dDiffLastWeekPct = 32,  // 較大 → 用上週
        };

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        var eff = Assert.Single(hits, h => h.szCategory == "efficiency");
        Assert.Equal("insight.efficiency.up_lastweek", eff.szKey);
        Assert.Equal("1.32", eff.args[0]);
        Assert.Equal("32", eff.args[1]);
    }

    [Fact]
    public void 效率比值偏離未超門檻_不產出效率提示()
    {
        var data = Data();
        data.efficiency = new DailyReportEfficiency { dDay = 1.1, dPrevDay = 1.0, dDiffPrevPct = 10, dDiffLastWeekPct = null };

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szCategory == "efficiency");
    }

    // ── 中性 fallback ──

    [Fact]
    public void 差異超門檻但無原因規則命中_產出中性提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("gas", 25, null));  // 超門檻；無警報、無天氣、無效率資料

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        var single = Assert.Single(hits);
        Assert.Equal("insight.none", single.szKey);
    }

    [Fact]
    public void 差異超門檻且有原因命中_不產出中性提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 25, null));
        data.alarm.nOccurredCount = 2;  // 警報關聯會命中

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.DoesNotContain(hits, h => h.szCategory == "none");
    }

    [Fact]
    public void 假日提示不算原因命中_仍產出中性提示()
    {
        var data = Data();
        data.isLastWeekHoliday = true;          // 情境提醒
        data.dayComparisons.Add(Row("gas", 25, null));  // 超門檻但無原因線索

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.Contains(hits, h => h.szCategory == "holiday");
        Assert.Contains(hits, h => h.szCategory == "none");
    }

    [Fact]
    public void 全部平穩_無任何提示()
    {
        var data = Data();
        data.dayComparisons.Add(Row("electricity", 3, -2));
        data.weather = new DailyReportWeather { dAvgTempDay = 30, dAvgTempLastWeek = 25 };  // 溫差大但能耗無異常

        var hits = DailyReportInsightService.EvaluateRules(data, Setting());

        Assert.Empty(hits);
    }
}
