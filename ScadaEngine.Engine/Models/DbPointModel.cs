using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaEngine.Engine.Models;

/// <summary>
/// DB 來源點位資料模型，對應 DBPoints 表
/// SID 格式：DB{CoordinatorId}-S{Sequence}
/// </summary>
public class DbPointModel
{
    /// <summary>
    /// 點位 SID，例 'DB1-S5'（主鍵）
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("SID")]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 所屬 Coordinator Id
    /// </summary>
    [Column("CoordinatorId")]
    public int nCoordinatorId { get; set; }

    /// <summary>
    /// 序號（1~100），由載入器以 JSON 陣列索引+1 自動填，JSON 不顯式指定
    /// </summary>
    [Column("Sequence")]
    public int nSequence { get; set; }

    /// <summary>
    /// 點位名稱
    /// </summary>
    [Required]
    [StringLength(100)]
    [Column("Name")]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// 物理單位
    /// </summary>
    [StringLength(50)]
    [Column("Unit")]
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 顯示下限 + 控制寫入下限
    /// </summary>
    [Column("Min")]
    public float fMin { get; set; } = 0.0f;

    /// <summary>
    /// 顯示上限 + 控制寫入上限
    /// </summary>
    [Column("Max")]
    public float fMax { get; set; } = 100.0f;

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(szSID) || string.IsNullOrWhiteSpace(szName))
            return false;
        if (nSequence < 1 || nSequence > 100)
            return false;
        return true;
    }
}
