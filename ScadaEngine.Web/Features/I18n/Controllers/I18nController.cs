using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Services;
using System.Globalization;

namespace ScadaEngine.Web.Features.I18n.Controllers;

[AllowAnonymous]   // 登入頁也要能取字典
[ApiController]
[Route("api/i18n")]
public class I18nController : Controller
{
    private static readonly string[] _aSupported = new[] { "zh-TW", "en" };

    private readonly I18nResourceService _i18n;

    public I18nController(I18nResourceService i18n)
    {
        _i18n = i18n;
    }

    /// <summary>
    /// GET /api/i18n/{culture} → 回傳該語系完整 key/value 字典。
    /// 不支援的 culture 會 fallback 到 zh-TW。
    /// </summary>
    [HttpGet("{culture}")]
    public IActionResult GetDictionary(string culture)
    {
        var szCulture = _aSupported.Contains(culture, StringComparer.OrdinalIgnoreCase)
            ? culture
            : "zh-TW";

        var dict = _i18n.GetDictionary(szCulture);

        // 短時間快取（key 不常異動，但部署後不該舊版卡住）
        Response.Headers.CacheControl = "public, max-age=300";

        return Json(new
        {
            culture = szCulture,
            keys = dict
        });
    }

    /// <summary>
    /// POST /api/i18n/set-culture?culture=en&amp;returnUrl=/Realtime
    /// 寫 cookie，由前端 redirect / reload。
    /// </summary>
    [HttpPost("set-culture")]
    [IgnoreAntiforgeryToken]   // 純 cookie 操作、無敏感資料
    public IActionResult SetCulture([FromQuery] string culture, [FromQuery] string? returnUrl = null)
    {
        if (!_aSupported.Contains(culture, StringComparer.OrdinalIgnoreCase))
            culture = "zh-TW";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return Ok(new { culture });
    }
}
