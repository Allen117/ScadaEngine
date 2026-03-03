using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScadaEngine.Web.Features.ControlAlgorithm.Controllers;

[Authorize]
[Route("[controller]")]
public class ControlAlgorithmController : Controller
{
    [HttpGet("/ControlAlgorithm")]
    public IActionResult Index()
    {
        ViewData["Title"] = "控制邏輯";
        return View();
    }
}
