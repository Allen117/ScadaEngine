using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaEngine.Engine.Models;

/// <summary>
/// Coordinator 資料模型，對應 ModbusCoordinator 表
/// </summary>
public class CoordinatorModel
{
    /// <summary>
    /// 自動遞增主鍵
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 設備名稱
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("Name")]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// Modbus ID
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("ModbusID")]
    public string szModbusID { get; set; } = string.Empty;

    /// <summary>
    /// 延遲時間（毫秒）
    /// </summary>
    [Column("DelayTime")]
    public int nDelayTime { get; set; } = 0;

    /// <summary>
    /// 是否啟用監控
    /// </summary>
    [Column("MonitorEnabled")]
    public bool isMonitorEnabled { get; set; } = true;

    /// <summary>
    /// 驗證模型有效性
    /// </summary>
    /// <returns>驗證結果</returns>
    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(szName) && 
               !string.IsNullOrWhiteSpace(szModbusID);
    }
}