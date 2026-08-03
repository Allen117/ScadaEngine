using ScadaEngine.Web.Features.ScadaPage.Models;

namespace ScadaEngine.Tests.ScadaPage;

/// <summary>
/// 鎖住 ControlActionTypeExtensions.ParseActionType：控制動作字串 → enum（對應 EventLog i18n key）。
/// 對錯 = 稽核日誌記錯動作類型。純映射，未知/null 一律 Unknown。
/// </summary>
public class ParseActionTypeTests
{
    [Theory]
    [InlineData("button", ControlActionType.Button)]
    [InlineData("ao_manual", ControlActionType.AoManual)]
    [InlineData("ao_auto", ControlActionType.AoAuto)]
    [InlineData("do_set", ControlActionType.DoSet)]
    [InlineData("do_auto", ControlActionType.DoAuto)]
    [InlineData("pump_start_stop", ControlActionType.PumpStartStop)]
    [InlineData("pump_freq", ControlActionType.PumpFreq)]
    [InlineData("pump_auto", ControlActionType.PumpAuto)]
    public void 已知動作字串_正確映射(string sz, ControlActionType expected)
    {
        Assert.Equal(expected, ControlActionTypeExtensions.ParseActionType(sz));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Button")]       // 大小寫敏感，非小寫不命中
    [InlineData("unknown_verb")]
    public void 未知或null_回Unknown(string? sz)
    {
        Assert.Equal(ControlActionType.Unknown, ControlActionTypeExtensions.ParseActionType(sz));
    }
}
