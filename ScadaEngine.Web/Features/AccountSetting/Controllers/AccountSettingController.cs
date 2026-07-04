using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.AccountSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.AccountSetting.Controllers;

[Authorize(Roles = "Admin")]
public class AccountSettingController : Controller
{
    private readonly AccountSettingService _service;

    public AccountSettingController(AccountSettingService service)
    {
        _service = service;
    }

    [HttpGet("/AccountSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "帳號管理";
        ViewData["ScadaDesignPages"] = await _service.GetScadaDesignPagesJsonAsync();
        ViewData["ConfigurablePages"] = _service.GetConfigurablePagesJson();

        var users = await _service.GetAllUsersAsync();
        return View(users);
    }

    [HttpPost("/AccountSetting/Create")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "無效的請求" });

        var (isSuccess, szMessage) = await _service.CreateUserAsync(
            request.Username, request.RealName, request.Password,
            request.Role, request.Department, request.IsActive);

        return isSuccess
            ? Ok(new { success = true })
            : BadRequest(new { success = false, message = szMessage });
    }

    [HttpPost("/AccountSetting/Update")]
    public async Task<IActionResult> Update([FromBody] UpdateUserRequest request)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "無效的請求" });

        var (isSuccess, szMessage) = await _service.UpdateUserAsync(
            request.UserID, request.RealName, request.Role,
            request.Department, request.IsActive, request.PermissionJson);

        if (!isSuccess)
            return StatusCode(500, new { success = false, message = szMessage });

        // 若修改的是自己，重新發行認證 Cookie
        await _service.RefreshAuthCookieIfSelfAsync(
            HttpContext, User, request.UserID, request.Role, request.PermissionJson);

        return Ok(new { success = true });
    }

    [HttpPost("/AccountSetting/Delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteUserRequest request)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "無效的請求" });

        var (isSuccess, szMessage) = await _service.DeleteUserAsync(request.UserID);

        return isSuccess
            ? Ok(new { success = true })
            : StatusCode(500, new { success = false, message = szMessage });
    }

    [HttpGet("/AccountSetting/GetPermissions/{nUserID}")]
    public async Task<IActionResult> GetPermissions(int nUserID)
    {
        var szPermissionJson = await _service.GetPermissionJsonAsync(nUserID);
        return Ok(new { permissionJson = szPermissionJson });
    }
}
