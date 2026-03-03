using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.Login.Models;
using ScadaEngine.Engine.Data.Interfaces;
using System.Security.Claims;

namespace ScadaEngine.Web.Features.Login.Controllers;

/// <summary>
/// 登入控制器
/// </summary>
public class LoginController : Controller
{
    private readonly ILogger<LoginController> _logger;
    private readonly IDataRepository _dataRepository;

    // 預設帳密 (僅在 Users 資料表無任何使用者時生效)
    private const string DEFAULT_USERNAME = "ITRI";
    private const string DEFAULT_PASSWORD = "ITRI";

    public LoginController(ILogger<LoginController> logger, IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
    }

    /// <summary>
    /// 顯示登入頁面 (GET)
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/RealTime");

        // 若 Users 資料表無使用者，顯示預設帳密提示
        var userCount = await _dataRepository.GetUserCountAsync();
        ViewBag.ShowDefaultHint = userCount == 0;

        return View(new LoginModel());
    }

    /// <summary>
    /// 處理登入表單提交 (POST)
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(LoginModel loginModel)
    {
        var userCount = await _dataRepository.GetUserCountAsync();
        ViewBag.ShowDefaultHint = userCount == 0;

        if (!ModelState.IsValid)
            return View(loginModel);

        if (!loginModel.IsInputValid())
        {
            ModelState.AddModelError(string.Empty, "使用者名稱或密碼格式錯誤");
            loginModel.ClearSensitiveData();
            return View(loginModel);
        }

        bool isAuthenticated = await AuthenticateAsync(loginModel.szUserName!, loginModel.szPassword!, userCount);

        if (!isAuthenticated)
        {
            _logger.LogWarning("登入失敗：使用者={UserName}", loginModel.szUserName);
            ModelState.AddModelError(string.Empty, "使用者名稱或密碼錯誤");
            loginModel.ClearSensitiveData();
            return View(loginModel);
        }

        // 建立認證 Cookie
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, loginModel.szUserName!),
            new(ClaimTypes.NameIdentifier, loginModel.szUserName!),
            new("LoginTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = loginModel.isRememberMe,
            ExpiresUtc = loginModel.isRememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(4)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        _logger.LogInformation("使用者登入成功：{UserName}", loginModel.szUserName);

        loginModel.ClearSensitiveData();
        return Redirect("/RealTime");
    }

    /// <summary>
    /// 登出
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var szUserName = User.Identity?.Name ?? "未知使用者";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("使用者登出：{UserName}", szUserName);
        return RedirectToAction("Index", "Login");
    }

    /// <summary>
    /// 無權限存取
    /// </summary>
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        _logger.LogWarning("無權限存取嘗試，使用者={UserName}", User.Identity?.Name ?? "匿名");
        return View();
    }

    // -----------------------------------------------------------------------
    // 私有方法
    // -----------------------------------------------------------------------

    /// <summary>
    /// 驗證帳號密碼：
    ///   - 若 Users 資料表有使用者 → 比對 DB (SHA256 hash)
    ///   - 若 Users 資料表為空    → 使用預設帳密 ITRI/ITRI
    /// </summary>
    private async Task<bool> AuthenticateAsync(string szUsername, string szPassword, int userCount)
    {
        if (userCount == 0)
        {
            // 預設帳密 (DB 無使用者時)
            return string.Equals(szUsername, DEFAULT_USERNAME, StringComparison.OrdinalIgnoreCase)
                && szPassword == DEFAULT_PASSWORD;
        }

        return await _dataRepository.ValidateUserAsync(szUsername, szPassword);
    }
}
