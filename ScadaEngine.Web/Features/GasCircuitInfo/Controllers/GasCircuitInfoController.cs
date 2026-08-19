using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.GasCircuitInfo.Controllers;

[Authorize]
public class GasCircuitInfoController : Controller
{
    [HttpGet("/GasCircuitInfo")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/GasCircuitInfo"))
            return Redirect("/EMS");

        return View();
    }
}
