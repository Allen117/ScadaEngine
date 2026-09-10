namespace ScadaEngine.Engine.Services;

/// <summary>
/// 演算法「自動注入輸入」（@inputs_auto_repeat）輔助：
/// 依演算法 metadata 宣告，對每組 i 從輸出 port {port}{i} 循下游找 output 節點 SID，
/// 查點位 Min/Max 後注入 {key}{i} 到 inputs dict。
/// 純函式（圖結構與點位表以 delegate 傳入），供 LogicFlowExecutionService 呼叫與單元測試覆蓋。
/// </summary>
public static class AlgoAutoInputHelper
{
    /// <summary>@inputs_auto_repeat 單筆定義：key = 注入的輸入名（套組 suffix）、
    /// Port = 來源輸出 port 名（套組 suffix）、Field = 點位欄位（Min / Max）</summary>
    public record AutoRepeatDef(string Key, string Port, string Field);

    /// <summary>
    /// per-sourcePort 下游 BFS：從 nStartNodeId 的指定輸出 port 出發（只走 SourcePort 相符的起始邊），
    /// 找第一個 output 節點的 SID；找不到回 null。
    /// 與 node-level FindDownstreamOutputSid 的差異：同節點不同輸出 port 各自對應不同下游點位。
    /// </summary>
    public static string? FindDownstreamOutputSid(
        IEnumerable<(int Source, string SourcePort, int Target)> edges,
        Func<int, (bool IsOutputWithSid, string? Sid)> nodeLookup,
        int nStartNodeId, string szSourcePort)
    {
        var edgeList = edges as IList<(int Source, string SourcePort, int Target)> ?? edges.ToList();
        var visited = new HashSet<int> { nStartNodeId };
        var queue = new Queue<int>();
        foreach (var e in edgeList.Where(e => e.Source == nStartNodeId && e.SourcePort == szSourcePort))
            queue.Enqueue(e.Target);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId)) continue;
            var (isOutputWithSid, szSid) = nodeLookup(nodeId);
            if (isOutputWithSid) return szSid;
            // 中繼節點（timer / contact 等）→ 繼續往下游（不限 port）
            foreach (var e in edgeList.Where(e => e.Source == nodeId))
                queue.Enqueue(e.Target);
        }
        return null;
    }

    /// <summary>
    /// 依 auto 定義展開 N 組注入值寫進 inputDict。
    /// 查不到下游 SID、點位不存在、或對應欄位為 NULL → 不注入該 key（決策 6：
    /// 缺漏不做 fallback，交由 Python 框架自然回 INPUT_MISSING 擋下游）。
    /// </summary>
    public static void InjectAutoInputs(
        Dictionary<string, double> inputDict,
        IReadOnlyList<AutoRepeatDef> defs,
        int nGroupCount,
        Func<string, string?> resolveSidByPort,
        Func<string, (float? fMin, float? fMax)?> lookupMinMax)
    {
        for (int i = 1; i <= nGroupCount; i++)
        {
            foreach (var def in defs)
            {
                var szSid = resolveSidByPort($"{def.Port}{i}");
                if (string.IsNullOrEmpty(szSid)) continue;
                var mm = lookupMinMax(szSid);
                if (mm == null) continue;
                float? fVal = def.Field switch
                {
                    "Min" => mm.Value.fMin,
                    "Max" => mm.Value.fMax,
                    _ => null,
                };
                if (fVal.HasValue)
                    inputDict[$"{def.Key}{i}"] = fVal.Value;
            }
        }
    }
}
