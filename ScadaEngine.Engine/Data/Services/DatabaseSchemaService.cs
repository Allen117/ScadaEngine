using System.Text.Json;
using System.Text.Json.Serialization;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;
namespace ScadaEngine.Engine.Data.Services;

/// <summary>
/// 資料庫綱要配置服務，負責載入和管理資料庫結構定義
/// </summary>
public class DatabaseSchemaService
{
    private readonly ILogger<DatabaseSchemaService> _logger;
    private readonly string _szSchemaPath;
    
    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="szSchemaPath">綱要檔路徑（預設為 "./DatabaseSchema/DatabaseSchema.json"）</param>
    public DatabaseSchemaService(ILogger<DatabaseSchemaService> logger, string szSchemaPath = "./DatabaseSchema/DatabaseSchema.json")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _szSchemaPath = szSchemaPath;
    }

    /// <summary>
    /// 載入資料庫綱要配置
    /// </summary>
    /// <returns>資料庫綱要模型，載入失敗時回傳空模型</returns>
    public async Task<DatabaseSchemaModel> LoadSchemaAsync()
    {
        try
        {
            if (!File.Exists(_szSchemaPath))
            {
                _logger.LogWarning("資料庫綱要檔案不存在: {SchemaPath}", _szSchemaPath);
                return new DatabaseSchemaModel();
            }

            var szJsonContent = await File.ReadAllTextAsync(_szSchemaPath);
            
            if (string.IsNullOrWhiteSpace(szJsonContent))
            {
                _logger.LogWarning("資料庫綱要檔案為空");
                return new DatabaseSchemaModel();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // 建立自訂轉換器來處理 JSON 屬性名稱對應
            var schema = JsonSerializer.Deserialize<DatabaseSchemaJsonModel>(szJsonContent, options);
            
            if (schema?.Tables == null)
            {
                _logger.LogError("無法解析資料庫綱要檔案或 Tables 區段不存在");
                return new DatabaseSchemaModel();
            }

            // 轉換為內部模型
            var result = new DatabaseSchemaModel
            {
                tableList = schema.Tables.Select(ConvertToTableModel).ToList()
            };

            _logger.LogInformation("成功載入資料庫綱要: 表格數量={TableCount}", result.tableList.Count);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析資料庫綱要檔案時發生 JSON 格式錯誤");
            return new DatabaseSchemaModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入資料庫綱要檔案時發生錯誤");
            return new DatabaseSchemaModel();
        }
    }

    /// <summary>
    /// 轉換 JSON 模型為內部表格模型
    /// </summary>
    /// <param name="jsonTable">JSON 表格模型</param>
    /// <returns>內部表格模型</returns>
    private DatabaseTableModel ConvertToTableModel(TableJsonModel jsonTable)
    {
        return new DatabaseTableModel
        {
            szTableName = jsonTable.TableName ?? string.Empty,
            columnList = jsonTable.Columns?.Select(ConvertToColumnModel).ToList() ?? new List<DatabaseColumnModel>(),
            clusteredIndexList = jsonTable.ClusteredIndex?.ToList()
        };
    }

    /// <summary>
    /// 轉換 JSON 模型為內部欄位模型
    /// </summary>
    /// <param name="jsonColumn">JSON 欄位模型</param>
    /// <returns>內部欄位模型</returns>
    private DatabaseColumnModel ConvertToColumnModel(ColumnJsonModel jsonColumn)
    {
        return new DatabaseColumnModel
        {
            szName = jsonColumn.Name ?? string.Empty,
            szType = jsonColumn.Type ?? string.Empty,
            nLength = jsonColumn.Length,
            isPrimaryKey = jsonColumn.IsPrimaryKey ?? false,
            isIdentity = jsonColumn.IsIdentity ?? false,
            isNullable = jsonColumn.Nullable ?? true,
            szDefault = ConvertDefault(jsonColumn.Default)
        };
    }

    /// <summary>
    /// 轉換預設值（支援 bool, int, string 類型）
    /// </summary>
    /// <param name="defaultValue">JSON 預設值</param>
    /// <returns>字串形式的預設值</returns>
    private string? ConvertDefault(object? defaultValue)
    {
        return defaultValue switch
        {
            bool bValue => bValue ? "1" : "0",
            int nValue => nValue.ToString(),
            string szValue => szValue,
            null => null,
            _ => defaultValue.ToString()
        };
    }
}

/// <summary>
/// JSON 反序列化用的輔助模型
/// </summary>
internal class DatabaseSchemaJsonModel
{
    public List<TableJsonModel>? Tables { get; set; }
}

/// <summary>
/// JSON 反序列化用的表格模型
/// </summary>
internal class TableJsonModel
{
    public string? TableName { get; set; }
    public List<ColumnJsonModel>? Columns { get; set; }
    public List<string>? ClusteredIndex { get; set; }
}

/// <summary>
/// JSON 反序列化用的欄位模型
/// </summary>
internal class ColumnJsonModel
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int? Length { get; set; }
    public bool? IsPrimaryKey { get; set; }
    public bool? IsIdentity { get; set; }
    public bool? Nullable { get; set; }
    public object? Default { get; set; }
}