// @algorithm: COP計算(C#)
// @inputs: cooling_capacity, power
// @outputs: out
// @description: C# 版 COP 計算（冷凍效率 = 冷凍能力 / 功率）；power=0 由框架自動標 DIVIDE_BY_ZERO

using System.Collections.Generic;
using ScadaEngine.Algorithms;

public static class CopCalcCsharp
{
    public static AlgorithmResult EvaluateOne(double cooling_capacity, double power)
        => AlgorithmResult.Ok(new() { ["out"] = cooling_capacity / power });
}
