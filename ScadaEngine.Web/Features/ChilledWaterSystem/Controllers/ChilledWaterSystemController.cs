using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScadaEngine.Web.Features.ChilledWaterSystem.Controllers
{
    [Authorize]
    public class ChilledWaterSystemController : Controller
    {
        /// <summary>
        /// GET /ChilledWaterSystem/Efficiency — 水系統效率管理頁面
        /// </summary>
        [HttpGet]
        public IActionResult Efficiency()
        {
            return View();
        }
    }
}
