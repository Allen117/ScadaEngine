using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Web.Features.ControlAlgorithm.Models;

/// <summary>
/// 條件控制頁面的視圖模型
/// </summary>
public class ConditionControlViewModel
{
    /// <summary>所有 Coordinator 設備清單</summary>
    public List<CoordinatorModel> CoordinatorList { get; set; } = new();

    /// <summary>所有 Modbus 點位清單</summary>
    public List<ModbusPointModel> PointList { get; set; } = new();

    /// <summary>資料庫中已存在的條件控制規則</summary>
    public List<ConditionControlRuleModel> ExistingRules { get; set; } = new();
}

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
