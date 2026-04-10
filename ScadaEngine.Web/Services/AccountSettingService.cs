using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using System.Security.Claims;

namespace ScadaEngine.Web.Services;

public class AccountSettingService
{
    private readonly IDataRepository _repository;

    public AccountSettingService(IDataRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<UserModel>> GetAllUsersAsync()
    {
        var users = await _repository.GetAllUsersAsync();
        return users.ToList();
    }

    public async Task<(bool isSuccess, string szMessage)> CreateUserAsync(
        string szUsername, string? szRealName, string szPassword, string szRole,
        string? szDepartment, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(szUsername) ||
            string.IsNullOrWhiteSpace(szPassword) ||
            string.IsNullOrWhiteSpace(szRole))
        {
            return (false, "帳號、密碼、角色為必填欄位");
        }

        if (szRole != "Admin" && szRole != "User")
            return (false, "角色只能是 Admin 或 User");

        var user = new UserModel
        {
            szUsername = szUsername.Trim(),
            szRealName = szRealName?.Trim() ?? "",
            szPasswordHash = szPassword,  // CreateUserAsync 內部會做 SHA256
            szRole = szRole.Trim(),
            szDepartment = szDepartment?.Trim() ?? "",
            isActive = isActive
        };

        var isSuccess = await _repository.CreateUserAsync(user);
        return isSuccess
            ? (true, "")
            : (false, "新增失敗，帳號可能已存在");
    }

    public async Task<(bool isSuccess, string szMessage)> UpdateUserAsync(
        int nUserID, string? szRealName, string szRole, string? szDepartment,
        bool isActive, string? szPermissionJson)
    {
        if (nUserID <= 0)
            return (false, "無效的使用者 ID");

        if (szRole != "Admin" && szRole != "User")
            return (false, "角色只能是 Admin 或 User");

        var user = new UserModel
        {
            nUserID = nUserID,
            szRealName = szRealName?.Trim() ?? "",
            szRole = szRole.Trim(),
            szDepartment = szDepartment?.Trim() ?? "",
            isActive = isActive
        };

        var isSuccess = await _repository.UpdateUserAsync(user);
        if (!isSuccess)
            return (false, "更新失敗");

        // User 角色：儲存權限設定
        if (szRole == "User" && szPermissionJson != null)
        {
            await _repository.SaveUserPermissionsAsync(nUserID, szPermissionJson);
        }

        return (true, "");
    }

    /// <summary>
    /// 若修改的是目前登入者自己，重新發行認證 Cookie 讓導覽列即時反映權限變更
    /// </summary>
    public async Task RefreshAuthCookieIfSelfAsync(
        HttpContext httpContext, ClaimsPrincipal currentUser,
        int nEditedUserID, string szNewRole, string? szPermissionJson)
    {
        var szCurrentUsername = currentUser.Identity?.Name;
        if (string.IsNullOrEmpty(szCurrentUsername))
            return;

        var editedUser = await _repository.GetUserByUsernameAsync(szCurrentUsername);
        if (editedUser == null || editedUser.nUserID != nEditedUserID)
            return;

        var szPermJson = (szNewRole == "User" && szPermissionJson != null)
            ? szPermissionJson
            : "{}";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, szCurrentUsername),
            new(ClaimTypes.NameIdentifier, szCurrentUsername),
            new(ClaimTypes.Role, szNewRole),
            new("Permissions", szPermJson),
            new("LoginTime", currentUser.FindFirstValue("LoginTime")
                ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var existingAuth = await httpContext.AuthenticateAsync();
        var authProperties = existingAuth.Properties ?? new AuthenticationProperties();

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    public async Task<(bool isSuccess, string szMessage)> DeleteUserAsync(int nUserID)
    {
        if (nUserID <= 0)
            return (false, "無效的使用者 ID");

        var isSuccess = await _repository.DeleteUserAsync(nUserID);
        return isSuccess
            ? (true, "")
            : (false, "刪除失敗");
    }

    public async Task<string> GetPermissionJsonAsync(int nUserID)
    {
        var perm = await _repository.GetUserPermissionsAsync(nUserID);
        return perm?.szPermissionJson ?? "{}";
    }

    public async Task<string> GetScadaDesignPagesJsonAsync()
    {
        var pages = await _repository.LoadPublishedDesignAsync();
        return System.Text.Json.JsonSerializer.Serialize(
            pages.Select(p => new { p.szPageSid, p.szPageName, p.szParentPageSid }));
    }

    public string GetConfigurablePagesJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(
            PermissionService.ConfigurablePages.Select(p => new { route = p.Route, name = p.Name }));
    }
}
