using System.Text.Json;

namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 使用者權限模型，對應 UserPermissions 資料表
/// </summary>
public class UserPermissionModel
{
    public int nUserID { get; set; }
    public string szPermissionJson { get; set; } = "{}";
    public DateTime? dtUpdatedAt { get; set; }

    /// <summary>
    /// 將 JSON 字串反序列化為 PermissionData
    /// </summary>
    public PermissionData ToPermissionData()
    {
        try
        {
            return JsonSerializer.Deserialize<PermissionData>(szPermissionJson) ?? new PermissionData();
        }
        catch
        {
            return new PermissionData();
        }
    }
}

/// <summary>
/// 權限資料結構（JSON 反序列化用）
/// </summary>
public class PermissionData
{
    /// <summary>
    /// 可存取的主頁面路由清單，例如 ["/ScadaPage", "/RealTime"]
    /// </summary>
    public List<string> pages { get; set; } = new();

    /// <summary>
    /// ScadaPage 子頁面權限，key = PageSid，例如 "p1"
    /// </summary>
    public Dictionary<string, ScadaPagePermission> scadaPages { get; set; } = new();

    /// <summary>
    /// 序列化為 JSON 字串
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }
}

/// <summary>
/// ScadaPage 單一子頁面的權限設定
/// </summary>
public class ScadaPagePermission
{
    public bool canView { get; set; }
    public bool canControl { get; set; }
}
