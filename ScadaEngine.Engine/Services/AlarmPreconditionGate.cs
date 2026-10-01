namespace ScadaEngine.Engine.Services;

/// <summary>前置條件閘門狀態</summary>
public enum PreconditionGateState
{
    /// <summary>條件不成立（或前置點位品質 Bad / 無資料）— 本點警報一律不觸發，已 active 者清除</summary>
    Closed,
    /// <summary>條件成立但未滿延遲 — 不觸發新警報，已 active 者僅能自行恢復</summary>
    Pending,
    /// <summary>條件已連續成立滿延遲 — 正常判斷本點警報</summary>
    Open
}

/// <summary>
/// 警報前置條件（連鎖遮蔽）閘門 — 純邏輯，一條規則一個實例。
/// 前置點位 X 的條件須連續成立 nDelaySec 秒才開閘；任一次不成立立即關閘並歸零。
/// </summary>
public sealed class AlarmPreconditionGate
{
    private readonly OnDelayTimer _timer = new();

    public string szOperator { get; }
    public double? dValue { get; }
    public int nDelaySec { get; }

    public AlarmPreconditionGate(string szOperator, double? dValue, int nDelaySec)
    {
        this.szOperator = (szOperator ?? string.Empty).Trim().ToUpperInvariant();
        this.dValue = dValue;
        this.nDelaySec = Math.Max(0, nDelaySec);
    }

    /// <summary>以 X 的最新值更新閘門，回傳當下狀態</summary>
    public PreconditionGateState Update(double? dXValue, bool isGood, DateTime dtNow)
    {
        bool isMet = IsConditionMet(szOperator, dValue, dXValue, isGood);
        if (!isMet)
        {
            _timer.Reset();
            return PreconditionGateState.Closed;
        }
        return _timer.Update(true, dtNow, nDelaySec)
            ? PreconditionGateState.Open
            : PreconditionGateState.Pending;
    }

    /// <summary>判斷前置條件是否成立（不含延遲）。品質 Bad / 無值 / 未知運算子 → 不成立。</summary>
    public static bool IsConditionMet(string? szOperator, double? dTarget, double? dXValue, bool isGood)
    {
        if (!isGood || !dXValue.HasValue || double.IsNaN(dXValue.Value))
            return false;

        double dX = dXValue.Value;
        string szOp = (szOperator ?? string.Empty).Trim().ToUpperInvariant();
        switch (szOp)
        {
            // DI：與 DI 警報判定一致（|v - 1| < 0.01 視為 ON）
            case "ON":  return IsOn(dX);
            case "OFF": return !IsOn(dX);
        }

        if (!dTarget.HasValue)
            return false;
        double dT = dTarget.Value;
        return szOp switch
        {
            "GE" => dX >= dT || NearlyEqual(dX, dT),
            "GT" => dX > dT && !NearlyEqual(dX, dT),
            "LE" => dX <= dT || NearlyEqual(dX, dT),
            "LT" => dX < dT && !NearlyEqual(dX, dT),
            "EQ" => NearlyEqual(dX, dT),
            "NE" => !NearlyEqual(dX, dT),
            _ => false
        };
    }

    /// <summary>支援的運算子白名單</summary>
    public static readonly IReadOnlyList<string> ValidOperators =
        new[] { "GE", "GT", "LE", "LT", "EQ", "NE", "ON", "OFF" };

    private static bool IsOn(double dX) => Math.Abs(dX - 1.0) < 0.01;

    /// <summary>即時值多來自 float，與使用者輸入 double 比較需容差（相對 1e-5，最小絕對 1e-5）</summary>
    private static bool NearlyEqual(double a, double b)
        => Math.Abs(a - b) <= 1e-5 * Math.Max(1.0, Math.Abs(b));
}

/// <summary>
/// 警報恢復時各通道是否發恢復通知（規則 RecoveryNotifyLine / Email / Sms）。
/// 一般恢復與前置條件不成立造成的清除皆適用；簡訊另受 SmsSetting.SendRecovery 約束（由簡訊服務內部判斷）。
/// </summary>
public readonly record struct RecoveryNotifyPlan(bool isLine, bool isEmail, bool isSms)
{
    public static RecoveryNotifyPlan From(ScadaEngine.Common.Data.Models.AlarmRuleModel rule)
        => new(rule.isRecoveryNotifyLine, rule.isRecoveryNotifyEmail, rule.isRecoveryNotifySms);
}
