using ScadaEngine.Web.Features.Ems.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 虛擬主要電表的電表資訊聚合器 — 從子孫葉子的 V/I/P/PF SIDs 讀即時值，套 effective sign 聚合成 4 個數值。
///
/// 演算法（詳見 docs/功能說明書_能源管理.md「電表資訊自動聚合」）：
///   功率 P_total = Σ (P_i × sign_i)                     — 有 PowerSID 且 Q=GOOD 的葉子
///   電流 I_total = Σ (I_i × sign_i)                     — 有 CurrentSID 且 Q=GOOD 的葉子
///   電壓 V       = 第一顆有 VoltageSID 且 Q=GOOD 的葉子（依 nSortOrder → nId 排序）
///   功因 PF      = ΣP_pf / √(ΣP_pf² + ΣQ²)，僅計「同時有 PowerSID 且 PowerFactorSID 且皆 GOOD」的葉子
///                  Q_i = P_i × tan(arccos(PF_i))；sign 同時套在 P 與 Q（保持物理一致）
/// </summary>
public class MainMeterAggregationService
{
    private readonly EnergyCircuitService _circuitService;
    private readonly MqttRealtimeSubscriberService _realtime;

    public MainMeterAggregationService(
        EnergyCircuitService circuitService,
        MqttRealtimeSubscriberService realtime)
    {
        _circuitService = circuitService;
        _realtime = realtime;
    }

    /// <summary>
    /// 計算指定虛擬主要電表的 V/I/P/PF 聚合值。任一格若無有效樣本則回 null（前端顯示「--」）。
    /// </summary>
    public async Task<EmsMainMeterValuesDto> ComputeAsync(int nMainMeterId)
    {
        var leaves = await _circuitService.GetLeavesUnderAsync(nMainMeterId);
        // 排序：先 nSortOrder（同層順序），再 nId（跨層穩定）— 電壓「取第一顆」要決定性
        var ordered = leaves
            .OrderBy(l => l.Leaf.nSortOrder)
            .ThenBy(l => l.Leaf.nId)
            .ToList();

        // 收集所有欲查的 SIDs（去重）— 一次拉 cache
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in ordered)
        {
            if (!string.IsNullOrWhiteSpace(l.Leaf.szVoltageSID)) sids.Add(l.Leaf.szVoltageSID!);
            if (!string.IsNullOrWhiteSpace(l.Leaf.szCurrentSID)) sids.Add(l.Leaf.szCurrentSID!);
            if (!string.IsNullOrWhiteSpace(l.Leaf.szPowerSID)) sids.Add(l.Leaf.szPowerSID!);
            if (!string.IsNullOrWhiteSpace(l.Leaf.szPowerFactorSID)) sids.Add(l.Leaf.szPowerFactorSID!);
        }

        var items = _realtime.GetRealtimeDataBySids(sids);
        var cache = items.ToDictionary(x => x.szSID, x => x, StringComparer.OrdinalIgnoreCase);

        // 取 GOOD 值；否則回 null
        double? Read(string? szSid)
        {
            if (string.IsNullOrWhiteSpace(szSid)) return null;
            if (!cache.TryGetValue(szSid!, out var item)) return null;
            if (!item.hasData) return null;
            if (!item.isQualityGood) return null;
            return item.dValue;
        }

        // ── 電壓：第一顆有值的葉子 ───────────────────────────
        double? dVoltage = null;
        foreach (var l in ordered)
        {
            var v = Read(l.Leaf.szVoltageSID);
            if (v != null) { dVoltage = v; break; }
        }

        // ── 電流、功率、功因：加總 ───────────────────────────
        double dCurrentSum = 0;
        double dPowerSum = 0;
        bool bHasCurrentSample = false;
        bool bHasPowerSample = false;

        double dPowerPfSubsetSum = 0;
        double dReactivePowerSum = 0;
        bool bHasPfSample = false;

        foreach (var l in ordered)
        {
            var nSign = l.nEffectiveSign; // +1 / -1
            var vI = Read(l.Leaf.szCurrentSID);
            if (vI != null)
            {
                dCurrentSum += vI.Value * nSign;
                bHasCurrentSample = true;
            }
            var vP = Read(l.Leaf.szPowerSID);
            if (vP != null)
            {
                dPowerSum += vP.Value * nSign;
                bHasPowerSample = true;
            }
            var vPf = Read(l.Leaf.szPowerFactorSID);
            // PF 計算：該葉子須同時有 P 與 PF；PF 需 (0,1]（0 值 → 沒功率、也沒角度資訊，跳過）
            if (vP != null && vPf != null && vPf.Value > 0 && vPf.Value <= 1.0)
            {
                var dP = vP.Value * nSign;
                // Q_i = P_i × tan(arccos(PF_i))；sign 與 P 同步套用（保持物理一致）
                var dQ = dP * Math.Tan(Math.Acos(vPf.Value));
                dPowerPfSubsetSum += dP;
                dReactivePowerSum += dQ;
                bHasPfSample = true;
            }
        }

        double? dPf = null;
        if (bHasPfSample)
        {
            var dS = Math.Sqrt(dPowerPfSubsetSum * dPowerPfSubsetSum + dReactivePowerSum * dReactivePowerSum);
            // S = 0 → PF 未定義；分子帶 sign 保留功因方向（負值代表功率反送）
            if (dS > 1e-9) dPf = dPowerPfSubsetSum / dS;
        }

        return new EmsMainMeterValuesDto
        {
            voltage     = dVoltage,
            current     = bHasCurrentSample ? Math.Round(dCurrentSum, 3) : null,
            power       = bHasPowerSample   ? Math.Round(dPowerSum, 3)   : null,
            powerFactor = dPf.HasValue      ? Math.Round(dPf.Value, 4)   : null
        };
    }
}
