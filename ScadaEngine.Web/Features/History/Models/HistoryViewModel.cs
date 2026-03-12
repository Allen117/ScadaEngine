using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Web.Features.History.Models;

/// <summary>
/// 歷史趨勢頁面的視圖模型
/// </summary>
public class HistoryTrendViewModel
{
    /// <summary>所有 Coordinator 設備清單（左側邊欄）</summary>
    public List<CoordinatorModel> CoordinatorList { get; set; } = new();

    /// <summary>所有點位清單（依 SID 排序）</summary>
    public List<ModbusPointModel> PointList { get; set; } = new();

    /// <summary>預設查詢起始時間（24 小時前）</summary>
    public DateTime dtStartTime { get; set; } = DateTime.Now.AddHours(-24);

    /// <summary>預設查詢結束時間（現在）</summary>
    public DateTime dtEndTime { get; set; } = DateTime.Now;
}
