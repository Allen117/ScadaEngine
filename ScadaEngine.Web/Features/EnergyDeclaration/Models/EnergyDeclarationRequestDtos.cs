namespace ScadaEngine.Web.Features.EnergyDeclaration.Models;

/// <summary>新增/更新申報報表設定</summary>
public class SaveDeclarationReportDto
{
    public string name { get; set; } = string.Empty;

    /// <summary>用電度數來源 — EnergyCircuit.Id（必填）</summary>
    public int energyCircuitId { get; set; }

    /// <summary>冷凍噸數來源 — WaterCircuit.Id（必填，與用電成對）</summary>
    public int waterCircuitId { get; set; }

    public string? description { get; set; }
}

/// <summary>申報報表查詢/匯出條件 — 頁面固定格式：只選年度，產出該年 12 個曆月</summary>
public class EnergyDeclarationQueryDto
{
    /// <summary>申報報表設定 Id</summary>
    public int reportId { get; set; }

    /// <summary>申報年度（該年 1/1 ~ 12/31，每月 1 號切界）</summary>
    public int year { get; set; }
}
