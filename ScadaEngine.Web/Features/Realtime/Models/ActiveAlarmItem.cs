namespace ScadaEngine.Web.Features.Realtime.Models;

/// <summary>
/// 即時未恢復警報項目（記憶體快取 + 前端 API DTO）
/// </summary>
public class ActiveAlarmItem
{
    /// <summary>點位 SID</summary>
    public string szSID { get; set; } = string.Empty;

    /// <summary>警報類型：high / low / di</summary>
    public string szType { get; set; } = string.Empty;

    /// <summary>嚴重度（0=Critical 1=High 2=Medium 3=Low）</summary>
    public byte nSeverity { get; set; }

    /// <summary>警報訊息（人類可讀 fallback；舊資料或 messageKey 為 null 時直接顯示這個）</summary>
    public string szMessage { get; set; } = string.Empty;

    /// <summary>結構化訊息 i18n key，例 alarm.high_exceed；NULL 表示無結構化資訊</summary>
    public string? szMessageKey { get; set; }

    /// <summary>結構化訊息參數 JSON，例 {"name":"溫度","threshold":"85.0"}</summary>
    public string? szMessageArgs { get; set; }

    /// <summary>觸發時的數值</summary>
    public double? dTriggerValue { get; set; }

    /// <summary>門檻值</summary>
    public double? dThresholdValue { get; set; }

    /// <summary>觸發時間</summary>
    public DateTime dtOccurredAt { get; set; }

    /// <summary>是否已被操作員確認</summary>
    public bool isAcknowledged { get; set; }

    /// <summary>確認者帳號（未確認為 null）</summary>
    public string? szAcknowledgedBy { get; set; }

    /// <summary>快取 key（{szSID}:{szType}）</summary>
    public string CacheKey => $"{szSID}:{szType}";

    /// <summary>嚴重度文字</summary>
    public string SeverityLabel => nSeverity switch
    {
        0 => "Critical",
        1 => "High",
        2 => "Medium",
        3 => "Low",
        _ => "Unknown"
    };

    /// <summary>嚴重度色票（CLAUDE.md UI 規範）</summary>
    public string SeverityColor => nSeverity switch
    {
        0 => "#dc3545",
        1 => "#fd7e14",
        2 => "#ffc107",
        3 => "#6c757d",
        _ => "#6c757d"
    };
}
