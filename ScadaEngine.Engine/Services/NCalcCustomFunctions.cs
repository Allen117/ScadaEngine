using NCalc;
using ScadaEngine.Common.Algorithms;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 計算點位公式的 NCalc 自訂函數集中註冊點（Engine 實際計算與 Web 公式預覽共用）。
/// 函數名稱不分大小寫。新增函數時同步更新 CalcPoint 頁的公式 tooltip 與功能說明書_Engine核心.md。
/// </summary>
public static class NCalcCustomFunctions
{
    /// <summary>變數名稱保留字 — 與自訂函數同名的變數會被 WrapFormulaVariables 包成 [變數] 而破壞公式</summary>
    public static readonly string[] ReservedNames = { "WetBulb" };

    /// <summary>是否為保留字（不分大小寫）</summary>
    public static bool IsReservedName(string szName)
        => ReservedNames.Any(r => string.Equals(r, szName, StringComparison.OrdinalIgnoreCase));

    /// <summary>對 Expression 掛上所有自訂函數</summary>
    public static void Register(Expression expression)
    {
        expression.EvaluateFunction += (szName, args) =>
        {
            if (string.Equals(szName, "WetBulb", StringComparison.OrdinalIgnoreCase))
            {
                // WetBulb(T, RH) — Stull 濕球溫度，範圍外回 NaN 由 NaN→Bad 機制接手
                if (args.Parameters.Count != 2)
                    throw new ArgumentException("WetBulb(T, RH) 需要 2 個參數");
                var dTemp = Convert.ToDouble(args.Parameters.Evaluate(0));
                var dRh = Convert.ToDouble(args.Parameters.Evaluate(1));
                args.Result = Psychrometrics.WetBulbStull(dTemp, dRh);
            }
        };
    }
}
