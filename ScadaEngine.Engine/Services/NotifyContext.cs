namespace ScadaEngine.Engine.Services;

/// <summary>
/// 通知派送上下文 — Line / Email 共用。
/// AlarmMonitorService 觸發 / 恢復時建構此物件並傳給各通道 service，
/// 各 service 依群組 Language 翻譯訊息。
/// </summary>
public class NotifyContext
{
    /// <summary>嚴重度：0=緊急 1=高 2=中 3=低</summary>
    public byte nSeverity { get; set; }

    /// <summary>觸發點位 SID（用於 EventLog 摘要關聯）</summary>
    public string szSID { get; set; } = string.Empty;

    /// <summary>點位顯示名稱（已從 rule.szPointName / data.szTagName 取得，避免通知服務再查 DB）</summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>結構化訊息 key，例如 "alarm.high_exceed"，由 NotificationLocalizer 翻譯</summary>
    public string szMessageKey { get; set; } = string.Empty;

    /// <summary>訊息參數，例如 { ["name"]="溫度", ["threshold"]="85.0" }</summary>
    public IDictionary<string, string?> args { get; set; } = new Dictionary<string, string?>();

    /// <summary>觸發 / 恢復時間</summary>
    public DateTime dtTime { get; set; }

    /// <summary>關聯到的 alarm EventLog.Id（通知摘要的 NotifyRelatedEventId）。新 alarm 寫入後填回</summary>
    public long nRelatedEventId { get; set; }

    /// <summary>對應到該警報的 AlarmRules.Id（Email 群組-規則對應需要）</summary>
    public int nAlarmRuleId { get; set; }
}
