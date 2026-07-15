using MathNet.Numerics.Distributions;
using MathNet.Numerics.LinearAlgebra;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 能源基線 OLS 複線性回歸求解器 — 純數學、無 DB / IO 依賴。
/// Y = β0 + β1·X1 + … + βp·Xp，QR 分解求解（溫濕度共線性下比正規方程式數值穩定），
/// 並由 (XᵀX)⁻¹ 推標準誤 → t 分布雙尾 p-value。
/// </summary>
public class BaselineRegressionEngine
{
    /// <summary>
    /// 執行 OLS 回歸。
    /// </summary>
    /// <param name="dYValues">因變數樣本（長度 n）</param>
    /// <param name="xColumns">自變數欄位陣列（p 欄，每欄長度 n，順序 = 變數 Sequence）</param>
    /// <exception cref="InvalidOperationException">樣本數不足以估計係數，或變數完全共線</exception>
    public BaselineRegressionResult Fit(double[] dYValues, IReadOnlyList<double[]> xColumns)
    {
        var n = dYValues.Length;
        var p = xColumns.Count;
        if (p < 1)
            throw new InvalidOperationException("至少需要一個自變數");
        if (xColumns.Any(c => c.Length != n))
            throw new InvalidOperationException("自變數樣本長度與因變數不一致");
        if (n < p + 2)
            throw new InvalidOperationException($"樣本數 {n} 不足：估計 {p} 個係數 + 截距至少需要 {p + 2} 筆樣本");

        // 設計矩陣 [1 | X1 | … | Xp]（n × (p+1)）
        var matX = Matrix<double>.Build.Dense(n, p + 1, (r, c) => c == 0 ? 1.0 : xColumns[c - 1][r]);
        var vecY = Vector<double>.Build.DenseOfArray(dYValues);

        var beta = matX.QR().Solve(vecY);
        if (beta.Any(b => double.IsNaN(b) || double.IsInfinity(b)))
            throw new InvalidOperationException("回歸求解失敗：自變數間完全共線（如同一點位選兩次）或資料異常，請調整變數後重試");

        // 殘差與判定係數
        var vecResidual = vecY - matX * beta;
        var dSse = vecResidual.DotProduct(vecResidual);
        var dMeanY = vecY.Average();
        var dSst = vecY.Sum(y => (y - dMeanY) * (y - dMeanY));
        var dR2 = dSst > 0 ? 1.0 - dSse / dSst : 0.0;

        var nDof = n - p - 1;   // 殘差自由度
        var dAdjR2 = nDof > 0 ? 1.0 - (1.0 - dR2) * (n - 1) / nDof : dR2;

        // CV(RMSE)：RMSE 用殘差自由度（IPMVP / ASHRAE Guideline 14 慣例），除以 Y 均值
        var dRmse = nDof > 0 ? Math.Sqrt(dSse / nDof) : 0.0;
        var dCvRmse = Math.Abs(dMeanY) > 1e-12 ? dRmse / Math.Abs(dMeanY) : double.NaN;

        // 各係數 p-value：se = √(σ²·diag((XᵀX)⁻¹))，t = β/se，雙尾 StudentT
        var dPValues = new double[p];
        Array.Fill(dPValues, double.NaN);
        if (nDof > 0)
        {
            try
            {
                var matXtxInv = (matX.TransposeThisAndMultiply(matX)).Inverse();
                var dSigma2 = dSse / nDof;
                for (var i = 0; i < p; i++)
                {
                    var dVar = dSigma2 * matXtxInv[i + 1, i + 1];
                    if (dVar <= 0 || double.IsNaN(dVar)) continue;
                    var dT = Math.Abs(beta[i + 1] / Math.Sqrt(dVar));
                    dPValues[i] = 2.0 * (1.0 - StudentT.CDF(0, 1, nDof, dT));
                }
            }
            catch
            {
                // XᵀX 近奇異（高度共線）→ p-value 留 NaN，前端顯示「—」提示改看調整後 R²
            }
        }

        return new BaselineRegressionResult
        {
            dIntercept = beta[0],
            dCoefficients = beta.SubVector(1, p).ToArray(),
            dPValues = dPValues,
            dR2 = dR2,
            dAdjR2 = dAdjR2,
            dCvRmse = dCvRmse,
            nSampleCount = n,
        };
    }

    /// <summary>用一組係數對單一樣本求預測值（EnPI 報告期用凍結係數）</summary>
    public static double Predict(double dIntercept, IReadOnlyList<double> dCoefficients, IReadOnlyList<double> dXValues)
    {
        var d = dIntercept;
        for (var i = 0; i < dCoefficients.Count; i++)
            d += dCoefficients[i] * dXValues[i];
        return d;
    }
}
