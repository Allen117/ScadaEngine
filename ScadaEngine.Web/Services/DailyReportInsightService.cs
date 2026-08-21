using System.Globalization;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.DailyReport.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 能源日報規則式智慧提示 — 確定性規則，零外部依賴（不串接 LLM API，使用者 2026-08-06 定案）。
/// 規則判定為靜態純函式 EvaluateRules（單元測試對象），文字渲染走 resx（zh-TW / en）。
/// 規則：假日效應 / 天氣差異 / 警報關聯 / 效率異常；差異超門檻卻無規則命中時輸出中性提示。
/// </summary>
public class DailyReportInsightService
{
    /// <summary>天氣規則觸發溫差門檻（°C）</summary>
    public const double TEMP_DIFF_THRESHOLD = 3.0;

    private readonly IStringLocalizer<DailyReportInsightService> _l;

    public DailyReportInsightService(IStringLocalizer<DailyReportInsightService> l)
    {
        _l = l;
    }

    /// <summary>
    /// 產生提示清單（文字以 CurrentUICulture 渲染 — 呼叫端於快照生成時已切至全局語言）。
    /// </summary>
    public List<DailyReportInsight> BuildInsights(DailyReportData data, DailyReportSettingModel setting)
    {
        var hits = EvaluateRules(data, setting);
        var result = new List<DailyReportInsight>(hits.Count);
        foreach (var hit in hits)
        {
            var args = hit.args;
            if (!string.IsNullOrEmpty(hit.szEnergyKey))
            {
                // 能源別顯示名插到 args[0]，模板以 {0} 引用
                var szEnergyName = _l[$"insight.energy.{hit.szEnergyKey}"].Value;
                args = new object[] { szEnergyName }.Concat(hit.args).ToArray();
            }
            result.Add(new DailyReportInsight
            {
                szCategory = hit.szCategory,
                szText = _l[hit.szKey, args].Value,
            });
        }
        return result;
    }

