namespace ScadaEngine.Web.Services;

/// <summary>
/// Singleton — 快取最新簡訊盒狀態（由 MqttRealtimeSubscriberService 訂閱
/// SCADA/Sys/Sms/Status retained 訊息更新）。
/// 儲存原始 JSON payload，controller 直接透傳給前端呈現，欄位異動免改 Web。
/// </summary>
public class SmsStatusCache
{
    private string _szPayloadJson = string.Empty;
    private DateTime _dtReceivedAt = DateTime.MinValue;
    private readonly object _lock = new();

    /// <summary>最新狀態 JSON；尚未收到任何訊息時為空字串</summary>
    public string PayloadJson
    {
        get { lock (_lock) return _szPayloadJson; }
    }

    public DateTime ReceivedAt
    {
        get { lock (_lock) return _dtReceivedAt; }
    }

    public void Update(string szPayloadJson)
    {
        lock (_lock)
        {
            _szPayloadJson = szPayloadJson ?? string.Empty;
            _dtReceivedAt = DateTime.Now;
        }
    }
}
