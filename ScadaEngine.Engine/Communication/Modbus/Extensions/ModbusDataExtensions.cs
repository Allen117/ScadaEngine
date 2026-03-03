using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Extensions;

/// <summary>
/// Modbus 相關的即時資料模型擴展方法
/// </summary>
public static class ModbusDataExtensions
{
    /// <summary>
    /// 從 Modbus 點位模型建立即時資料實例
    /// </summary>
    /// <param name="tagModel">Modbus 點位模型</param>
    /// <param name="fRawValue">原始讀取數值</param>
    /// <param name="szQuality">資料品質狀態</param>
    /// <returns>即時資料實例</returns>
    public static RealtimeDataModel CreateFromModbusTag(Models.ModbusTagModel tagModel, float fRawValue, string szQuality = "Good")
    {
        return new RealtimeDataModel
        {
            szSID = tagModel.szSID,
            szTagName = tagModel.szName,
            fValue = fRawValue * tagModel.fRatio, // 套用縮放比例
            szUnit = tagModel.szUnit,
            dtTimestamp = DateTime.Now,
            szQuality = szQuality,
            nAddress = int.Parse(tagModel.szAddress)
        };
    }
}