namespace ScadaEngine.Engine.Services;

/// <summary>
/// 「條件連續成立 N 秒」計時器（純邏輯，時間由呼叫端傳入，便於單元測試）。
/// 前置條件閘門延遲與本點警報 on-delay 共用。
/// 條件不成立即歸零；延遲 &lt;= 0 視為立即滿足。
/// </summary>
public sealed class OnDelayTimer
{
    /// <summary>條件開始連續成立的時間；null = 目前不成立</summary>
    public DateTime? dtSince { get; private set; }

    /// <summary>
    /// 餵入當下條件，回傳「是否已連續成立滿 nDelaySec 秒」。
    /// </summary>
    public bool Update(bool isCondition, DateTime dtNow, int nDelaySec)
    {
        if (!isCondition)
        {
            dtSince = null;
            return false;
        }

        dtSince ??= dtNow;
        if (nDelaySec <= 0)
            return true;
        return (dtNow - dtSince.Value).TotalSeconds >= nDelaySec;
    }

    public void Reset() => dtSince = null;
}
