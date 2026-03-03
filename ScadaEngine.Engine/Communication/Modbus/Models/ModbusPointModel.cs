using System.ComponentModel.DataAnnotations;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Models;

/// <summary>
/// Modbus 點位模型 (對應 ModbusPoints 資料表)
/// </summary>
public class ModbusPointModel
{
    /// <summary>
    /// 點位唯一識別碼 (主鍵)
    /// </summary>
    [Key]
    [Required]
    [StringLength(100)]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 點位名稱
    /// </summary>
    [Required]
    [StringLength(100)]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// Modbus 暫存器地址 (原始 JSON 格式)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string szAddress { get; set; } = string.Empty;

    /// <summary>
    /// 資料型態 (Integer, FloatingPt, SwappedFP, Double, SwappedDouble 等)
    /// </summary>
    [Required]
    [StringLength(100)]
    public string szDataType { get; set; } = string.Empty;

    /// <summary>
    /// 數值縮放比例
    /// </summary>
    [Required]
    public float fRatio { get; set; } = 1.0f;

    /// <summary>
    /// 物理單位 (如 °C, V, A 等)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 最小值 (控制點位使用)
    /// </summary>
    public float? fMin { get; set; }

    /// <summary>
    /// 最大值 (控制點位使用)
    /// </summary>
    public float? fMax { get; set; }

    /// <summary>
    /// 驗證模型有效性
    /// </summary>
    /// <returns>驗證成功回傳 true</returns>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(szSID) || string.IsNullOrWhiteSpace(szName))
            return false;

        if (string.IsNullOrWhiteSpace(szAddress))
            return false;

        if (string.IsNullOrWhiteSpace(szDataType))
            return false;

        // 檢查數據型態是否支援
        var supportedTypes = new[] { "INTEGER", "UINTEGER", "FLOATINGPT", "SWAPPEDFP", "DOUBLE", "SWAPPEDDOUBLE" };
        if (!supportedTypes.Contains(szDataType))
            return false;

        return true;
    }

    /// <summary>
    /// 從 ModbusTagModel 建立 ModbusPointModel
    /// </summary>
    /// <param name="tag">Modbus 標籤模型</param>
    /// <param name="szSID">點位 SID</param>
    /// <returns>ModbusPoint 模型</returns>
    public static ModbusPointModel FromTag(ModbusTagModel tag, string szSID)
    {
        var point = new ModbusPointModel
        {
            szSID = szSID,
            szName = tag.szName,
            szAddress = tag.szAddress,
            szDataType = tag.szDataType,
            fRatio = float.Parse(tag.szRatio),
            szUnit = tag.szUnit
        };

        // 解析最小值和最大值
        if (!string.IsNullOrWhiteSpace(tag.szMin) && float.TryParse(tag.szMin, out var fMin))
            point.fMin = fMin;

        if (!string.IsNullOrWhiteSpace(tag.szMax) && float.TryParse(tag.szMax, out var fMax))
            point.fMax = fMax;

        return point;
    }
}