namespace ScadaEngine.ModbusServer.Core;

/// <summary>
/// SID 自然排序比較器：數字段依數值比較，其餘依序比較字元（不分大小寫）。
/// 例：196865-S2 &lt; 196865-S10、DB1-S9 &lt; DB1-S10 &lt; DB2-S1。
/// 首次配址與後續 append 皆以此排序，確保配址結果可重現。
/// </summary>
public class NaturalSidComparer : IComparer<string>
{
    public static readonly NaturalSidComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            char cx = x[i], cy = y[j];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                // 取出兩邊完整數字段比大小（跳過前導零後先比長度再逐位比）
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var nx = x.AsSpan(si, i - si).TrimStart('0');
                var ny = y.AsSpan(sj, j - sj).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length - ny.Length;
                int cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                // 數值相同（含前導零差異）→ 繼續比後面
            }
            else
            {
                int cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (cmp != 0) return cmp;
                i++; j++;
            }
        }
        return (x.Length - i) - (y.Length - j);
    }
}
