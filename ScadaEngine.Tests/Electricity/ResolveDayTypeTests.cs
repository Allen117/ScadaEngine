using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住 ElectricityCostService.ResolveDayType 的日別判定規則：
/// 假日優先於星期 → 週六=sat → 週日=sun_offday → 其餘=weekday。
/// 這條規則算錯會導致 TOU 尖離峰套錯、客戶帳單金額錯，屬「該補測試」的核心邏輯。
/// </summary>
public class ResolveDayTypeTests
{
    [Fact]
    public void 國定假日_即使是平日也判為假日型()
    {
        // Arrange：2026-02-17（週二，假設為春節）
        var dtDay = new DateTime(2026, 2, 17);
        var holidays = new HashSet<DateTime> { new(2026, 2, 17) };

        // Act
        var szResult = ElectricityCostService.ResolveDayType(dtDay, holidays);

        // Assert
        Assert.Equal("sun_offday", szResult);
    }

    [Fact]
    public void 週六_無假日_判為sat()
    {
        var dtSaturday = new DateTime(2026, 8, 1); // 週六
        Assert.Equal("sat", ElectricityCostService.ResolveDayType(dtSaturday, new HashSet<DateTime>()));
    }

    [Fact]
    public void 週日_無假日_判為sun_offday()
    {
        var dtSunday = new DateTime(2026, 8, 2); // 週日
        Assert.Equal("sun_offday", ElectricityCostService.ResolveDayType(dtSunday, new HashSet<DateTime>()));
    }

    [Theory]
    [InlineData("2026-08-03")] // 週一
    [InlineData("2026-08-05")] // 週三
    [InlineData("2026-08-07")] // 週五
    public void 平日_無假日_判為weekday(string szDate)
    {
        var dtDay = DateTime.Parse(szDate);
        Assert.Equal("weekday", ElectricityCostService.ResolveDayType(dtDay, new HashSet<DateTime>()));
    }

    [Fact]
    public void 假日判定只看日期不看時間_帶時分秒也命中()
    {
        var dtWithTime = new DateTime(2026, 2, 17, 13, 45, 0);
        var holidays = new HashSet<DateTime> { new(2026, 2, 17) };
        Assert.Equal("sun_offday", ElectricityCostService.ResolveDayType(dtWithTime, holidays));
    }
}
