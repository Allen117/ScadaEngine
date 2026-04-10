namespace ScadaEngine.Web.Features.LogicFlow.Models;

/// <summary>
/// LogicFlowTree 資料表對應模型
/// </summary>
public class LogicFlowTreeNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NodeType { get; set; } = "folder";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}
