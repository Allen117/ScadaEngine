using ScadaEngine.Web.Features.AlarmSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Alarm;

/// <summary>
/// 鎖住 Web 端警報規則存檔驗證：前置條件點位 / 運算子 / 比較值 / 延遲範圍。
/// 驗證漏掉 → 存進 Engine 無法評估的規則（閘門永遠關閉 = 警報被靜默遮蔽）。
/// </summary>
public class AlarmRuleValidateTests
{
    private static AlarmRuleSaveDto Dto(Action<AlarmRuleSaveDto>? set = null)
    {
        var dto = new AlarmRuleSaveDto
        {
            sid = "196865-S1",
            isPrecondition = true,
            preSid = "196865-S2",
            preOperator = "GE",
            preValue = 30,
            preDelaySec = 30
        };
        set?.Invoke(dto);
        return dto;
    }

    [Fact]
    public void Valid_ReturnsNull() => Assert.Null(AlarmRuleService.ValidateRule(Dto()));

    [Fact]
    public void PreconditionOff_IgnoresPreFields()
        => Assert.Null(AlarmRuleService.ValidateRule(Dto(d => { d.isPrecondition = false; d.preSid = null; d.preOperator = "XX"; })));

    [Theory]
    [InlineData("", "alarm.error.pre_sid_required")]
    [InlineData("196865-s1", "alarm.error.pre_sid_self")]
    [InlineData("DMD-196865-S3", "alarm.error.pre_sid_virtual")]
    [InlineData("NRGD-5", "alarm.error.pre_sid_virtual")]
    [InlineData("NRGP-5", "alarm.error.pre_sid_virtual")]
    public void PreSid_Rejected(string szPreSid, string szExpectedKey)
        => Assert.Equal(szExpectedKey, AlarmRuleService.ValidateRule(Dto(d => d.preSid = szPreSid)));

    [Fact]
    public void Operator_Whitelist()
    {
        Assert.Equal("alarm.error.pre_operator_invalid", AlarmRuleService.ValidateRule(Dto(d => d.preOperator = ">=")));
        Assert.Null(AlarmRuleService.ValidateRule(Dto(d => d.preOperator = "ne")));
    }

    [Fact]
    public void CompareOperator_RequiresValue_DiDoesNot()
    {
        Assert.Equal("alarm.error.pre_value_required", AlarmRuleService.ValidateRule(Dto(d => d.preValue = null)));
        Assert.Null(AlarmRuleService.ValidateRule(Dto(d => { d.preOperator = "ON"; d.preValue = null; })));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(86401, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 86401)]
    public void Delay_OutOfRange(int nPreDelay, int nAlarmDelay)
        => Assert.Equal("alarm.error.delay_range",
            AlarmRuleService.ValidateRule(Dto(d => { d.preDelaySec = nPreDelay; d.alarmDelaySec = nAlarmDelay; })));

    [Fact]
    public void EmptySid_Rejected()
        => Assert.Equal("alarm.error.sid_required", AlarmRuleService.ValidateRule(Dto(d => d.sid = " ")));
}
