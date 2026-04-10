namespace ScadaEngine.Web.Features.LogicFlow.Models;

/// <summary>
/// LogicFlowDiagram 資料表對應模型
/// </summary>
public class LogicFlowDiagramDto
{
    public int TreeId { get; set; }
    public string? DiagramJson { get; set; }
    public int Version { get; set; }
}
