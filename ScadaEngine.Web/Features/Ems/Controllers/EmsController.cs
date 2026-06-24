using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Ems.Controllers;

/// <summary>
/// 能源管理 Hub — /EMS 進入點
/// </summary>
[Authorize]
public class EmsController : Controller
{
    [HttpGet("/EMS")]
    public IActionResult Index()
    {
        if (!PermissionService.IsAdmin(User))
        {
            bool hasAny = PermissionService.EmsRoutes
                .Where(r => !string.Equals(r, "/EMS", StringComparison.OrdinalIgnoreCase))
                .Any(r => PermissionService.CanAccessPage(User, r));
            if (!hasAny)
                return Redirect("/ScadaPage");
        }

        return View();
    }
}
