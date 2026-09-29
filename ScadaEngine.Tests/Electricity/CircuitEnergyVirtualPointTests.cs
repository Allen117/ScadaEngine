using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Services;
using static ScadaEngine.Engine.Services.CircuitEnergyVirtualPointService;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住 CircuitEnergyVirtualPointService 的純邏輯：
/// ToRealtimePoint（NRGD-/NRGM- SID 組字、名稱後綴、單位 kWh、Quality 忠實透傳）、
/// CalcDeltaWithRollover（MaxKwh 溢位）、SumLeafDeltas（sign 加總、缺值整迴路 Bad）、
/// ExpandLeaves（全樹展開、有效 sign = 路徑乘積、虛擬節點不計）。
/// 對錯 = 警報引擎 / 條件控制 / 歷史查詢讀到的迴路用電數字錯誤或 Bad 誤判。
/// </summary>
public class CircuitEnergyVirtualPointTests
{
    private static readonly DateTime DT_TICK = new(2026, 9, 24, 10, 30, 0);

    // ── ToRealtimePoint ──

    [Fact]
    public void SID組字_每日NRGD_每月NRGM_電度NRGP前綴()
    {
        Assert.Equal("NRGD-12", ToRealtimePoint(12, "A棟總盤", NrgMetric.Daily, 123.4, true, DT_TICK).szSID);
        Assert.Equal("NRGM-12", ToRealtimePoint(12, "A棟總盤", NrgMetric.Monthly, 4567.8, true, DT_TICK).szSID);
        Assert.Equal("NRGP-12", ToRealtimePoint(12, "A棟總盤", NrgMetric.Period, 4321.0, true, DT_TICK).szSID);
    }

    [Fact]
    public void Good_發布值與單位kWh_Timestamp為tick()
    {
        var point = ToRealtimePoint(12, "A棟總盤", NrgMetric.Daily, 123.4, isGood: true, DT_TICK);
        Assert.Equal("Good", point.szQuality);
        Assert.Equal(123.4f, point.fValue, 3);
        Assert.Equal("kWh", point.szUnit);
        Assert.Equal(DT_TICK, point.dtTimestamp);
    }

    [Fact]
    public void Bad_發布值0_不美化()
    {
        var point = ToRealtimePoint(12, "A棟總盤", NrgMetric.Daily, 999.9, isGood: false, DT_TICK);
        Assert.Equal("Bad", point.szQuality);
        Assert.Equal(0f, point.fValue);
    }

