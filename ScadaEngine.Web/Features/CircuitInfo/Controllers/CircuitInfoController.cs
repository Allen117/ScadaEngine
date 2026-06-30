using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.CircuitInfo.Controllers;

[Authorize]
public class CircuitInfoController : Controller
{
    [HttpGet("/CircuitInfo")]
    public IActionResult Index()
    {
        if (!PermissionService.CanAccessPage(User, "/CircuitInfo"))
            return Redirect("/EMS");

        return View();
    }
}
