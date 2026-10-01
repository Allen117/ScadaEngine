using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Services;

namespace ScadaEngine.Tests.Alarm;

/// <summary>
/// 鎖住警報前置條件（連鎖遮蔽）與 on-delay 的判定規則：
/// 運算子邊界、延遲開閘、中途不成立歸零、品質 Bad / 無值視為不成立。
/// 判錯 → 警報被誤遮蔽（漏報）或遮不住（誤報），屬警報核心邏輯。
/// </summary>
public class AlarmPreconditionGateTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0);

    // ── 運算子 ──

    [Theory]
    [InlineData("GE", 30.0, 30.0, true)]
    [InlineData("GE", 30.0, 29.9, false)]
    [InlineData("GT", 30.0, 30.0, false)]
    [InlineData("GT", 30.0, 30.1, true)]
    [InlineData("LE", 30.0, 30.0, true)]
    [InlineData("LE", 30.0, 30.1, false)]
    [InlineData("LT", 30.0, 30.0, false)]
    [InlineData("LT", 30.0, 29.9, true)]
    [InlineData("EQ", 30.0, 30.0, true)]
    [InlineData("EQ", 30.0, 30.5, false)]
    [InlineData("NE", 30.0, 30.0, false)]
    [InlineData("NE", 30.0, 30.5, true)]
    [InlineData("ge", 30.0, 31.0, true)]  // 大小寫不敏感
    public void Operators_Boundary(string szOp, double dTarget, double dX, bool isExpected)
    {
        Assert.Equal(isExpected, AlarmPreconditionGate.IsConditionMet(szOp, dTarget, dX, true));
    }

    [Fact]
    public void Eq_ToleratesFloatPrecision()
    {
        // 即時值來自 float：30.1f 轉 double = 30.100000381...
        Assert.True(AlarmPreconditionGate.IsConditionMet("EQ", 30.1, (double)30.1f, true));
        Assert.True(AlarmPreconditionGate.IsConditionMet("GE", 30.1, (double)30.1f, true));
        Assert.False(AlarmPreconditionGate.IsConditionMet("GT", 30.1, (double)30.1f, true));
    }

    [Theory]
    [InlineData("ON", 1.0, true)]
    [InlineData("ON", 0.0, false)]
    [InlineData("OFF", 0.0, true)]
    [InlineData("OFF", 1.0, false)]
    public void DiOperators(string szOp, double dX, bool isExpected)
    {
        // DI 運算子不看 PreValue
        Assert.Equal(isExpected, AlarmPreconditionGate.IsConditionMet(szOp, null, dX, true));
    }

    [Fact]
    public void BadQuality_OrNoValue_NotMet()
    {
        Assert.False(AlarmPreconditionGate.IsConditionMet("ON", null, 1.0, false));
        Assert.False(AlarmPreconditionGate.IsConditionMet("GE", 0, null, true));
        Assert.False(AlarmPreconditionGate.IsConditionMet("GE", 0, double.NaN, true));
    }

    [Fact]
    public void UnknownOperator_OrMissingTarget_NotMet()
    {
        Assert.False(AlarmPreconditionGate.IsConditionMet("XX", 1, 1, true));
        Assert.False(AlarmPreconditionGate.IsConditionMet("GE", null, 1, true));
    }

    // ── 閘門延遲 ──

    [Fact]
    public void DelayZero_OpensImmediately()
    {
        var gate = new AlarmPreconditionGate("ON", null, 0);
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0));
    }

    [Fact]
    public void Delay_PendingUntilElapsed_ThenOpen()
    {
        var gate = new AlarmPreconditionGate("ON", null, 30);
        Assert.Equal(PreconditionGateState.Pending, gate.Update(1, true, T0));
        Assert.Equal(PreconditionGateState.Pending, gate.Update(1, true, T0.AddSeconds(29)));
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0.AddSeconds(30)));
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0.AddSeconds(500)));
    }

    [Fact]
    public void Delay_InterruptedResetsCountdown()
    {
        var gate = new AlarmPreconditionGate("ON", null, 30);
        gate.Update(1, true, T0);
        Assert.Equal(PreconditionGateState.Closed, gate.Update(0, true, T0.AddSeconds(20)));
        // 重新從 25s 起算，55s 前都還沒滿
        Assert.Equal(PreconditionGateState.Pending, gate.Update(1, true, T0.AddSeconds(25)));
        Assert.Equal(PreconditionGateState.Pending, gate.Update(1, true, T0.AddSeconds(54)));
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0.AddSeconds(55)));
    }

    [Fact]
    public void Open_ThenConditionFalse_ClosesImmediately()
    {
        var gate = new AlarmPreconditionGate("GE", 30, 10);
        gate.Update(40, true, T0);
        Assert.Equal(PreconditionGateState.Open, gate.Update(40, true, T0.AddSeconds(10)));
        Assert.Equal(PreconditionGateState.Closed, gate.Update(20, true, T0.AddSeconds(11)));
    }

    [Fact]
    public void Open_ThenBadQuality_Closes()
    {
        var gate = new AlarmPreconditionGate("ON", null, 0);
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0));
        Assert.Equal(PreconditionGateState.Closed, gate.Update(1, false, T0.AddSeconds(1)));
        Assert.Equal(PreconditionGateState.Closed, gate.Update(null, true, T0.AddSeconds(2)));
    }

    [Fact]
    public void NegativeDelay_TreatedAsZero()
    {
        var gate = new AlarmPreconditionGate("ON", null, -5);
        Assert.Equal(0, gate.nDelaySec);
        Assert.Equal(PreconditionGateState.Open, gate.Update(1, true, T0));
    }

    // ── OnDelayTimer（本點 on-delay）──

    [Fact]
    public void OnDelay_ZeroIsImmediate()
    {
        var timer = new OnDelayTimer();
        Assert.True(timer.Update(true, T0, 0));
        Assert.False(timer.Update(false, T0.AddSeconds(1), 0));
    }

    [Fact]
    public void OnDelay_RequiresContinuousCondition()
    {
        var timer = new OnDelayTimer();
        Assert.False(timer.Update(true, T0, 10));
        Assert.False(timer.Update(true, T0.AddSeconds(9), 10));
        Assert.True(timer.Update(true, T0.AddSeconds(10), 10));
        // 回正常歸零
        Assert.False(timer.Update(false, T0.AddSeconds(11), 10));
        Assert.Null(timer.dtSince);
        Assert.False(timer.Update(true, T0.AddSeconds(12), 10));
        Assert.True(timer.Update(true, T0.AddSeconds(22), 10));
    }

    [Fact]
    public void OnDelay_ResetWhileGateNotOpen_DoesNotAccumulate()
    {
        // 模擬 AlarmMonitorService：閘門未 Open 期間每次 tick 都 Reset，Open 後才開始計時
        var timer = new OnDelayTimer();
        timer.Update(true, T0, 10);
        timer.Reset();                                  // 閘門 Pending
        Assert.False(timer.Update(true, T0.AddSeconds(15), 10)); // 閘門 Open 才起算
        Assert.True(timer.Update(true, T0.AddSeconds(25), 10));
    }

    // ── 恢復通知旗標（一般恢復與前置清除共用）──

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void RecoveryNotifyPlan_FollowsRuleFlags(bool isLine, bool isEmail, bool isSms)
    {
        var rule = new AlarmRuleModel
        {
            isRecoveryNotifyLine = isLine,
            isRecoveryNotifyEmail = isEmail,
            isRecoveryNotifySms = isSms
        };
        var plan = RecoveryNotifyPlan.From(rule);
        Assert.Equal(isLine, plan.isLine);
        Assert.Equal(isEmail, plan.isEmail);
        Assert.Equal(isSms, plan.isSms);
    }

    [Fact]
    public void RecoveryNotifyPlan_Defaults_AllOn()
    {
        // 預設三通道都發（與加欄位前行為一致；簡訊另受全域 SendRecovery 約束）
        var plan = RecoveryNotifyPlan.From(new AlarmRuleModel());
        Assert.True(plan.isLine);
        Assert.True(plan.isEmail);
        Assert.True(plan.isSms);
    }
}
