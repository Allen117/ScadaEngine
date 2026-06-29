namespace ScadaEngine.Web.Services;

/// <summary>
/// Singleton — 快取最新 HASP 授權狀態（由 MqttRealtimeSubscriberService 更新）。
/// 初始值 valid=true，直到第一次 MQTT 訊息抵達後才反映真實狀態。
/// </summary>
public class LicenseStatusCache
{
    private bool _isValid = true;
    private DateTime _checkedAt = DateTime.MinValue;
    private string _reason = "";
    private readonly object _lock = new();

    public bool IsValid
    {
        get { lock (_lock) return _isValid; }
    }

    public DateTime CheckedAt
    {
        get { lock (_lock) return _checkedAt; }
    }

    public string Reason
    {
        get { lock (_lock) return _reason; }
    }

    public void Update(bool isValid, DateTime checkedAt, string reason)
    {
        lock (_lock)
        {
            _isValid = isValid;
            _checkedAt = checkedAt;
            _reason = reason;
        }
    }
}
