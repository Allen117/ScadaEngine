namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 智慧助理「區間效率分析」結果 — 某份能源申報項目在任意 [起, 訖) 區間的
/// 總用電（kWh）、總冷量（RT·h）與平均效率（kWh/RT·h），外加規則式分級碼。
/// 由 EnergyDeclarationService.GetIntervalEfficiencyAsync 產出，
/// 供右下角對話窗逐句吐字。數值口徑重用 EnergyReportService / RefrigerationTonReportService，
/// 與各自單獨報表查詢結果一致。分級句子由前端依 <see cref="szVerdictCode"/> 經 i18n 組出（利於雙語）。
/// </summary>
public class IntervalEfficiencyResult
{
    /// <summary>申報報表設定 Id</summary>
    public int nReportId { get; set; }

    /// <summary>申報報表名稱（顯示用）</summary>
    public string szReportName { get; set; } = string.Empty;

    /// <summary>用電迴路名稱（顯示用；迴路已刪除時為空）</summary>
    public string szEnergyCircuitName { get; set; } = string.Empty;

    /// <summary>冷凍噸迴路名稱（顯示用；迴路已刪除時為空）</summary>
    public string szWaterCircuitName { get; set; } = string.Empty;

    /// <summary>查詢起點（含，= 起始日 00:00）</summary>
    public DateTime dtStart { get; set; }

    /// <summary>查詢終點（exclusive，= 結束日次日 00:00）</summary>
    public DateTime dtEnd { get; set; }

    /// <summary>區間總用電量（kWh）</summary>
    public double dTotalKwh { get; set; }

    /// <summary>區間總冷量（RT·h）</summary>
    public double dTotalRtHour { get; set; }

    /// <summary>平均效率 = 總 kWh ÷ 總 RT·h；RT·h ≤ 0 時為 null（前端顯示資料不足）</summary>
    public double? dEfficiency { get; set; }

    /// <summary>規則式分級碼：good / normal / poor / insufficient（前端據此組評語句子）</summary>
    public string szVerdictCode { get; set; } = "insufficient";

    /// <summary>用電或冷凍噸側資料覆蓋率不足 → true（結果僅供參考，前端加註警語）</summary>
    public bool isStaleWarning { get; set; }

    /// <summary>
    /// 錯誤碼（非 null 時前端顯示友善提示、不吐數字）：
    /// circuit_deleted = 綁定迴路已被刪除。null 表示正常。
    /// </summary>
    public string? szErrorCode { get; set; }
}
