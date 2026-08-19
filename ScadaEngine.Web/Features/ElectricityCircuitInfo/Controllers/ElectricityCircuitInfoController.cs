using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ElectricityCircuitInfo.Controllers;

[Authorize]
public class ElectricityCircuitInfoController : Controller
{
    [HttpGet("/ElectricityCircuitInfo")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/ElectricityCircuitInfo"))
            return Redirect("/EMS");

        return View();
    }
}
