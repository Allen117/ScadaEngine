using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.WaterCircuitInfo.Controllers;

[Authorize]
public class WaterCircuitInfoController : Controller
{
    [HttpGet("/WaterCircuitInfo")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/WaterCircuitInfo"))
            return Redirect("/EMS");

        return View();
    }
}
