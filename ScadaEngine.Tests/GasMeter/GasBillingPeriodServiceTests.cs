using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.GasMeter;

/// <summary>
/// 鎖住 GasBillingPeriodService 的期別推導 — 特別是**電/水兩套沒有的「刪除此期（IsSkipped）」**，
/// 它是「兩月一期」的實作基礎。這裡算錯 = 用氣/氣費報表的每一期 bucket 都切錯。
///
/// 規則（見 GasBillingPeriodService 類別註解）：
///   ① nominal：等同水費版級聯推導，skipped row 視同不存在
///   ② effective：skipped 期回 null；存在的期別向後吸收同年連續 skipped 期；
///      本期之前同年皆 skipped 時向前吸收至該年 1 月起始
/// 因此「刪除 = 併入前一期」「刪第一期 = 下一期向前吸收」皆由推導自然得出，
/// 刪除/復原只寫或刪單一 row，不動鄰期 → 完全可逆。
/// </summary>
public class GasBillingPeriodServiceTests
{
    // ── 測試資料建構 ──────────────────────────────────────

    private static Dictionary<(int, int), GasBillingPeriodModel> Rows(
        params GasBillingPeriodModel[] rows) =>
        rows.ToDictionary(r => (r.nPeriodYear, r.nPeriodMonth));

    /// <summary>被刪除的期別 row（起訖存刪除當下的名目值，不參與級聯）</summary>
    private static GasBillingPeriodModel Skipped(int nYear, int nMonth) => new()
    {
        nPeriodYear = nYear,
        nPeriodMonth = nMonth,
        dtStartDate = new DateTime(nYear, nMonth, 1),
        dtEndDate = new DateTime(nYear, nMonth, 1).AddMonths(1).AddDays(-1),
        isSkipped = true,
    };

    /// <summary>使用者自訂起訖的期別 row</summary>
    private static GasBillingPeriodModel Custom(int nYear, int nMonth, string szStart, string szEnd) => new()
    {
        nPeriodYear = nYear,
        nPeriodMonth = nMonth,
        dtStartDate = DateTime.Parse(szStart),
        dtEndDate = DateTime.Parse(szEnd),
        isSkipped = false,
    };

    private static Dictionary<(int, int), GasBillingPeriodModel> SkipMonths(int nYear, params int[] months) =>
        months.Select(m => Skipped(nYear, m)).ToDictionary(r => (r.nPeriodYear, r.nPeriodMonth));

    // ── 基本推導（未刪除任何期別時 = 水費版行為） ───────────

    [Fact]
    public void 完全無自訂_每期皆為自然月()
    {
        var rows = Rows();
        for (var m = 1; m <= 12; m++)
        {
            var p = GasBillingPeriodService.ResolveEffective(2026, m, rows);
            Assert.NotNull(p);
            Assert.Equal(new DateTime(2026, m, 1), p!.dtStart);
            Assert.Equal(new DateTime(2026, m, 1).AddMonths(1), p.dtEndExclusive);
            Assert.True(p.isNaturalMonth);
        }
    }

    [Fact]
    public void 自訂一期_後續期別逐期級聯順延()
    {
        // 1 月自訂 1/05~2/04 → 2 月推導 2/05~3/04、3 月推導 3/05~4/04
        var rows = Rows(Custom(2026, 1, "2026-01-05", "2026-02-04"));

        var feb = GasBillingPeriodService.ResolveEffective(2026, 2, rows)!;
        Assert.Equal(new DateTime(2026, 2, 5), feb.dtStart);
        Assert.Equal(new DateTime(2026, 3, 4), feb.dtEndInclusive);

        var mar = GasBillingPeriodService.ResolveEffective(2026, 3, rows)!;
        Assert.Equal(new DateTime(2026, 3, 5), mar.dtStart);
        Assert.Equal(new DateTime(2026, 4, 4), mar.dtEndInclusive);
    }

    // ── 刪除此期（IsSkipped） ───────────────────────────────

    [Fact]
    public void 已刪除的期別_不存在()
    {
        var rows = SkipMonths(2026, 2);
        Assert.Null(GasBillingPeriodService.ResolveEffective(2026, 2, rows));
        Assert.True(GasBillingPeriodService.IsSkipped(2026, 2, rows));
    }

