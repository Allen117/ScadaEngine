using ScadaEngine.Engine.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Widget;

/// <summary>
/// 鎖住 MapDemandResult 的需量顯示狀態對應（plan 2026-09-11 決策 2）：
/// (1) 查無資料 → no_data；(2) Quality=0 → no_data 且不回傳誤導性的 0；
/// (3) 最新一筆距今 >5 分鐘 → stale 且值保留；(4) 正常 → ok + 值。
/// </summary>
public class WidgetCircuitMetricDemandTests
{
    private static readonly DateTime _dtNow = new(2026, 9, 11, 10, 30, 0);

    [Fact]
    public void 查無資料_回no_data()
    {
        var (szStatus, dValue) = WidgetCircuitMetricService.MapDemandResult(null, _dtNow);
        Assert.Equal("no_data", szStatus);
        Assert.Null(dValue);
    }

    [Fact]
    public void 值為null_回no_data()
    {
        var demand = new TodayDemandModel { dCurrentKW = null, dtTimestamp = null, nQuality = 1 };
        var (szStatus, dValue) = WidgetCircuitMetricService.MapDemandResult(demand, _dtNow);
        Assert.Equal("no_data", szStatus);
        Assert.Null(dValue);
    }

    [Fact]
    public void Quality為0_回no_data_不回傳0值()
    {
        // Quality=0 時 Engine 寫入的 DemandKW 固定為 0，顯示 0 會誤導值班人員「需量歸零」
        var demand = new TodayDemandModel { dCurrentKW = 0, dtTimestamp = _dtNow.AddMinutes(-1), nQuality = 0 };
        var (szStatus, dValue) = WidgetCircuitMetricService.MapDemandResult(demand, _dtNow);
        Assert.Equal("no_data", szStatus);
        Assert.Null(dValue);
    }

    [Fact]
    public void 時間戳超過5分鐘_回stale_值保留()
    {
        var demand = new TodayDemandModel { dCurrentKW = 123.456, dtTimestamp = _dtNow.AddMinutes(-6), nQuality = 1 };
        var (szStatus, dValue) = WidgetCircuitMetricService.MapDemandResult(demand, _dtNow);
        Assert.Equal("stale", szStatus);
        Assert.Equal(123.46, dValue);
    }

    [Fact]
    public void 時間戳恰為5分鐘_仍為ok()
    {
        // 門檻為「> 5 分鐘」，剛好 5 分鐘（每分鐘一筆的正常抖動範圍）不算 stale
        var demand = new TodayDemandModel { dCurrentKW = 88.0, dtTimestamp = _dtNow.AddMinutes(-5), nQuality = 1 };
        var (szStatus, _) = WidgetCircuitMetricService.MapDemandResult(demand, _dtNow);
        Assert.Equal("ok", szStatus);
    }

    [Fact]
    public void 正常資料_回ok_值四捨五入兩位()
    {
        var demand = new TodayDemandModel { dCurrentKW = 456.789, dtTimestamp = _dtNow.AddMinutes(-1), nQuality = 1 };
        var (szStatus, dValue) = WidgetCircuitMetricService.MapDemandResult(demand, _dtNow);
        Assert.Equal("ok", szStatus);
        Assert.Equal(456.79, dValue);
    }
}
