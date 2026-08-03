using ScadaEngine.Engine.Models;

namespace ScadaEngine.Tests.ConditionCtrl;

/// <summary>
/// 鎖住 ConditionControlRuleModel.ParseOperator：比較符號字串 → nOperator 位元組。
/// 對錯 = 條件控制規則觸發判斷用錯運算子。純映射，未知一律 0（">"）。
/// </summary>
public class ParseOperatorTests
{
    [Theory]
    [InlineData(">", 0)]
    [InlineData("<", 1)]
    [InlineData(">=", 2)]
    [InlineData("<=", 3)]
    [InlineData("==", 4)]
    [InlineData("!=", 5)]
    public void 已知符號_正確映射(string symbol, byte expected)
    {
        Assert.Equal(expected, ConditionControlRuleModel.ParseOperator(symbol));
    }

    [Theory]
    [InlineData("")]
    [InlineData("=>")]   // 順序顛倒，非合法符號
    [InlineData("=")]
    [InlineData("gt")]
    public void 未知符號_回退為0(string symbol)
    {
        Assert.Equal((byte)0, ConditionControlRuleModel.ParseOperator(symbol));
    }
}
