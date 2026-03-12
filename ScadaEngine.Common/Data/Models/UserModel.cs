namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 使用者帳號模型，對應 Users 資料表
/// </summary>
public class UserModel
{
    public int nUserID { get; set; }
    public string szUsername { get; set; } = string.Empty;
    public string szRealName { get; set; } = string.Empty;
    public string szPasswordHash { get; set; } = string.Empty;
    public string szRole { get; set; } = string.Empty;
    public string szDepartment { get; set; } = string.Empty;
    public bool isActive { get; set; } = true;
    public DateTime? dtLastLoginAt { get; set; }
    public DateTime? dtCreatedAt { get; set; }
    public DateTime? dtUpdatedAt { get; set; }
}
