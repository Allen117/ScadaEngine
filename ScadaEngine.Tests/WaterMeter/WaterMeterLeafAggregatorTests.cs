using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.WaterMeter;

/// <summary>
/// 鎖住 WaterMeterLeafAggregator 純邏輯：boundary 相減、MaxVolume 溢位歸零、UnitScale L→m³ 換算、
/// 邊界缺值三段語意（兩缺不寫 / 缺一邊 Q=0 / 兩有 Q=1）。
/// 對錯 = 用水報表與水費的每一度水都算錯。與電表 EnergyLeafAggregator 語意對稱。
/// </summary>
public class WaterMeterLeafAggregatorTests
{
    private static readonly DateTime Hour = new(2026, 8, 3, 10, 0, 0);

    // ── CalcDeltaWithRollover：boundary 相減 + 溢位 ──────────────────

    [Theory]
    [InlineData(100.0, 130.5, 30.5)]   // 正常累計
    [InlineData(0.0, 0.0, 0.0)]        // 無用水
    [InlineData(999.9, 999.9, 0.0)]    // 讀數不動
    public void 正常累計_結尾減開頭(double start, double end, double expected)
    {
        var (dDelta, isRolledOver) = WaterMeterLeafAggregator.CalcDeltaWithRollover(start, end, 10000);
        Assert.Equal(expected, dDelta, 10);
        Assert.False(isRolledOver);
    }

    [Fact]
    public void 溢位歸零_有MaxVolume_套環繞公式()
    {
        // 9990 → 15，Max=10000：(10000 - 9990) + 15 = 25
        var (dDelta, isRolledOver) = WaterMeterLeafAggregator.CalcDeltaWithRollover(9990, 15, 10000);
        Assert.Equal(25, dDelta, 10);
        Assert.True(isRolledOver);
    }

    [Theory]
    [InlineData(null)]  // 未設定
    [InlineData(0.0)]   // 設 0 視同未設定
    [InlineData(-5.0)]  // 非法負值視同未設定
    public void 倒退但無有效MaxVolume_delta視為0(double? maxVolume)
    {
        var (dDelta, isRolledOver) = WaterMeterLeafAggregator.CalcDeltaWithRollover(500, 20, maxVolume);
        Assert.Equal(0, dDelta, 10);
        Assert.False(isRolledOver);
    }

    // ── ComputeFromBoundaries：三段語意 + UnitScale ──────────────────

    [Fact]
    public void 兩邊界都缺_回null不寫列()
    {
        var model = WaterMeterLeafAggregator.ComputeFromBoundaries("W1-S1", Hour, null, null, 10000, 1.0);
        Assert.Null(model);
    }

    [Theory]
    [InlineData(true)]   // 缺開頭
    [InlineData(false)]  // 缺結尾
    public void 只缺一邊_掉線transition_Q0Delta0(bool missingStart)
    {
        double? fStart = missingStart ? null : 100.0;
        double? fEnd = missingStart ? 100.0 : null;

        var model = WaterMeterLeafAggregator.ComputeFromBoundaries("W1-S1", Hour, fStart, fEnd, 10000, 1.0);

        Assert.NotNull(model);
        Assert.Equal(0, model!.nQuality);
        Assert.Equal(0, model.dDeltaM3, 10);
        Assert.False(model.isRolledOver);
        Assert.Equal("W1-S1", model.szSID);
        Assert.Equal(Hour, model.dtHourStart);
    }

    [Fact]
    public void 兩邊都有_Q1_正常寫入()
    {
        var model = WaterMeterLeafAggregator.ComputeFromBoundaries("W1-S1", Hour, 120.0, 123.5, 10000, 1.0);

        Assert.NotNull(model);
        Assert.Equal(1, model!.nQuality);
        Assert.Equal(3.5, model.dDeltaM3, 10);
        Assert.False(model.isRolledOver);
    }

    [Fact]
    public void UnitScale_公升點位_換算成立方米()
    {
        // L 系點位 UnitScale=0.001：1500 L → 1.5 m³
        var model = WaterMeterLeafAggregator.ComputeFromBoundaries("W1-S2", Hour, 20000.0, 21500.0, null, 0.001);

        Assert.NotNull(model);
        Assert.Equal(1.5, model!.dDeltaM3, 10);
    }

    [Fact]
    public void UnitScale_溢位先以原始單位計算再換算()
    {
        // L 系點位，Max=100000 L：99000 → 500，delta 原始 = (100000-99000)+500 = 1500 L = 1.5 m³
        var model = WaterMeterLeafAggregator.ComputeFromBoundaries("W1-S2", Hour, 99000.0, 500.0, 100000, 0.001);

        Assert.NotNull(model);
        Assert.Equal(1.5, model!.dDeltaM3, 10);
        Assert.True(model.isRolledOver);
    }
}
