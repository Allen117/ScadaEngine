using System.ComponentModel.DataAnnotations;

namespace ScadaEngine.Web.Features.Login.Models;

/// <summary>
/// 登入模型 - 採用肥 Model 設計，包含驗證邏輯
/// </summary>
public class LoginModel
{
    /// <summary>
    /// 使用者名稱 (匈牙利命名法: sz = string)
    /// </summary>
    [Required(ErrorMessage = "請輸入使用者名稱")]
    [Display(Name = "使用者名稱")]
    public string szUserName { get; set; } = string.Empty;

    /// <summary>
    /// 密碼 (匈牙利命名法: sz = string)
    /// </summary>
    [Required(ErrorMessage = "請輸入密碼")]
    [Display(Name = "密碼")]
    [DataType(DataType.Password)]
    public string szPassword { get; set; } = string.Empty;

    /// <summary>
    /// 記住我選項 (匈牙利命名法: is = bool)
    /// </summary>
    [Display(Name = "記住我")]
    public bool isRememberMe { get; set; } = false;

    /// <summary>
    /// 清除敏感資料 (安全考量)
    /// </summary>
    public void ClearSensitiveData()
    {
        szPassword = string.Empty;
    }

    /// <summary>
    /// 檢查輸入資料的完整性 (只確認欄位非空)
    /// </summary>
    /// <returns>資料有效回傳 true</returns>
    public bool IsInputValid()
    {
        return !string.IsNullOrWhiteSpace(szUserName) && !string.IsNullOrWhiteSpace(szPassword);
    }
}