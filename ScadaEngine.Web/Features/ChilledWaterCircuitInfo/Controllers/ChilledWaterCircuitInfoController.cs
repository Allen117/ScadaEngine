using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ChilledWaterCircuitInfo.Controllers;

[Authorize]
public class ChilledWaterCircuitInfoController : Controller
{
    [HttpGet("/ChilledWaterCircuitInfo")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/ChilledWaterCircuitInfo"))
            return Redirect("/EMS");

        return View();
    }
}
