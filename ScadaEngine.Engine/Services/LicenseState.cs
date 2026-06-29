namespace ScadaEngine.Engine.Services;

/// <summary>
/// Singleton — 保存最新 HASP 授權驗證狀態，供各採集服務讀取。
/// </summary>
public class LicenseState
{
    private volatile bool _isValid = true;
    private DateTime _checkedAt = DateTime.MinValue;
    private string _reason = "";
    private readonly object _lock = new();

    public bool IsValid => _isValid;
    public DateTime CheckedAt => _checkedAt;
    public string Reason => _reason;

    public void Update(bool isValid, string reason = "")
    {
        lock (_lock)
        {
            _isValid = isValid;
            _checkedAt = DateTime.UtcNow;
            _reason = reason;
        }
    }
}
