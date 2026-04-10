namespace ScadaEngine.Web.Features.EventLog.Models
{
    /// <summary>
    /// 前端觸發警報時送出的 DTO
    /// </summary>
    public class EventLogTriggerDto
    {
        public string sid { get; set; } = string.Empty;
        public int eventType { get; set; }          // 0=Alarm
        public int severity { get; set; }           // 0~3
        public double? triggerValue { get; set; }
        public double? thresholdValue { get; set; }
        public int? operatorType { get; set; }      // 0=> 1=<...
        public string message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 前端警報恢復時送出的 DTO
    /// </summary>
    public class EventLogClearDto
    {
        public string sid { get; set; } = string.Empty;
    }
}