    [Fact]
    public void 刪除2月_1月期自動變成1月1日到2月28日()
    {
        var rows = SkipMonths(2026, 2);

        var jan = GasBillingPeriodService.ResolveEffective(2026, 1, rows)!;
        Assert.Equal(new DateTime(2026, 1, 1), jan.dtStart);
        Assert.Equal(new DateTime(2026, 2, 28), jan.dtEndInclusive);
        Assert.Equal(59, (int)(jan.dtEndExclusive - jan.dtStart).TotalDays);   // 31 + 28
        Assert.False(jan.isNaturalMonth);
        Assert.Equal("2026-01-01~02-28", jan.szLabel);

        // 3 月不受影響
        var mar = GasBillingPeriodService.ResolveEffective(2026, 3, rows)!;
        Assert.Equal(new DateTime(2026, 3, 1), mar.dtStart);
        Assert.Equal(new DateTime(2026, 3, 31), mar.dtEndInclusive);
    }

    [Fact]
    public void 刪除該年第一期_下一期起始往前延伸()
    {
        var rows = SkipMonths(2026, 1);

        Assert.Null(GasBillingPeriodService.ResolveEffective(2026, 1, rows));

        var feb = GasBillingPeriodService.ResolveEffective(2026, 2, rows)!;
        Assert.Equal(new DateTime(2026, 1, 1), feb.dtStart);      // 向前吸收 1 月
        Assert.Equal(new DateTime(2026, 2, 28), feb.dtEndInclusive);
        Assert.Equal(59, (int)(feb.dtEndExclusive - feb.dtStart).TotalDays);
    }

    [Fact]
    public void 復原_移除skipped列後回到原起訖()
    {
        var withSkip = SkipMonths(2026, 2);
        var janMerged = GasBillingPeriodService.ResolveEffective(2026, 1, withSkip)!;
        Assert.Equal(new DateTime(2026, 2, 28), janMerged.dtEndInclusive);

        // 復原 = 刪掉 skipped row（UnskipAsync 的效果），鄰期自動收回被吸收的日數
        var restored = Rows();
        var jan = GasBillingPeriodService.ResolveEffective(2026, 1, restored)!;
        var feb = GasBillingPeriodService.ResolveEffective(2026, 2, restored)!;
        Assert.Equal(new DateTime(2026, 1, 31), jan.dtEndInclusive);
        Assert.Equal(new DateTime(2026, 2, 1), feb.dtStart);
        Assert.Equal(new DateTime(2026, 2, 28), feb.dtEndInclusive);
    }

    [Fact]
    public void 連續刪除兩期_日數一併併入前一期()
    {
        // 刪 2、3 月 → 1 月期 = 1/01~3/31
        var rows = SkipMonths(2026, 2, 3);
        var jan = GasBillingPeriodService.ResolveEffective(2026, 1, rows)!;
        Assert.Equal(new DateTime(2026, 1, 1), jan.dtStart);
        Assert.Equal(new DateTime(2026, 3, 31), jan.dtEndInclusive);
    }

    // ── 兩月一期端到端 ──────────────────────────────────────

