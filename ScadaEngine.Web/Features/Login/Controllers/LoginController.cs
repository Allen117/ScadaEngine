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

    // 預設帳密 (僅在 DB 無任何啟用的 Admin 帳號時生效)
    private const string DEFAULT_USERNAME = "admin";
    private const string DEFAULT_PASSWORD = "admin";

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
            return Redirect("/ScadaPage");

        // 若 DB 無啟用的 Admin 帳號，顯示預設帳密提示
        var nAdminCount = await _dataRepository.GetAdminCountAsync();
        ViewBag.ShowDefaultHint = nAdminCount == 0;

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
        var nAdminCount = await _dataRepository.GetAdminCountAsync();
        ViewBag.ShowDefaultHint = nAdminCount == 0;

        if (!ModelState.IsValid)
            return View(loginModel);

        if (!loginModel.IsInputValid())
        {
            ModelState.AddModelError(string.Empty, "使用者名稱或密碼格式錯誤");
            loginModel.ClearSensitiveData();
            return View(loginModel);
        }

        // 先嘗試 DB 驗證
        bool isDbAuth = await _dataRepository.ValidateUserAsync(loginModel.szUserName!, loginModel.szPassword!);

        // DB 驗證失敗時，若無 Admin 帳號則嘗試預設帳密
        bool isDefaultAuth = false;
        if (!isDbAuth && nAdminCount == 0)
        {
            isDefaultAuth = string.Equals(loginModel.szUserName, DEFAULT_USERNAME, StringComparison.OrdinalIgnoreCase)
                && loginModel.szPassword == DEFAULT_PASSWORD;
        }

        if (!isDbAuth && !isDefaultAuth)
        {
            _logger.LogWarning("登入失敗：使用者={UserName}", loginModel.szUserName);
            ModelState.AddModelError(string.Empty, "使用者名稱或密碼錯誤");
            loginModel.ClearSensitiveData();
            return View(loginModel);
        }

        // 取得使用者角色資訊
        string szRole = "Admin"; // 預設帳號為 Admin
        string szPermissionJson = "{}";

        if (isDbAuth)
        {
            var dbUser = await _dataRepository.GetUserByUsernameAsync(loginModel.szUserName!);
            if (dbUser != null)
            {
                szRole = dbUser.szRole;

                // User 角色需載入權限設定
                if (szRole == "User")
                {
                    var perm = await _dataRepository.GetUserPermissionsAsync(dbUser.nUserID);
                    szPermissionJson = perm?.szPermissionJson ?? "{}";
                }
            }
        }

        // 建立認證 Cookie
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, loginModel.szUserName!),
            new(ClaimTypes.NameIdentifier, loginModel.szUserName!),
            new(ClaimTypes.Role, szRole),
            new("Permissions", szPermissionJson),
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

        // 更新最後登入時間至 DB（僅 DB 帳號）
        if (isDbAuth)
        {
            await _dataRepository.UpdateLastLoginAsync(loginModel.szUserName!);
        }

        _logger.LogInformation("使用者登入成功：{UserName}（{AuthType}）", loginModel.szUserName,
            isDefaultAuth ? "預設帳號" : "DB帳號");

        loginModel.ClearSensitiveData();
        return Redirect("/ScadaPage");
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
}
