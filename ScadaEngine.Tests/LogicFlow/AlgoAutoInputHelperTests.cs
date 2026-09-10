using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.LogicFlow;

/// <summary>
/// @inputs_auto_repeat 注入邏輯測試：per-port 下游 SID 解析（BFS 只走指定 sourcePort 起始邊）
/// 與 Min/Max 注入的缺漏行為（決策 6：查不到就不注入，不做 fallback）。
/// </summary>
public class AlgoAutoInputHelperTests
{
    // 圖形示意：algo(1) --freq1--> output(2, SID=A)
    //           algo(1) --freq2--> timer(3) --> output(4, SID=B)
    private static readonly List<(int Source, string SourcePort, int Target)> _edges = new()
    {
        (1, "freq1", 2),
        (1, "freq2", 3),
        (3, "out", 4),
    };

    private static (bool, string?) NodeLookup(int id) => id switch
    {
        2 => (true, "SID-A"),
        4 => (true, "SID-B"),
        _ => (false, null),
    };

    [Fact]
    public void FindDownstreamOutputSid_直連output_取得SID()
    {
        var sid = AlgoAutoInputHelper.FindDownstreamOutputSid(_edges, NodeLookup, 1, "freq1");
        Assert.Equal("SID-A", sid);
    }

    [Fact]
    public void FindDownstreamOutputSid_經中繼節點_取得SID()
    {
        var sid = AlgoAutoInputHelper.FindDownstreamOutputSid(_edges, NodeLookup, 1, "freq2");
        Assert.Equal("SID-B", sid);
    }

    [Fact]
    public void FindDownstreamOutputSid_不同port不互相污染()
    {
        // freq1 只能走到 SID-A，不會拿到 freq2 那條線的 SID-B（node-level 版本做不到這點）
        var sid1 = AlgoAutoInputHelper.FindDownstreamOutputSid(_edges, NodeLookup, 1, "freq1");
        var sid2 = AlgoAutoInputHelper.FindDownstreamOutputSid(_edges, NodeLookup, 1, "freq2");
        Assert.NotEqual(sid1, sid2);
    }

    [Fact]
    public void FindDownstreamOutputSid_未接線port_回null()
    {
        var sid = AlgoAutoInputHelper.FindDownstreamOutputSid(_edges, NodeLookup, 1, "freq3");
        Assert.Null(sid);
    }

    [Fact]
    public void FindDownstreamOutputSid_迴圈不會死循環()
    {
        var cyclic = new List<(int, string, int)> { (1, "freq1", 3), (3, "out", 1) };
        var sid = AlgoAutoInputHelper.FindDownstreamOutputSid(cyclic, _ => (false, null), 1, "freq1");
        Assert.Null(sid);
    }

    private static readonly List<AlgoAutoInputHelper.AutoRepeatDef> _defs = new()
    {
        new("freq_min", "freq", "Min"),
        new("freq_max", "freq", "Max"),
    };

    [Fact]
    public void InjectAutoInputs_兩組皆有點位_全數注入()
    {
        var dict = new Dictionary<string, double>();
        AlgoAutoInputHelper.InjectAutoInputs(dict, _defs, 2,
            port => port == "freq1" ? "SID-A" : port == "freq2" ? "SID-B" : null,
            sid => sid == "SID-A" ? (20f, 60f) : (25f, 55f));

        Assert.Equal(20, dict["freq_min1"]);
        Assert.Equal(60, dict["freq_max1"]);
        Assert.Equal(25, dict["freq_min2"]);
        Assert.Equal(55, dict["freq_max2"]);
    }

    [Fact]
    public void InjectAutoInputs_下游無output_該組不注入()
    {
        var dict = new Dictionary<string, double>();
        AlgoAutoInputHelper.InjectAutoInputs(dict, _defs, 2,
            port => port == "freq1" ? "SID-A" : null,   // 第 2 組沒接
            _ => (20f, 60f));

        Assert.Equal(20, dict["freq_min1"]);
        Assert.Equal(60, dict["freq_max1"]);
        Assert.False(dict.ContainsKey("freq_min2"));
        Assert.False(dict.ContainsKey("freq_max2"));
    }

    [Fact]
    public void InjectAutoInputs_點位Min為NULL_只注入Max不做fallback()
    {
        var dict = new Dictionary<string, double>();
        AlgoAutoInputHelper.InjectAutoInputs(dict, _defs, 1,
            _ => "SID-A",
            _ => ((float?)null, 60f));

        Assert.False(dict.ContainsKey("freq_min1"));  // 決策 6：缺就缺，交給 INPUT_MISSING
        Assert.Equal(60, dict["freq_max1"]);
    }

    [Fact]
    public void InjectAutoInputs_點位表查無SID_不注入()
    {
        var dict = new Dictionary<string, double>();
        AlgoAutoInputHelper.InjectAutoInputs(dict, _defs, 1,
            _ => "SID-X",
            _ => null);

        Assert.Empty(dict);
    }

    [Fact]
    public void InjectAutoInputs_未知Field_不注入()
    {
        var dict = new Dictionary<string, double>();
        var badDefs = new List<AlgoAutoInputHelper.AutoRepeatDef> { new("x", "freq", "Ratio") };
        AlgoAutoInputHelper.InjectAutoInputs(dict, badDefs, 1, _ => "SID-A", _ => (20f, 60f));
        Assert.Empty(dict);
    }
}