    [Theory]
    [InlineData(2026, 365)]
    [InlineData(2028, 366)]   // 閏年
    public void 兩月一期_刪除偶數月後全年恰6期且日數合計正確(int nYear, int nExpectedDays)
    {
        var rows = SkipMonths(nYear, 2, 4, 6, 8, 10, 12);

        var periods = GasBillingPeriodService.BuildPeriodRanges(
            new DateTime(nYear, 1, 1), new DateTime(nYear, 12, 1), rows);

        Assert.Equal(6, periods.Count);

        // 每期恰兩個月，且日數合計 = 全年天數
        var nTotalDays = 0;
        foreach (var p in periods)
        {
            var nDays = (int)(p.dtEndExclusive - p.dtStart).TotalDays;
            nTotalDays += nDays;
            Assert.Equal(p.dtStart.AddMonths(2), p.dtEndExclusive);
        }
        Assert.Equal(nExpectedDays, nTotalDays);

        // 期別起始月份為 1/3/5/7/9/11
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11 }, periods.Select(p => p.nMonth).ToArray());
    }

    [Fact]
    public void 兩月一期_全年每一天恰落在一期內_無空窗無重疊()
    {
        var rows = SkipMonths(2026, 2, 4, 6, 8, 10, 12);
        var periods = GasBillingPeriodService.BuildPeriodRanges(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 1), rows);

        for (var d = new DateTime(2026, 1, 1); d < new DateTime(2027, 1, 1); d = d.AddDays(1))
        {
            var nHit = periods.Count(p => p.dtStart <= d && d < p.dtEndExclusive);
            Assert.True(nHit == 1, $"{d:yyyy-MM-dd} 落在 {nHit} 期（應恰為 1）");
        }
    }

    [Fact]
    public void 兩月一期_月粒度bucket由12個減為6個()
    {
        var full = GasBillingPeriodService.BuildPeriodRanges(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 1), Rows());
        Assert.Equal(12, full.Count);

        var bimonthly = GasBillingPeriodService.BuildPeriodRanges(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 1), SkipMonths(2026, 2, 4, 6, 8, 10, 12));
        Assert.Equal(6, bimonthly.Count);
    }

    // ── 設定頁清單（BuildYear） ─────────────────────────────

    [Fact]
    public void 設定頁清單_只列存在的期別_已刪除另回復原清單()
    {
        var rows = SkipMonths(2026, 2, 4, 6, 8, 10, 12);
        var (periods, skipped) = GasBillingPeriodService.BuildYear(2026, rows);

        Assert.Equal(6, periods.Count);
        Assert.Equal(6, skipped.Count);
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11 }, periods.Select(p => p.period.nMonth).ToArray());
        Assert.Equal(new[] { 2, 4, 6, 8, 10, 12 }, skipped.Select(p => p.nMonth).ToArray());
    }

    [Fact]
    public void 設定頁清單_兩月一期時相鄰期無空窗無重疊()
    {
        var rows = SkipMonths(2026, 2, 4, 6, 8, 10, 12);
        var (periods, _) = GasBillingPeriodService.BuildYear(2026, rows);

        // 第一列（1 月期）與去年 12 月期比較亦應無縫；其餘各列 gap = 0
        Assert.All(periods, p => Assert.Equal(0, p.nGapDays));
    }

    [Fact]
    public void 設定頁清單_空窗與重疊天數正確()
    {
        // 1 月自訂 1/01~1/20 → 2 月推導 1/21~2/20，無空窗；
        // 改成 2 月也自訂 1/25~2/24 → 與 1 月結束隔日（1/21）差 4 天空窗
        var rows = Rows(
            Custom(2026, 1, "2026-01-01", "2026-01-20"),
            Custom(2026, 2, "2026-01-25", "2026-02-24"));
        var (periods, _) = GasBillingPeriodService.BuildYear(2026, rows);

        Assert.Equal(4, periods[1].nGapDays);    // 2 月列：+4 天空窗

        var overlapped = Rows(
            Custom(2026, 1, "2026-01-01", "2026-01-20"),
            Custom(2026, 2, "2026-01-16", "2026-02-15"));
        var (periods2, _) = GasBillingPeriodService.BuildYear(2026, overlapped);
        Assert.Equal(-5, periods2[1].nGapDays);  // 2 月列：重疊 5 天
    }

    // ── 定位「某年月屬於哪一期」 ─────────────────────────────

    [Fact]
    public void 已刪除的年月_回傳吸收它的那一期()
    {
        var rows = SkipMonths(2026, 2);

        var containing = GasBillingPeriodService.ResolveContaining(2026, 2, rows);
        Assert.NotNull(containing);
        Assert.Equal(1, containing!.nMonth);                              // 被 1 月期吸收
        Assert.Equal(new DateTime(2026, 2, 28), containing.dtEndInclusive);
    }

    [Fact]
    public void 已刪除的該年第一期_回傳向前吸收它的下一期()
    {
        var rows = SkipMonths(2026, 1);

        var containing = GasBillingPeriodService.ResolveContaining(2026, 1, rows);
        Assert.NotNull(containing);
        Assert.Equal(2, containing!.nMonth);
        Assert.Equal(new DateTime(2026, 1, 1), containing.dtStart);
    }

    // ── 標籤 ────────────────────────────────────────────────

    [Fact]
    public void 標籤_自然月維持yyyyMM_兩月一期顯示完整區間()
    {
        var natural = GasBillingPeriodService.ResolveEffective(2026, 3, Rows())!;
        Assert.Equal("2026-03", natural.szLabel);

        var merged = GasBillingPeriodService.ResolveEffective(2026, 11, SkipMonths(2026, 12))!;
        Assert.Equal("2026-11-01~12-31", merged.szLabel);
    }
}
