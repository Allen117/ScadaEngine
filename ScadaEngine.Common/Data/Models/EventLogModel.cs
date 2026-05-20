namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// EventLog 資料表對應模型（Dapper 使用 SQL alias 對應）
    /// </summary>
    public class EventLogModel
    {
        public long nId { get; set; }
        public string szSID { get; set; } = string.Empty;
        public byte nEventType { get; set; }       // 0=Alarm 1=Fault 2=Warning 3=Info 4=System
        public byte nSeverity { get; set; }         // 0=緊急 1=高 2=中 3=低 4=無（資訊類事件用）
        public double? dTriggerValue { get; set; }
        public double? dThresholdValue { get; set; }
        public byte? nOperator { get; set; }        // 0=> 1=< 2=>= 3=<= 4=== 5=!=
        public string szMessage { get; set; } = string.Empty;
        // 結構化訊息：Engine 寫 key + args，Web 顯示時依使用者 culture 翻譯。舊資料為 NULL，fallback 用 szMessage。
        public string? szMessageKey { get; set; }
        public string? szMessageArgs { get; set; }
        public DateTime dtOccurredAt { get; set; }
        public DateTime? dtClearedAt { get; set; }
        public bool isAcknowledged { get; set; }
        public string? szAcknowledgedBy { get; set; }
        public DateTime? dtAcknowledgedAt { get; set; }
        public string? szRemarks { get; set; }
    }
}
