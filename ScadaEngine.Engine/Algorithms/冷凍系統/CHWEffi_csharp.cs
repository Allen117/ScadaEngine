// @algorithm: kW/RT 計算(C#)
// @inputs: kW, RT
// @outputs: kW/RT
// @description: 越低越好；RT=0 由演算法主動回 DIVIDE_BY_ZERO（示範使用者主動回傳 status）

using System.Collections.Generic;
using ScadaEngine.Algorithms;

public static class CHWEffiCsharp
{
    /// <summary>
    /// kW/RT = 耗電量 / 冷凍噸。
    ///
    /// Status（業務語意，由演算法主動回傳）:
    ///   DIVIDE_BY_ZERO (Error) — RT=0 時主動回傳，省去框架的 inf/nan 偵測路徑
    ///
    /// 註：C# double 除以 0 不丟 DivideByZeroException（IEEE 754 → Infinity），
    ///     框架會在偵測到非有限結果且輸入有 0 時自動升為 DivideByZero；
    ///     此處顯式檢查純粹示範「使用者主動回 status」的寫法。
    ///
    /// 註 2：C# 第一版不支援 variadic，故無對應 Python 版的 @variadic / @inputs_repeat 標記。
    /// </summary>
    public static AlgorithmResult EvaluateOne(double kW, double RT)
    {
        if (RT == 0)
            return AlgorithmResult.From(
                new Dictionary<string, double> { ["kW/RT"] = 0 },
                AlgorithmStatusCode.DivideByZero);

        return AlgorithmResult.Ok(new Dictionary<string, double> { ["kW/RT"] = kW / RT });
    }
}
