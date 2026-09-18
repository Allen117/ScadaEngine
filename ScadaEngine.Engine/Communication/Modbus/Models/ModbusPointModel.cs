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
    /// 站號內子設備分群名稱（Tag.Device 的投影，對應 ModbusPoints.DeviceGroup）。
    /// 未分群 / 多站號時為 null（見 plan 決策 4、5）。
    /// </summary>
    [StringLength(100)]
    public string? szDeviceGroup { get; set; }

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

        // 檢查數據型態是否支援（白名單唯一真相來源在 ModbusTagModel，含 DEC10K3 / BCD / BIT0–BIT15）
        if (!ModbusTagModel.SupportedDataTypes.Contains(szDataType))
            return false;

        return true;
    }

    /// <summary>
    /// 依「站號 / Device 互斥」規則（plan 決策 4）算出點位的 DeviceGroup 投影值 —— 互斥規則的單一真相來源，
    /// 供 Engine 載入 JSON 與 Web 熱編輯寫回共用，避免兩處各寫一份而走偏。
    /// 多站號 Coordinator → 一律 null（站號即設備，Tag.Device 靜默忽略）；
    /// 單站號 → Tag.Device 去頭尾空白，留白（未填）則回 null（未分群，行為不變）。
    /// </summary>
    public static string? ResolveDeviceGroup(string? szDevice, bool isMultiStation)
        => (isMultiStation || string.IsNullOrWhiteSpace(szDevice)) ? null : szDevice.Trim();

    /// <summary>
    /// 從 ModbusTagModel 建立 ModbusPointModel
    /// </summary>
    /// <param name="tag">Modbus 標籤模型</param>
    /// <param name="szSID">點位 SID</param>
    /// <param name="szDeviceGroup">站號內子設備分群（由呼叫端依站號互斥規則決定，多站號傳 null）</param>
    /// <returns>ModbusPoint 模型</returns>
    public static ModbusPointModel FromTag(ModbusTagModel tag, string szSID, string? szDeviceGroup = null)
    {
        var point = new ModbusPointModel
        {
            szSID = szSID,
            szName = tag.szName,
            szAddress = tag.szAddress,
            szDataType = tag.szDataType,
            fRatio = float.Parse(tag.szRatio),
            szUnit = tag.szUnit,
            szDeviceGroup = szDeviceGroup
        };

        // 解析最小值和最大值
        if (!string.IsNullOrWhiteSpace(tag.szMin) && float.TryParse(tag.szMin, out var fMin))
            point.fMin = fMin;

        if (!string.IsNullOrWhiteSpace(tag.szMax) && float.TryParse(tag.szMax, out var fMax))
            point.fMax = fMax;

        return point;
    }
}