using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Core;

/// <summary>
/// 「對外 Modbus 實際供值」的判定規則（純邏輯）— Modbus 掃寫迴圈與網頁 API 共用，
/// 確保網頁顯示的值/狀態與 Modbus 讀到的完全一致。
/// </summary>
public static class ServedValueRules
{
    /// <summary>
    /// 計算對外供值。
    /// 規則：從未有值 → NaN；品質 GOOD 且新鮮且點位仍存在 → 實際值；
    /// 其餘（品質 Bad / 逾時未更新 / 點位已刪除停用）→ 依 BadQualityMode 回 NaN 或保持最後值。
    /// DB 來源（isFreshBypass）以 SQL 讀取成功代表新鮮，不看時間戳。
    /// </summary>
    public static float Compute(RealtimeValueModel? item, bool isActive, bool isHoldLast,
        int nStaleSeconds, DateTime dtNow)
    {
        if (item == null || !item.hasData)
            return FloatRegisterEncoder.QuietNaN;

        if (isActive && IsGood(item) && IsFresh(item, nStaleSeconds, dtNow))
            return (float)item.dValue;

        return isHoldLast ? (float)item.dValue : FloatRegisterEncoder.QuietNaN;
    }

    /// <summary>網頁對照表的狀態標籤（DISABLED / NO_DATA / STALE / GOOD / BAD / ...）</summary>
    public static string GetStatusLabel(RealtimeValueModel? item, bool isActive, int nStaleSeconds, DateTime dtNow)
    {
        if (!isActive) return "DISABLED";
        if (item == null || !item.hasData) return "NO_DATA";
        if (!IsGood(item)) return item.szQuality;
        if (!IsFresh(item, nStaleSeconds, dtNow)) return "STALE";
        return "GOOD";
    }

    private static bool IsGood(RealtimeValueModel item) =>
        string.Equals(item.szQuality, "GOOD", StringComparison.OrdinalIgnoreCase);

    private static bool IsFresh(RealtimeValueModel item, int nStaleSeconds, DateTime dtNow) =>
        item.isFreshBypass || (dtNow - item.dtTimestamp).TotalSeconds <= nStaleSeconds;
}
