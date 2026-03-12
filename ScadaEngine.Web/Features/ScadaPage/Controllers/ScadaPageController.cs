using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

[Authorize]
public class ScadaPageController : Controller
{
    [HttpGet("/ScadaPage")]
    public IActionResult Index()
    {
        ViewData["Title"] = "即時監控";
        return View();
    }
}
