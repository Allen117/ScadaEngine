namespace ScadaEngine.Web.Features.EventLog.Models
{
    /// <summary>
    /// EventLog 查詢頁面 ViewModel
    /// </summary>
    public class EventLogQueryViewModel
    {
        public DateTime dtStartTime { get; set; } = DateTime.Now.AddDays(-7);
        public DateTime dtEndTime { get; set; } = DateTime.Now;
        public int? nEventType { get; set; }
        public int? nSeverity { get; set; }
        public string? szSID { get; set; }
        public int? nAcknowledged { get; set; }
    }
}
