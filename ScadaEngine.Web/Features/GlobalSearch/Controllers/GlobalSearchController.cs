using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.GlobalSearch.Controllers;

[Authorize]
[ApiController]
[Route("api/globalsearch")]
public class GlobalSearchController : Controller
{
    private readonly GlobalSearchService _search;

    public GlobalSearchController(GlobalSearchService search)
    {
        _search = search;
    }

    /// <summary>
    /// GET /api/globalsearch/index → 回傳登入者可見的頁面搜尋索引（雙語標題 + 關鍵字）。
    /// 權限在伺服器端過濾完才回傳，前端拿不到無權限頁面的存在資訊。
    /// </summary>
    [HttpGet("index")]
    public IActionResult GetIndex()
    {
        var aIndex = _search.GetIndexForUser(User);

        // 內容依使用者權限而異，僅允許私有快取
        Response.Headers.CacheControl = "private, max-age=300";

        return Json(new { entries = aIndex });
    }
}
