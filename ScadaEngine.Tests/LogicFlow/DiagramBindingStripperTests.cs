using System.Text.Json;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.LogicFlow;

/// <summary>
/// LogicFlowDiagramBindingStripper 測試 —— 鎖住「綁定必被清除 / 參數必被保留」。
///
/// 本檔的預期清單即為書面真相：新增節點綁定欄位時，這裡沒同步 = 測試不會提醒，
/// 而漏清一個 output 節點的 sid，就等於複製出的邏輯一啟用就寫進**別台設備**的
/// Modbus 暫存器（本功能最高風險點）。
/// </summary>
public class DiagramBindingStripperTests
{
    /// <summary>複製時必須被移除的節點欄位（與 Stripper.BindingKeys 對照）</summary>
    private static readonly string[] _mustStrip =
    {
        "sid", "pointName", "unit", "scheduleId", "scheduleName",
        "histEnabled", "histOffsetMinutes", "fMin", "fMax",
    };

    /// <summary>複製時必須原樣保留的節點欄位</summary>
    private static readonly string[] _mustKeep =
    {
        "id", "type", "x", "y", "operator", "constValue",
        "timerDelay", "timerHold", "presetValue", "cuMinIntervalMs",
        "algoInputs", "inputCount", "height",
    };

    // 一張塞滿各種節點的流程圖：綁定欄位與參數欄位刻意混在同一顆節點上
    private const string FullDiagram = """
    {
      "nodes": [
        { "id": 1, "type": "input",  "x": 20, "y": 40,
          "sid": "M1-D1-P1", "pointName": "冰機1 出水溫", "unit": "°C",
          "histEnabled": true, "histOffsetMinutes": 60, "fMin": 0, "fMax": 100 },
        { "id": 2, "type": "output", "x": 500, "y": 40,
          "sid": "M1-D1-P9", "pointName": "冰機1 啟停", "fMin": 0, "fMax": 1 },
        { "id": 3, "type": "contact_no", "x": 20, "y": 200,
          "scheduleId": 7, "scheduleName": "平日上班時段" },
        { "id": 4, "type": "constant", "x": 20, "y": 320, "constValue": 12.5 },
        { "id": 5, "type": "timer", "x": 200, "y": 320, "operator": "tp",
          "timerDelay": 30, "timerHold": 5 },
        { "id": 6, "type": "counter", "x": 200, "y": 420,
          "presetValue": 8, "cuMinIntervalMs": 60000 },
        { "id": 7, "type": "algorithm", "x": 340, "y": 200, "operator": "cop_calc",
          "algoInputs": ["kw", "rt"], "inputCount": 3, "height": 150,
          "sid": "M1-D1-P5", "unit": "kW", "fMin": 0, "fMax": 999 }
      ],
      "edges": [
        { "id": 1, "source": 1, "sourcePort": "out", "target": 7, "targetPort": "kw" },
        { "id": 2, "source": 7, "sourcePort": "out", "target": 2, "targetPort": "in" },
        { "id": 3, "source": 3, "sourcePort": "out", "target": 5, "targetPort": "in" }
      ]
    }
    """;

    private static JsonElement StripAndParse(string json)
        => JsonDocument.Parse(LogicFlowDiagramBindingStripper.Strip(json)).RootElement;

    /// <summary>重新序列化成緊湊格式再比對 —— GetRawText() 會連原始 JSON 的空白一起帶出來，
    /// 來源含 <c>["kw", "rt"]</c>、Stripper 輸出 <c>["kw","rt"]</c> 會假性失敗</summary>
    private static string Compact(JsonElement e) => JsonSerializer.Serialize(e);

    [Fact]
    public void Strip_output節點的sid_必被清除()
    {
        var root = StripAndParse(FullDiagram);
        var output = root.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "output");

