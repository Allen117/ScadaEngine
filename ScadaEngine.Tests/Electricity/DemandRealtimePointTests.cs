using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住 DemandCalculatorService.ToRealtimePoint：需量計算結果 → 虛擬點位 RealtimeDataModel 映射。
/// 對錯 = LogicFlow / 條件控制 / 警報 / 歷史查詢讀到的需量點位資料錯誤。
/// 重點：DMD- SID 組字、Quality 忠實透傳（Bad 不得美化為 Good）、單位 kW、Timestamp=dtTick。
/// </summary>
public class DemandRealtimePointTests
{
    private static DemandDataModel MakeResult(byte nQuality, double dKw = 123.45)
    {
        return new DemandDataModel
        {
            szSID         = "196865-S1",
            dtTimestamp   = new DateTime(2026, 9, 24, 10, 30, 0),
            dDemandKW     = nQuality == 1 ? dKw : 0,
            dtWindowStart = new DateTime(2026, 9, 24, 10, 15, 0),
            nSampleCount  = nQuality == 1 ? 15 : 1,
            nQuality      = nQuality
        };
    }

    [Fact]
    public void SID組字_加DMD前綴()
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(1), "全廠總表");
        Assert.Equal("DMD-196865-S1", point.szSID);
    }

    [Fact]
    public void Quality1_發布Good_值為需量KW()
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(1, 123.45), "全廠總表");
        Assert.Equal("Good", point.szQuality);
        Assert.Equal(123.45f, point.fValue, 3);
        Assert.Equal("kW", point.szUnit);
        Assert.Equal(new DateTime(2026, 9, 24, 10, 30, 0), point.dtTimestamp);
    }

    [Fact]
    public void Quality0_發布Bad_值為0_不美化()
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(0), "全廠總表");
        Assert.Equal("Bad", point.szQuality);
        Assert.Equal(0f, point.fValue);
    }

    [Fact]
    public void 點名_電表名加需量後綴()
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(1), "全廠總表");
        Assert.Equal("全廠總表 需量", point.szTagName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void 無電表名_退用kWhSID組名(string? szMeterName)
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(1), szMeterName);
        Assert.Equal("196865-S1 需量", point.szTagName);
    }

    [Fact]
    public void 來源標記_Demand與需量群組()
    {
        var point = DemandCalculatorService.ToRealtimePoint(MakeResult(1), "全廠總表");
        Assert.Equal("Demand", point.szDeviceIP);
        Assert.Equal("需量", point.szCoordinatorName);
        Assert.True(point.IsReadSuccess);
    }
}
