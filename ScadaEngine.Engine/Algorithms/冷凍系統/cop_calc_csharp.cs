// @algorithm: COP計算(C#)
// @inputs: cooling_capacity, power
// @outputs: out
// @description: C# 版 COP 計算（冷凍效率 = 冷凍能力 / 功率）

using System.Collections.Generic;

public static class CopCalcCsharp
{
    public static Dictionary<string, double> Evaluate(Dictionary<string, double> inputs)
    {
        var cp = inputs.GetValueOrDefault("cooling_capacity", 0);
        var pw = inputs.GetValueOrDefault("power", 0);
        var cop = pw != 0 ? cp / pw : 0;
        return new Dictionary<string, double> { ["out"] = cop };
    }
}
