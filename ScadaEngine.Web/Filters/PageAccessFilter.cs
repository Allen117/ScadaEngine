using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Filters;

/// <summary>
/// 全域授權過濾器 — 檢查 User 角色的頁面存取權限
/// Admin 角色直接放行，User 角色依 Permissions Claim 檢查
/// </summary>
public class PageAccessFilter : IAsyncAuthorizationFilter
{
    /// <summary>
    /// 需要做權限檢查的路由集合（含 Admin 專用頁面）
    /// </summary>
    private static readonly HashSet<string> _protectedRoutes;

    static PageAccessFilter()
    {
        _protectedRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (route, _) in PermissionService.ConfigurablePages)
        {
            _protectedRoutes.Add(route);
        }
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // 未認證 → 交給 [Authorize] 處理
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        // Admin 全部放行
        if (PermissionService.IsAdmin(user))
            return Task.CompletedTask;

        var szPath = context.HttpContext.Request.Path.Value ?? "";

        // 只檢查受保護的頁面路由
        if (!_protectedRoutes.Contains(szPath))
            return Task.CompletedTask;

        // User 角色：檢查權限
        if (!PermissionService.CanAccessPage(user, szPath))
        {
            context.Result = new RedirectResult("/Login/AccessDenied");
        }

        return Task.CompletedTask;
    }
}
