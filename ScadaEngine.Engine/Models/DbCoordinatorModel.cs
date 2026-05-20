using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaEngine.Engine.Models;

/// <summary>
/// DB 來源 Coordinator 資料模型，對應 DBCoordinator 表
/// </summary>
public class DbCoordinatorModel
{
    /// <summary>
    /// 自動遞增主鍵（SID 中的 {CoordinatorId} 即為此值）
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Coordinator 名稱（= sheet name = JSON 檔名），UNIQUE
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("Name")]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// 輪詢間隔（毫秒），下限 200ms（DbCommunicationService 內 clamp）
    /// </summary>
    [Column("PollingInterval")]
    public int nPollingInterval { get; set; } = 1000;

    /// <summary>
    /// SQL 讀取逾時（毫秒）；polling 時換算成 SqlCommand.CommandTimeout（秒，向上取整）
    /// </summary>
    [Column("ConnectTimeout")]
    public int nConnectTimeout { get; set; } = 1000;

    /// <summary>
    /// 是否啟用監控
    /// </summary>
    [Column("MonitorEnabled")]
    public bool isMonitorEnabled { get; set; } = true;

    /// <summary>
    /// 建立時間
    /// </summary>
    [Column("CreatedAt")]
    public DateTime dtCreatedAt { get; set; }

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(szName);
    }
}
