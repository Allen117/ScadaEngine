namespace ScadaEngine.Web.Features.ScheduleSetting.Models;

/// <summary>新增/編輯排程 DTO</summary>
public class ScheduleSaveDto
{
    public int? id { get; set; }
    public string name { get; set; } = string.Empty;
    public byte recurrenceType { get; set; }
    public int? runLength { get; set; }
    public int? restLength { get; set; }
    public string? anchorDateTime { get; set; }
    public string? daysOfWeek { get; set; }
    public string? daysOfMonth { get; set; }
    public string startTime { get; set; } = string.Empty;
    public string endTime { get; set; } = string.Empty;
    public bool isEnabled { get; set; } = true;
    public string? remarks { get; set; }
}
