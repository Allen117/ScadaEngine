using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住 Common BillingPeriodResolver（電費期別推導核心，自 Web BillingPeriodService 抽出）。
/// 對錯 = Web 全部月粒度報表期界 + Engine NRGP- 本月電度虛擬點位期界同時錯。
/// 規則：有自訂 row 直接採用；無 row 從最近自訂 row 級聯（起始 = 前期結束 +1 天）；
/// 完全無自訂 = 自然月；當前期別重疊取後起、空窗退回當月期別。
/// </summary>
public class BillingPeriodResolverTests
{
    private static Dictionary<(int, int), BillingPeriodModel> Rows(
        params (int nYear, int nMonth, string szStart, string szEnd)[] rows)
        => rows.ToDictionary(
            r => (r.nYear, r.nMonth),
            r => new BillingPeriodModel
            {
                nPeriodYear = r.nYear,
                nPeriodMonth = r.nMonth,
                dtStartDate = DateTime.Parse(r.szStart),
                dtEndDate = DateTime.Parse(r.szEnd)
            });

    [Fact]
    public void 完全無自訂_退自然月()
    {
        var p = BillingPeriodResolver.ResolvePeriod(2026, 9, Rows());
        Assert.Equal(new DateTime(2026, 9, 1), p.dtStart);
        Assert.Equal(new DateTime(2026, 10, 1), p.dtEndExclusive);
        Assert.False(p.isCustomized);
        Assert.True(p.isNaturalMonth);
    }

    [Fact]
    public void 有自訂row_直接採用()
    {
        var rows = Rows((2026, 9, "2026-08-15", "2026-09-14"));
        var p = BillingPeriodResolver.ResolvePeriod(2026, 9, rows);
        Assert.Equal(new DateTime(2026, 8, 15), p.dtStart);
        Assert.Equal(new DateTime(2026, 9, 15), p.dtEndExclusive);   // 結束日含當天 → 訖 = +1 天
        Assert.True(p.isCustomized);
    }

    [Fact]
    public void 無row_從最近自訂row逐期級聯()
    {
        // 7 月自訂結束於 8/14 → 8 月推導 = 8/15 ~ 9/14、9 月推導 = 9/15 ~ 10/14
        var rows = Rows((2026, 7, "2026-07-15", "2026-08-14"));
        var p8 = BillingPeriodResolver.ResolvePeriod(2026, 8, rows);
        Assert.Equal(new DateTime(2026, 8, 15), p8.dtStart);
        Assert.Equal(new DateTime(2026, 9, 15), p8.dtEndExclusive);
        Assert.False(p8.isCustomized);

        var p9 = BillingPeriodResolver.ResolvePeriod(2026, 9, rows);
        Assert.Equal(new DateTime(2026, 9, 15), p9.dtStart);
        Assert.Equal(new DateTime(2026, 10, 15), p9.dtEndExclusive);
    }

    [Fact]
    public void 當前期別_今天落在期內取該期()
    {
        var rows = Rows((2026, 9, "2026-08-20", "2026-09-19"));
        var p = BillingPeriodResolver.ResolveCurrentPeriod(new DateTime(2026, 9, 10), rows);
        Assert.Equal(2026, p.nYear);
        Assert.Equal(9, p.nMonth);
        Assert.Equal(new DateTime(2026, 8, 20), p.dtStart);
    }

    [Fact]
    public void 當前期別_重疊時取起始最晚的期()
    {
        // 8 月期 8/1~9/10 與 9 月期 9/1~9/30 重疊，9/5 應落 9 月期（後開始者）
        var rows = Rows(
            (2026, 8, "2026-08-01", "2026-09-10"),
            (2026, 9, "2026-09-01", "2026-09-30"));
        var p = BillingPeriodResolver.ResolveCurrentPeriod(new DateTime(2026, 9, 5), rows);
        Assert.Equal(9, p.nMonth);
        Assert.Equal(new DateTime(2026, 9, 1), p.dtStart);
    }

    [Fact]
    public void 當前期別_空窗落點退回當月期別()
    {
        // 9 月期只到 9/10、10 月期 9/21 起 → 9/15 無期涵蓋，退回 9 月期
        var rows = Rows(
            (2026, 9, "2026-08-11", "2026-09-10"),
            (2026, 10, "2026-09-21", "2026-10-20"));
        var p = BillingPeriodResolver.ResolveCurrentPeriod(new DateTime(2026, 9, 15), rows);
        Assert.Equal(9, p.nMonth);
    }

    [Fact]
    public void 標籤_自然月yyyyMM_非自然月起訖()
    {
        Assert.Equal("2026-09", BillingPeriodResolver.ResolvePeriod(2026, 9, Rows()).szLabel);
        var rows = Rows((2026, 9, "2026-08-15", "2026-09-14"));
        Assert.Equal("2026-08-15~09-14", BillingPeriodResolver.ResolvePeriod(2026, 9, rows).szLabel);
    }
}
