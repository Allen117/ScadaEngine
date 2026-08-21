namespace ScadaEngine.Web.Features.Ems.Models;

/// <summary>
/// 空調水系統迴路冷量 API 回應（/EMS/api/chilled-water-rth，長條圖用）。
/// 與電/水/氣三組用量 API 回傳契約一致，前端 ems-circuit.js 不需區分。
/// </summary>
public class EmsChilledWaterRthDto
{
    public List<string> labels { get; set; } = new();
    public List<double> values { get; set; } = new();

    /// <summary>任一葉子在區間內 hourly 覆蓋率低於門檻（冰水主機資料不完整）</summary>
    public bool hasWarning { get; set; }
}