        Assert.False(output.TryGetProperty("sid", out _));
        Assert.False(output.TryGetProperty("pointName", out _));
    }

    [Fact]
    public void Strip_所有綁定欄位_全數移除()
    {
        var root = StripAndParse(FullDiagram);
        foreach (var node in root.GetProperty("nodes").EnumerateArray())
        {
            foreach (var szKey in _mustStrip)
            {
                Assert.False(node.TryGetProperty(szKey, out _),
                    $"節點 {node.GetProperty("id").GetInt32()}（{node.GetProperty("type").GetString()}）殘留綁定欄位 {szKey}");
            }
        }
    }

    [Fact]
    public void Strip_清單與Stripper公開常數一致()
    {
        // 兩份清單分歧 = 有人改了 Stripper 卻沒回來改測試（或反之）
        Assert.Equal(
            _mustStrip.OrderBy(s => s, StringComparer.Ordinal),
            LogicFlowDiagramBindingStripper.BindingKeys.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void Strip_參數欄位_全數保留()
    {
        var root = StripAndParse(FullDiagram);
        var byId = root.GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("id").GetInt32());

        // 逐顆節點確認它原本有的參數欄位仍在（以原始 JSON 為基準比對）
        var origById = JsonDocument.Parse(FullDiagram).RootElement
            .GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("id").GetInt32());

        foreach (var (nId, orig) in origById)
        {
            foreach (var szKey in _mustKeep)
            {
                if (!orig.TryGetProperty(szKey, out var origVal)) continue;
                Assert.True(byId[nId].TryGetProperty(szKey, out var newVal), $"節點 {nId} 遺失參數 {szKey}");
                Assert.Equal(Compact(origVal), Compact(newVal));
            }
        }

        // 具體鎖住幾個最容易被誤清的
        Assert.Equal(12.5, byId[4].GetProperty("constValue").GetDouble());
        Assert.Equal(30, byId[5].GetProperty("timerDelay").GetInt32());
        Assert.Equal(5, byId[5].GetProperty("timerHold").GetInt32());
        Assert.Equal(8, byId[6].GetProperty("presetValue").GetInt32());
        Assert.Equal(60000, byId[6].GetProperty("cuMinIntervalMs").GetInt32());
        Assert.Equal("cop_calc", byId[7].GetProperty("operator").GetString());
        Assert.Equal(3, byId[7].GetProperty("inputCount").GetInt32());
        Assert.Equal(150, byId[7].GetProperty("height").GetInt32());
        Assert.Equal(new[] { "kw", "rt" },
            byId[7].GetProperty("algoInputs").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Strip_edges完全不變()
    {
        var orig = JsonDocument.Parse(FullDiagram).RootElement.GetProperty("edges");
        var root = StripAndParse(FullDiagram);
        var edges = root.GetProperty("edges");

        Assert.Equal(orig.GetArrayLength(), edges.GetArrayLength());
        for (var i = 0; i < orig.GetArrayLength(); i++)
        {
            foreach (var szKey in new[] { "id", "source", "sourcePort", "target", "targetPort" })
            {
                Assert.Equal(Compact(orig[i].GetProperty(szKey)),
                             Compact(edges[i].GetProperty(szKey)));
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ 壞掉的 json")]
    [InlineData("[1,2,3]")]          // 根不是物件
    [InlineData("\"just a string\"")]
    public void Strip_空值或格式異常_回傳空白流程圖且不拋例外(string? szInput)
    {
        var result = LogicFlowDiagramBindingStripper.Strip(szInput);
        Assert.Equal(LogicFlowDiagramBindingStripper.EmptyDiagram, result);
    }

    [Fact]
    public void Strip_缺nodes欄位_不拋例外且保留其餘內容()
    {
        var result = LogicFlowDiagramBindingStripper.Strip("{\"edges\":[]}");
        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(0, root.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void Strip_nodes內含非物件元素_略過不拋例外()
    {
        var result = LogicFlowDiagramBindingStripper.Strip(
            "{\"nodes\":[null,5,{\"id\":1,\"type\":\"input\",\"sid\":\"X\"}],\"edges\":[]}");
        var nodes = JsonDocument.Parse(result).RootElement.GetProperty("nodes");
        Assert.Equal(3, nodes.GetArrayLength());
        Assert.False(nodes[2].TryGetProperty("sid", out _));
    }

    [Fact]
    public void Strip_空節點陣列_原樣回傳()
    {
        var result = LogicFlowDiagramBindingStripper.Strip(LogicFlowDiagramBindingStripper.EmptyDiagram);
        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(0, root.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, root.GetProperty("edges").GetArrayLength());
    }
}
