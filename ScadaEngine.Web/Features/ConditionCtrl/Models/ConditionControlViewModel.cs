using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Web.Features.ConditionCtrl.Models;

/// <summary>
/// 條件控制頁面的視圖模型
/// </summary>
public class ConditionControlViewModel
{
    /// <summary>所有 Modbus Coordinator 設備清單</summary>
    public List<CoordinatorModel> CoordinatorList { get; set; } = new();

    /// <summary>所有 DB 來源 Coordinator 設備清單</summary>
    public List<DbCoordinatorModel> DbCoordinatorList { get; set; } = new();

    /// <summary>所有點位清單（含 Modbus / 計算點位 / DB 來源）</summary>
    public List<ModbusPointModel> PointList { get; set; } = new();

    /// <summary>資料庫中已存在的條件控制規則</summary>
    public List<ConditionControlRuleModel> ExistingRules { get; set; } = new();
}
