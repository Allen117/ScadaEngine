namespace ScadaEngine.Web.Features.AccountSetting.Models;

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string? RealName { get; set; }
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
}