    [Fact]
    public void 點名_迴路名加指標後綴()
    {
        Assert.Equal("A棟總盤 每日用電", ToRealtimePoint(12, "A棟總盤", NrgMetric.Daily, 1, true, DT_TICK).szTagName);
        Assert.Equal("A棟總盤 每月用電", ToRealtimePoint(12, "A棟總盤", NrgMetric.Monthly, 1, true, DT_TICK).szTagName);
        Assert.Equal("A棟總盤 本月電度", ToRealtimePoint(12, "A棟總盤", NrgMetric.Period, 1, true, DT_TICK).szTagName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void 無迴路名_退用迴路Id組名(string? szName)
    {
        var point = ToRealtimePoint(7, szName, NrgMetric.Daily, 1, isGood: true, DT_TICK);
        Assert.Equal("迴路7 每日用電", point.szTagName);
    }

    [Fact]
    public void 來源標記_CircuitEnergy與迴路用電群組()
    {
        var point = ToRealtimePoint(12, "A棟總盤", NrgMetric.Daily, 1, true, DT_TICK);
        Assert.Equal("CircuitEnergy", point.szDeviceIP);
        Assert.Equal("迴路用電", point.szCoordinatorName);
        Assert.True(point.IsReadSuccess);
    }

    // ── CalcDeltaWithRollover ──

    [Fact]
    public void 正常差值_end大於等於start()
    {
        Assert.Equal(50.0, CalcDeltaWithRollover(100, 150, 99999));
        Assert.Equal(0.0, CalcDeltaWithRollover(100, 100, 99999));
    }

    [Fact]
    public void 溢位_有MaxKwh_繞圈補差()
    {
        // 9990 → 15（Max 10000）：(10000-9990)+15 = 25
        Assert.Equal(25.0, CalcDeltaWithRollover(9990, 15, 10000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void 倒退_無有效MaxKwh_視為歸零回0(double? dMaxKwh)
    {
        Assert.Equal(0.0, CalcDeltaWithRollover(100, 40, dMaxKwh));
    }

    // ── SumLeafDeltas ──

    [Fact]
    public void 多葉子_套sign加總_含負sign()
    {
        var (dTotal, isGood) = SumLeafDeltas(new[]
        {
            new CircuitLeafDelta(100, 150, null, 1),    // +50
            new CircuitLeafDelta(200, 230, null, 1),    // +30
            new CircuitLeafDelta(10, 15, null, -1)      // -5（逆相）
        });
        Assert.True(isGood);
        Assert.Equal(75.0, dTotal, 6);
    }

    [Fact]
    public void 任一葉子邊界缺值_整迴路Bad值0_寧缺勿假()
    {
        var (dTotal, isGood) = SumLeafDeltas(new[]
        {
            new CircuitLeafDelta(100, 150, null, 1),
            new CircuitLeafDelta(null, 230, null, 1)    // 期初缺值（staleness 過期）
        });
        Assert.False(isGood);
        Assert.Equal(0.0, dTotal);

        (dTotal, isGood) = SumLeafDeltas(new[]
        {
            new CircuitLeafDelta(100, null, null, 1)    // 現值缺值（掉線）
        });
        Assert.False(isGood);
        Assert.Equal(0.0, dTotal);
    }

    [Fact]
    public void 無葉子迴路_Bad值0()
    {
        var (dTotal, isGood) = SumLeafDeltas(Array.Empty<CircuitLeafDelta>());
        Assert.False(isGood);
        Assert.Equal(0.0, dTotal);
    }

    [Fact]
    public void 葉子溢位_計入迴路加總()
    {
        var (dTotal, isGood) = SumLeafDeltas(new[]
        {
            new CircuitLeafDelta(9990, 15, 10000, 1),   // 溢位 +25
            new CircuitLeafDelta(0, 10, null, 1)        // +10
        });
        Assert.True(isGood);
        Assert.Equal(35.0, dTotal, 6);
    }

    // ── ExpandLeaves ──

    private static EnergyCircuitNodeModel Node(int nId, int? nParentId, string? szSid = null, int nSign = 1, double? dMaxKwh = null)
        => new() { nId = nId, nParentId = nParentId, szName = $"C{nId}", szSID = szSid, nSign = nSign, dMaxKwh = dMaxKwh };

    [Fact]
    public void 虛擬根_收齊子樹綁SID節點_有效sign為路徑乘積()
    {
        // 根 1（虛擬）→ 2（SID=A, Sign=+1）、3（虛擬, Sign=-1）→ 4（SID=B, Sign=+1）
        var nodes = new[]
        {
            Node(1, null),
            Node(2, 1, "A", 1),
            Node(3, 1, nSign: -1),
            Node(4, 3, "B", 1)
        };
        var leaves = ExpandLeaves(nodes)[1];
        Assert.Equal(2, leaves.Count);
        Assert.Equal(1, leaves.First(l => l.szSID == "A").nEffectiveSign);
        Assert.Equal(-1, leaves.First(l => l.szSID == "B").nEffectiveSign);   // 路徑經過 Sign=-1 的虛擬節點
    }

    [Fact]
    public void 查詢根自身Sign不計入_與Web遞迴CTE語意一致()
    {
        // 節點 3 自身 Sign=-1：以 3 為根查詢時 anchor EffectiveSign=1，其葉子 4 的有效 sign 仍為 +1
        var nodes = new[]
        {
            Node(3, null, nSign: -1),
            Node(4, 3, "B", 1)
        };
        var leaves = ExpandLeaves(nodes)[3];
        Assert.Equal(1, Assert.Single(leaves).nEffectiveSign);
    }

    [Fact]
    public void 綁SID的中間節點_自身也算葉子()
    {
        // 2 綁 SID 且有子節點 5（也綁 SID）→ 兩者都收
        var nodes = new[]
        {
            Node(2, null, "A"),
            Node(5, 2, "C", -1, 5000)
        };
        var leaves = ExpandLeaves(nodes)[2];
        Assert.Equal(2, leaves.Count);
        var leafC = leaves.First(l => l.szSID == "C");
        Assert.Equal(-1, leafC.nEffectiveSign);
        Assert.Equal(5000, leafC.dMaxKwh);
    }

    [Fact]
    public void 每個節點都是一個迴路key_虛擬節點無SID後裔則空清單()
    {
        var nodes = new[]
        {
            Node(1, null),
            Node(2, 1, "A")
        };
        var map = ExpandLeaves(nodes);
        Assert.Equal(2, map.Count);
        Assert.Single(map[1]);          // 根收到後裔 A
        Assert.Single(map[2]);          // 節點 2 收到自身
        Assert.Equal("A", map[2][0].szSID);
    }
}
