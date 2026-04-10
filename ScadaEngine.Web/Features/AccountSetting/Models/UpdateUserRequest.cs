namespace ScadaEngine.Web.Features.AccountSetting.Models;

public class UpdateUserRequest
{
    public int UserID { get; set; }
    public string? RealName { get; set; }
    public string Role { get; set; } = "User";
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public string? PermissionJson { get; set; }
}

public class DeleteUserRequest
{
    public int UserID { get; set; }
}
