using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Core;

/// <summary>某來源類型的位址段已滿，無法為新點位配址</summary>
public class AddressSegmentFullException : Exception
{
    public AddressSegmentFullException(string szSource, string szSid, int nNextAddress, int nEndExclusive)
        : base($"位址段已滿：來源 {szSource} 的下一個位址 {nNextAddress} 超出段界 {nEndExclusive}（SID={szSid}）。" +
               "請於 ModbusServerSetting.json 擴大該段容量（注意：改段界會影響既有下游對接，僅限規劃期調整）。")
    {
    }
}

/// <summary>
/// Append-only 配址核心（純邏輯，無 I/O）。
///
/// 規則（docs/plans 決策 2）：
/// 1. 每點 float32 = 2 registers，位址一律偶數對齊。
/// 2. 每個來源類型一個段；段內首次配址依 SID 自然排序自段首依序配置。
/// 3. 之後每次同步只把「新出現的 SID」append 到段內已用位址之後，永不重排、永不回收。
/// 4. 已從點位表消失的 SID 保留原位址（下游 PLC/HMI 的位址表永久有效）。
/// 5. 段溢位擲 <see cref="AddressSegmentFullException"/>。
/// </summary>
public static class AddressAllocator
{
    /// <summary>
    /// 將目前點位目錄同步進既有 AddressMap。
    /// 回傳新增筆數；傳入的 <paramref name="map"/> 就地更新（append 新點 + 刷新輔助欄位）。
    /// </summary>
    public static int Sync(AddressMapModel map, IReadOnlyList<CatalogPointModel> catalog,
        IReadOnlyDictionary<string, AddressSegmentModel> segments)
    {
        var existingBySid = map.Entries.ToDictionary(e => e.szSID, StringComparer.Ordinal);

        // 各段目前下一個可用位址：段內既有最大位址 + 2（永不回收）；空段從段首開始
        var nextAddress = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (szSource, segment) in segments)
        {
            var used = map.Entries.Where(e => e.szSource == szSource).Select(e => e.nAddress).ToList();
            nextAddress[szSource] = used.Count > 0 ? used.Max() + 2 : segment.nStart;
        }

        int nAdded = 0;
        foreach (var group in catalog.GroupBy(p => p.Source))
        {
            var szSource = group.Key.ToString();
            if (!segments.TryGetValue(szSource, out var segment))
                throw new InvalidOperationException($"ModbusServerSetting.json 缺少來源 {szSource} 的段界設定");

            foreach (var point in group.OrderBy(p => p.szSID, NaturalSidComparer.Instance))
            {
                if (existingBySid.TryGetValue(point.szSID, out var entry))
                {
                    // 既有點：位址不動，只刷新輔助資訊
                    entry.szName = point.szName;
                    entry.szUnit = point.szUnit;
                    entry.szRawAddress = point.szRawAddress;
                    continue;
                }

                var nAddr = nextAddress[szSource];
                if (nAddr + 2 > segment.nEndExclusive)
                    throw new AddressSegmentFullException(szSource, point.szSID, nAddr, segment.nEndExclusive);

                var newEntry = new AddressMapEntryModel
                {
                    szSID = point.szSID,
                    szSource = szSource,
                    nAddress = nAddr,
                    szName = point.szName,
                    szUnit = point.szUnit,
                    szRawAddress = point.szRawAddress,
                };
                map.Entries.Add(newEntry);
                existingBySid[point.szSID] = newEntry;
                nextAddress[szSource] = nAddr + 2;
                nAdded++;
            }
        }

        if (nAdded > 0)
            map.Entries.Sort((a, b) => a.nAddress.CompareTo(b.nAddress));

        return nAdded;
    }
}
