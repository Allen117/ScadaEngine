namespace ScadaEngine.Common.Algorithms;

/// <summary>
/// 濕空氣熱力學純函數（無相依、可獨立驗證）。
/// </summary>
public static class Psychrometrics
{
    /// <summary>Stull 經驗式適用下限：相對濕度 %</summary>
    public const double MinRh = 5.0;
    /// <summary>相對濕度上限（100% 時濕球≈乾球，公式外插誤差極小，予以接受）</summary>
    public const double MaxRh = 100.0;
    /// <summary>
    /// 乾球溫度適用下限 °C。Stull 原文宣稱 -20 起，但實測 0°C 以下擬合劣化
    /// （對 RH 非單調達 1°C、低濕區 Tw &gt; T 達 +2.4°C），故收斂至 0。
    /// </summary>
    public const double MinTemp = 0.0;
    /// <summary>Stull 經驗式適用上限：乾球溫度 °C</summary>
    public const double MaxTemp = 50.0;

    /// <summary>
    /// 濕球溫度 — Stull (2011) 經驗式。
    /// 輸入乾球溫度（°C）與相對濕度（%），近海平面氣壓（約 1013 hPa）下誤差約 ±0.3~1°C。
    /// 適用範圍外（含 NaN 輸入）回傳 double.NaN，由呼叫端既有的 NaN→Quality Bad 機制接手。
    /// </summary>
    /// <param name="dTemp">乾球溫度（°C），適用 0 ~ 50</param>
    /// <param name="dRh">相對濕度（%），適用 5 ~ 100</param>
    /// <returns>濕球溫度（°C），範圍外回傳 NaN</returns>
    public static double WetBulbStull(double dTemp, double dRh)
    {
        if (double.IsNaN(dTemp) || double.IsNaN(dRh)) return double.NaN;
        if (dRh < MinRh || dRh > MaxRh) return double.NaN;
        if (dTemp < MinTemp || dTemp > MaxTemp) return double.NaN;

        var dTw = dTemp * Math.Atan(0.151977 * Math.Sqrt(dRh + 8.313659))
                + Math.Atan(dTemp + dRh)
                - Math.Atan(dRh - 1.676331)
                + 0.00391838 * Math.Pow(dRh, 1.5) * Math.Atan(0.023101 * dRh)
                - 4.686035;

        // 擬合誤差可能微幅高估（RH≈100 時 ≤ +0.2°C），物理上濕球不可能超過乾球
        return Math.Min(dTw, dTemp);
    }

    /// <summary>焓值/露點的相對濕度下限：焓值為解析式，RH=0（乾空氣）合法</summary>
    public const double MinRhEnthalpy = 0.0;
    /// <summary>露點的相對濕度下限：RH→0 時 ln 無定義，取 1%</summary>
    public const double MinRhDewPoint = 1.0;
    /// <summary>近海平面定壓 hPa（與 WetBulb 同假設）</summary>
    private const double PressureHpa = 1013.25;

    /// <summary>
    /// 濕空氣比焓 — ASHRAE 標準式 h = 1.006T + W(2501 + 1.86T)，W 由 Magnus 飽和壓推得。
    /// 單位 kJ/kg 乾空氣，近海平面定壓 1013.25 hPa。
    /// 適用範圍外（含 NaN 輸入）回傳 double.NaN，由呼叫端既有的 NaN→Quality Bad 機制接手。
    /// </summary>
    /// <param name="dTemp">乾球溫度（°C），適用 0 ~ 50</param>
    /// <param name="dRh">相對濕度（%），適用 0 ~ 100</param>
    /// <returns>比焓（kJ/kg 乾空氣），範圍外回傳 NaN</returns>
    public static double Enthalpy(double dTemp, double dRh)
    {
        if (double.IsNaN(dTemp) || double.IsNaN(dRh)) return double.NaN;
        if (dRh < MinRhEnthalpy || dRh > MaxRh) return double.NaN;
        if (dTemp < MinTemp || dTemp > MaxTemp) return double.NaN;

        var dPv = dRh / 100.0 * SaturationPressureMagnus(dTemp);
        var dW = 0.622 * dPv / (PressureHpa - dPv);
        return 1.006 * dTemp + dW * (2501.0 + 1.86 * dTemp);
    }

    /// <summary>
    /// 露點溫度 — Magnus 式反解：γ = ln(RH/100) + 17.62T/(243.12+T)，Td = 243.12γ/(17.62−γ)。
    /// 適用範圍外（含 NaN 輸入）回傳 double.NaN，由呼叫端既有的 NaN→Quality Bad 機制接手。
    /// </summary>
    /// <param name="dTemp">乾球溫度（°C），適用 0 ~ 50</param>
    /// <param name="dRh">相對濕度（%），適用 1 ~ 100</param>
    /// <returns>露點溫度（°C），範圍外回傳 NaN</returns>
    public static double DewPoint(double dTemp, double dRh)
    {
        if (double.IsNaN(dTemp) || double.IsNaN(dRh)) return double.NaN;
        if (dRh < MinRhDewPoint || dRh > MaxRh) return double.NaN;
        if (dTemp < MinTemp || dTemp > MaxTemp) return double.NaN;

        var dGamma = Math.Log(dRh / 100.0) + 17.62 * dTemp / (243.12 + dTemp);
        var dTd = 243.12 * dGamma / (17.62 - dGamma);

        // 浮點誤差下 RH=100 可能微幅高於乾球，物理上露點不可能超過乾球
        return Math.Min(dTd, dTemp);
    }

    /// <summary>Magnus 飽和水蒸氣壓（WMO 係數），單位 hPa</summary>
    private static double SaturationPressureMagnus(double dTemp)
        => 6.112 * Math.Exp(17.62 * dTemp / (243.12 + dTemp));
}
