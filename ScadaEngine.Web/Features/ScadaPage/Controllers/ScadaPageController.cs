using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

[Authorize]
public class ScadaPageController : Controller
{
    [HttpGet("/ScadaPage")]
    public IActionResult Index()
    {
        ViewData["Title"] = "即時監控";

        var isAdmin = PermissionService.IsAdmin(User);
        var permData = PermissionService.GetPermissionData(User);

        // 傳遞權限資料給前端 JS
        ViewData["IsAdmin"] = isAdmin;
        ViewData["ScadaPagePermissions"] = isAdmin
            ? "{}"
            : JsonSerializer.Serialize(permData.scadaPages);

        return View();
    }
}
