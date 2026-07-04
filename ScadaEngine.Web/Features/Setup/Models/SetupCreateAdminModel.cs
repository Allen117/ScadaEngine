using System.ComponentModel.DataAnnotations;

namespace ScadaEngine.Web.Features.Setup.Models;

/// <summary>
/// First-run 建立首組管理者的表單模型
/// </summary>
public class SetupCreateAdminModel
{
    [Required]
    public string? szUsername { get; set; }

    public string? szRealName { get; set; }

    [Required]
    public string? szPassword { get; set; }

    [Required]
    public string? szConfirmPassword { get; set; }
}
