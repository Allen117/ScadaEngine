namespace ScadaEngine.Web.Features.ScheduleSetting.Models;

/// <summary>
/// 時間排程 — 對應 TimeSchedules 資料表（Dapper 映射用，需 SQL alias）
/// </summary>
public class TimeScheduleModel
{
    public int nId { get; set; }
    public string szName { get; set; } = string.Empty;
    public byte nRecurrenceType { get; set; }
    public int? nRunLength { get; set; }
    public int? nRestLength { get; set; }
    public DateTime? dtAnchorDateTime { get; set; }
    public string? szDaysOfWeek { get; set; }
    public string? szDaysOfMonth { get; set; }
    public string szStartTime { get; set; } = string.Empty;
    public string szEndTime { get; set; } = string.Empty;
    public string? szExcludeDates { get; set; }
    public string? szIncludeDates { get; set; }
    public bool isEnabled { get; set; } = true;
    public string? szRemarks { get; set; }
    public DateTime? dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
