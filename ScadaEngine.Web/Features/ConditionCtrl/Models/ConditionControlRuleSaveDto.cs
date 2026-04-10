namespace ScadaEngine.Web.Features.ConditionCtrl.Models;

/// <summary>
/// 儲存條件控制規則的 API 請求 DTO
/// </summary>
public class ConditionControlRuleSaveDto
{
    public string ConditionPointSID { get; set; } = string.Empty;
    /// <summary>0=&gt; 1=&lt; 2=&gt;= 3=&lt;= 4=== 5=!=</summary>
    public byte Operator { get; set; }
    public double ConditionValue { get; set; }
    public string ControlPointSID { get; set; } = string.Empty;
    public double ControlValue { get; set; }
    public string? Remarks { get; set; }
}
