using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Features.AccountSetting.Controllers;

[Authorize]
public class AccountSettingController : Controller
{
    private readonly IDataRepository _repository;

    public AccountSettingController(IDataRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("/AccountSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "帳號管理";
        var users = await _repository.GetAllUsersAsync();
        return View(users.ToList());
    }

    [HttpPost("/AccountSetting/Create")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { success = false, message = "帳號、密碼、角色為必填欄位" });
        }

        var user = new UserModel
        {
            szUsername = request.Username.Trim(),
            szRealName = request.RealName?.Trim() ?? "",
            szPasswordHash = request.Password,  // CreateUserAsync 內部會做 SHA256
            szRole = request.Role.Trim(),
            szDepartment = request.Department?.Trim() ?? "",
            isActive = request.IsActive
        };

        var isSuccess = await _repository.CreateUserAsync(user);
        if (isSuccess)
            return Ok(new { success = true });

        return StatusCode(500, new { success = false, message = "新增失敗，帳號可能已存在" });
    }

    public class CreateUserRequest
    {
        public string Username { get; set; } = "";
        public string? RealName { get; set; }
        public string Password { get; set; } = "";
        public string Role { get; set; } = "";
        public string? Department { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