    /// <summary>
    /// 規則判定核心（靜態純函式）。回傳命中清單，順序 = 顯示順序：假日 → 天氣 → 警報 → 效率 → 中性 fallback。
    /// 門檻類（天氣/警報/效率/fallback）僅在對應差異超過 setting.dDiffThresholdPercent 時觸發；
    /// 假日提醒獨立於門檻（情境提醒），IsHolidayHintEnabled=0 時整類跳過。
    /// </summary>
    public static List<DailyReportInsightHit> EvaluateRules(DailyReportData data, DailyReportSettingModel setting)
    {
        var hits = new List<DailyReportInsightHit>();
        var dThreshold = setting.dDiffThresholdPercent;

        // ── 1. 假日效應（情境提醒，不看門檻）──
        if (setting.isHolidayHintEnabled)
        {
            if (data.isReportDateHoliday)
                hits.Add(new DailyReportInsightHit { szCategory = "holiday", szKey = "insight.holiday.report_day" });

            // vs 上週同星期：只在「報告日與上週同日的假日/上班日狀態不同」時提示。
            // 兩天狀態相同（例如連假期間兩天都放假）代表比較基準一致，提示只會變成噪音。
            // 同一個星期幾本來就同為平日或同為週末，故狀態不同必然來自國定假日或補班日。
            var szShift = LastWeekBaselineShift(data);
            if (szShift != null)
                hits.Add(new DailyReportInsightHit { szCategory = "holiday", szKey = $"insight.holiday.lastweek_{szShift}" });

            if (data.isPrevDayHoliday)
                hits.Add(new DailyReportInsightHit { szCategory = "holiday", szKey = "insight.holiday.prevday" });
        }

        // 差異超門檻的能源別（原因提示的觸發前提）
        var beyondPrev = data.dayComparisons
            .Where(r => r.dDiffPrevPct.HasValue && Math.Abs(r.dDiffPrevPct.Value) > dThreshold).ToList();
        var beyondLastWeek = data.dayComparisons
            .Where(r => r.dDiffLastWeekPct.HasValue && Math.Abs(r.dDiffLastWeekPct.Value) > dThreshold).ToList();
        var isAnyBeyond = beyondPrev.Count > 0 || beyondLastWeek.Count > 0;
        var nCauseHitsBefore = hits.Count;

        // ── 2. 天氣差異（外氣日均溫差 > 3°C，且該基準日有能源差異超門檻）──
        if (data.weather?.dAvgTempDay is double dTempDay)
        {
            if (beyondLastWeek.Count > 0 && data.weather.dAvgTempLastWeek is double dTempLastWeek
                && Math.Abs(dTempDay - dTempLastWeek) > TEMP_DIFF_THRESHOLD)
            {
                var dDiff = Math.Round(Math.Abs(dTempDay - dTempLastWeek), 1);
                hits.Add(new DailyReportInsightHit
                {
                    szCategory = "weather",
                    szKey = dTempDay > dTempLastWeek ? "insight.weather.higher_lastweek" : "insight.weather.lower_lastweek",
                    args = new object[] { Fmt(dTempDay), Fmt(dDiff) },
                });
            }
            else if (beyondPrev.Count > 0 && data.weather.dAvgTempPrevDay is double dTempPrev
                && Math.Abs(dTempDay - dTempPrev) > TEMP_DIFF_THRESHOLD)
            {
                var dDiff = Math.Round(Math.Abs(dTempDay - dTempPrev), 1);
                hits.Add(new DailyReportInsightHit
                {
                    szCategory = "weather",
                    szKey = dTempDay > dTempPrev ? "insight.weather.higher_prevday" : "insight.weather.lower_prevday",
                    args = new object[] { Fmt(dTempDay), Fmt(dDiff) },
                });
            }
        }

        // ── 3. 警報關聯（能源別 vs 前日超門檻 且 當日有警報/故障 → 取差異最大的一項）──
        if (data.alarm.nOccurredCount > 0 && beyondPrev.Count > 0)
        {
            var top = beyondPrev.OrderByDescending(r => Math.Abs(r.dDiffPrevPct!.Value)).First();
            hits.Add(new DailyReportInsightHit
            {
                szCategory = "alarm",
                szEnergyKey = top.szEnergy,
                szKey = top.dDiffPrevPct!.Value > 0 ? "insight.alarm.surge" : "insight.alarm.drop",
                args = new object[] { Fmt(Math.Abs(top.dDiffPrevPct.Value)), data.alarm.nOccurredCount },
            });
        }

        // ── 4. 效率異常（kWh/RTh 比值偏離基準日 > 門檻）──
        if (data.efficiency is { dDay: double dEffDay })
        {
            var dEffPrevPct = data.efficiency.dDiffPrevPct;
            var dEffLastWeekPct = data.efficiency.dDiffLastWeekPct;
            // 取偏離較大的基準
            var isUseLastWeek = Math.Abs(dEffLastWeekPct ?? 0) >= Math.Abs(dEffPrevPct ?? 0);
            var dPct = isUseLastWeek ? dEffLastWeekPct : dEffPrevPct;
            if (dPct.HasValue && Math.Abs(dPct.Value) > dThreshold)
            {
                hits.Add(new DailyReportInsightHit
                {
                    szCategory = "efficiency",
                    szKey = (dPct.Value > 0 ? "insight.efficiency.up_" : "insight.efficiency.down_")
                          + (isUseLastWeek ? "lastweek" : "prevday"),
                    args = new object[] { Fmt(dEffDay), Fmt(Math.Abs(dPct.Value)) },
                });
            }
        }

        // ── 5. 中性 fallback（有超門檻差異但無任何原因類提示命中）──
        if (isAnyBeyond && hits.Count == nCauseHitsBefore)
            hits.Add(new DailyReportInsightHit { szCategory = "none", szKey = "insight.none" });

        return hits;
    }

    /// <summary>
    /// 「vs 上週同星期」比較基準是否改變 —— 報告日與上週同日的假日/上班日狀態**不同**時才回傳，
    /// 相同（都放假或都上班）回傳 null。
    ///
    /// 智慧提示、日報 Email、/DailyReport 頁面三處都要這個判定，集中在這裡才不會各寫一份而漂移；
    /// 回傳的是「情境代碼」而非完整 resx key，各處自行接上own 命名空間的前綴。
    /// </summary>
    /// <returns>"offday"（報告日上班、上週同日放假）／"workday"（報告日放假、上週同日上班）／null（不需備註）</returns>
    public static string? LastWeekBaselineShift(DailyReportData data)
    {
        if (data.isReportDateHoliday == data.isLastWeekHoliday) return null;
        return data.isReportDateHoliday ? "workday" : "offday";
    }

    /// <summary>數值一律 invariant 格式化再進模板（避免 culture 小數點差異）；兩位小數足夠涵蓋比值精度</summary>
    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
