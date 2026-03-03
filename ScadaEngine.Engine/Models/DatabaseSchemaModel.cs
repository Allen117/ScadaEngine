using System.Text.Json.Serialization;

namespace ScadaEngine.Engine.Models;

/// <summary>
/// 資料庫表格欄位模型
/// </summary>
public class DatabaseColumnModel
{
    /// <summary>
    /// 欄位名稱
    /// </summary>
    [JsonPropertyName("Name")]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// 欄位資料型態
    /// </summary>
    [JsonPropertyName("Type")]
    public string szType { get; set; } = string.Empty;

    /// <summary>
    /// 欄位長度（適用於 varchar, nvarchar 等）
    /// </summary>
    [JsonPropertyName("Length")]
    public int? nLength { get; set; }

    /// <summary>
    /// 是否為主鍵
    /// </summary>
    [JsonPropertyName("IsPrimaryKey")]
    public bool isPrimaryKey { get; set; } = false;

    /// <summary>
    /// 是否為自動遞增欄位
    /// </summary>
    [JsonPropertyName("IsIdentity")]
    public bool isIdentity { get; set; } = false;

    /// <summary>
    /// 是否為唯一約束欄位
    /// </summary>
    [JsonPropertyName("IsUnique")]
    public bool isUnique { get; set; } = false;

    /// <summary>
    /// 是否可為 NULL
    /// </summary>
    [JsonPropertyName("Nullable")]
    public bool isNullable { get; set; } = true;

    /// <summary>
    /// 預設值
    /// </summary>
    [JsonPropertyName("Default")]
    public string? szDefault { get; set; }
}

/// <summary>
/// 資料庫表格模型
/// </summary>
public class DatabaseTableModel
{
    /// <summary>
    /// 表格名稱
    /// </summary>
    [JsonPropertyName("TableName")]
    public string szTableName { get; set; } = string.Empty;

    /// <summary>
    /// 表格欄位清單
    /// </summary>
    [JsonPropertyName("Columns")]
    public List<DatabaseColumnModel> columnList { get; set; } = new List<DatabaseColumnModel>();

    /// <summary>
    /// 叢集索引欄位（用於指定複合主鍵或排序）
    /// </summary>
    [JsonPropertyName("ClusteredIndex")]
    public List<string>? clusteredIndexList { get; set; }
}

/// <summary>
/// 資料庫綱要模型，對應 DatabaseSchema.json 檔案結構
/// </summary>
public class DatabaseSchemaModel
{
    /// <summary>
    /// 資料庫表格清單
    /// </summary>
    [JsonPropertyName("Tables")]
    public List<DatabaseTableModel> tableList { get; set; } = new List<DatabaseTableModel>();
}