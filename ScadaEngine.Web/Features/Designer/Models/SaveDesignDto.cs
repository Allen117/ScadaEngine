using ScadaEngine.Engine.Models;

namespace ScadaEngine.Web.Features.Designer.Models;

/// <summary>POST /Designer/Save 的請求格式</summary>
public class SaveDesignDto
{
    public string                    szName { get; set; } = "未命名設計";
    public List<ScadaDesignPageModel> pages { get; set; } = new();
}
