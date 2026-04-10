namespace ScadaEngine.Web.Features.CalcPoint.Models;

public class CreateCalcPointRequest
{
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public string? GroupName { get; set; }
    public string Formula { get; set; } = "";
    public string InputMappings { get; set; } = "{}";
}

public class UpdateCalcPointRequest
{
    public string SID { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public string? GroupName { get; set; }
    public string Formula { get; set; } = "";
    public string InputMappings { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
}

public class DeleteCalcPointRequest
{
    public string SID { get; set; } = "";
}

public class PreviewCalcPointRequest
{
    public string Formula { get; set; } = "";
    public string InputMappings { get; set; } = "{}";
}
