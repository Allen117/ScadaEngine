using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Services;
using System.Security.Claims;

namespace ScadaEngine.Web.Features.Account.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly IDataRepository _repository;

    public AccountController(IDataRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("/Account/Profile")]
    public async Task<IActionResult> Profile()
    {
        ViewData["Title"] = "個人資料";

        var szUsername = User.Identity?.Name;
        if (string.IsNullOrEmpty(szUsername))
            return RedirectToAction("Index", "Login");

        var user = await _repository.GetUserByUsernameAsync(szUsername);
        if (user == null)
        {
            // 預設帳號（DB 無記錄）→ 從 Claims 建構基本資料
            var szLoginTime = User.FindFirstValue("LoginTime");
            user = new UserModel
            {
                szUsername = szUsername,
                szRealName = szUsername,
                szRole = User.FindFirstValue(ClaimTypes.Role) ?? "Admin",
                isActive = true,
                dtLastLoginAt = DateTime.TryParse(szLoginTime, out var dtLogin) ? dtLogin : null
            };
        }

        // User 角色需載入權限資料
        string szPermissionJson = "{}";
        if (user.szRole == "User")
        {
            var perm = await _repository.GetUserPermissionsAsync(user.nUserID);
            szPermissionJson = perm?.szPermissionJson ?? "{}";
        }
        ViewData["PermissionJson"] = szPermissionJson;

        // 可配置頁面清單 & ScadaDesign 頁面清單（供 JS 渲染權限）
        ViewData["ConfigurablePages"] = System.Text.Json.JsonSerializer.Serialize(
            PermissionService.ConfigurablePages.Select(p => new { route = p.Route, name = p.Name }));

        var pages = await _repository.LoadPublishedDesignAsync();
        ViewData["ScadaDesignPages"] = System.Text.Json.JsonSerializer.Serialize(
            pages.Select(p => new { p.szPageSid, p.szPageName, p.szParentPageSid }));

        return View(user);
    }
}
