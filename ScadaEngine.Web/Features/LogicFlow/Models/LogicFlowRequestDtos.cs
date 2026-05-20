namespace ScadaEngine.Web.Features.LogicFlow.Models;

/// <summary>新增節點請求</summary>
public class CreateNodeDto
{
    public int? ParentId { get; set; }
    public string Name { get; set; } = "新項目";
    public string NodeType { get; set; } = "folder";
    public int SortOrder { get; set; }
}

/// <summary>重新命名請求</summary>
public class RenameNodeDto
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>切換啟用/停用請求</summary>
public class ToggleEnabledDto
{
    public bool IsEnabled { get; set; }
}

/// <summary>排序更新請求</summary>
public class SortOrderDto
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>儲存流程圖請求</summary>
public class SaveDiagramDto
{
    public string DiagramJson { get; set; } = "{}";
    public int Version { get; set; }
}

/// <summary>演算法預覽呼叫請求（variadic 演算法可帶 n）</summary>
public class AlgoEvalRequest
{
    public Dictionary<string, double>? Inputs { get; set; }
    public int? N { get; set; }
}
