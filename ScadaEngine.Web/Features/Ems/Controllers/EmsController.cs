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
    private static readonly string[] _aEnergySubPages =
    [
        "/ChilledWaterSystem",
        "/EnergyMeter",
        "/EnergyReport",
        "/RefrigerationTonReport",
    ];

    [HttpGet("/EMS")]
    public IActionResult Index()
    {
        if (!PermissionService.IsAdmin(User))
        {
            bool hasAny = _aEnergySubPages.Any(route => PermissionService.CanAccessPage(User, route));
            if (!hasAny)
                return Redirect("/ScadaPage");
        }

        ViewData["EmsMode"] = true;
        return View();
    }
}
