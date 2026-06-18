namespace ScadaEngine.Web.Features.Designer.Models;

/// <summary>
/// Designer 表格列範本檔案內容 — 單一全域範本
/// </summary>
public class DesignerTemplateFileDto
{
    public string       szSeparator { get; set; } = "-";
    public List<string> arrRoles    { get; set; } = new() { "V", "A", "KW", "PF", "KWH" };
}
