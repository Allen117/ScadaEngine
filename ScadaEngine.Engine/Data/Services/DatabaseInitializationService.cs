using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Dapper;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Data.Services;

/// <summary>
/// 資料庫初始化服務，負責檢查並建立資料庫表格結構
/// </summary>
public class DatabaseInitializationService
{
    private readonly ILogger<DatabaseInitializationService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly DatabaseSchemaService _schemaService;
    private string _szConnectionString = string.Empty;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="configService">資料庫配置服務</param>
    /// <param name="schemaService">資料庫綱要服務</param>
    public DatabaseInitializationService(
        ILogger<DatabaseInitializationService> logger,
        DatabaseConfigService configService,
        DatabaseSchemaService schemaService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    }

    /// <summary>
    /// 初始化資料庫結構
    /// </summary>
    /// <returns>初始化成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> InitializeDatabaseSchemaAsync()
    {
        try
        {
            // 取得連線字串
            _szConnectionString = await _configService.GetConnectionStringAsync();
            
            // 載入資料庫綱要
            var schema = await _schemaService.LoadSchemaAsync();
            if (schema.tableList.Count == 0)
            {
                _logger.LogWarning("沒有找到資料庫表格定義，跳過結構初始化");
                return true;
            }

            _logger.LogInformation("開始初始化資料庫結構，共 {TableCount} 個表格", schema.tableList.Count);

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            var nCreatedTables = 0;
            var nExistingTables = 0;

            foreach (var table in schema.tableList)
            {
                try
                {
                    var isTableExists = await CheckTableExistsAsync(connection, table.szTableName);
                    Console.WriteLine($"檢查表格 {table.szTableName} 是否存在: {isTableExists}");
                    _logger.LogInformation("檢查表格 {TableName} 是否存在: {Exists}", table.szTableName, isTableExists);
                    
                    if (isTableExists)
                    {
                        Console.WriteLine($"表格 {table.szTableName} 已存在，跳過建立");
                        _logger.LogDebug("表格 {TableName} 已存在，跳過建立", table.szTableName);
                        nExistingTables++;
                    }
                    else
                    {
                        Console.WriteLine($"開始建立表格: {table.szTableName}");
                        _logger.LogInformation("開始建立表格: {TableName}", table.szTableName);
                        if (await CreateTableAsync(connection, table))
                        {
                            Console.WriteLine($"成功建立表格: {table.szTableName}");
                            _logger.LogInformation("成功建立表格: {TableName}", table.szTableName);
                            nCreatedTables++;
                        }
                        else
                        {
                            Console.WriteLine($"建立表格失敗: {table.szTableName}");
                            _logger.LogError("建立表格失敗: {TableName}", table.szTableName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "處理表格 {TableName} 時發生錯誤", table.szTableName);
                }
            }

            _logger.LogInformation("資料庫結構初始化完成: 新建={Created}, 已存在={Existing}", nCreatedTables, nExistingTables);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化資料庫結構時發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 檢查表格是否已存在
    /// </summary>
    /// <param name="connection">資料庫連線</param>
    /// <param name="szTableName">表格名稱</param>
    /// <returns>表格存在回傳 true，不存在回傳 false</returns>
    private async Task<bool> CheckTableExistsAsync(SqlConnection connection, string szTableName)
    {
        const string szSql = @"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_NAME = @TableName AND TABLE_TYPE = 'BASE TABLE'";

        var nCount = await connection.QuerySingleAsync<int>(szSql, new { TableName = szTableName });
        return nCount > 0;
    }

    /// <summary>
    /// 建立資料庫表格
    /// </summary>
    /// <param name="connection">資料庫連線</param>
    /// <param name="table">表格模型</param>
    /// <returns>建立成功回傳 true，失敗回傳 false</returns>
    private async Task<bool> CreateTableAsync(SqlConnection connection, DatabaseTableModel table)
    {
        try
        {
            var szCreateTableSql = GenerateCreateTableSql(table);
            
            Console.WriteLine($"建立表格 {table.szTableName} 的 SQL:\n{szCreateTableSql}");
            _logger.LogInformation("建立表格 SQL: {TableName}\n{Sql}", table.szTableName, szCreateTableSql);
            
            await connection.ExecuteAsync(szCreateTableSql);
            
            // 驗證表格是否真的建立成功
            var isCreated = await CheckTableExistsAsync(connection, table.szTableName);
            Console.WriteLine($"表格 {table.szTableName} 建立後驗證結果: {isCreated}");
            _logger.LogInformation("表格 {TableName} 建立後驗證結果: {Created}", table.szTableName, isCreated);
            
            return isCreated;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"建立表格 {table.szTableName} 時發生錯誤: {ex.Message}");
            Console.WriteLine($"詳細錯誤: {ex}");
            _logger.LogError(ex, "建立表格 {TableName} 時發生 SQL 錯誤: {ErrorMessage}", table.szTableName, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 產生建立表格的 SQL 語句
    /// </summary>
    /// <param name="table">表格模型</param>
    /// <returns>CREATE TABLE SQL 語句</returns>
    private string GenerateCreateTableSql(DatabaseTableModel table)
    {
        var sqlBuilder = new StringBuilder();
        sqlBuilder.AppendLine($"CREATE TABLE [{table.szTableName}] (");

        // 產生欄位定義
        var columnDefinitions = table.columnList.Select(GenerateColumnDefinition);
        sqlBuilder.AppendLine($"    {string.Join(",\n    ", columnDefinitions)}");

        // 產生主鍵約束
        var primaryKeyColumns = table.columnList.Where(c => c.isPrimaryKey).Select(c => $"[{c.szName}]").ToList();
        if (primaryKeyColumns.Count > 0)
        {
            var szClusteredOption = table.clusteredIndexList != null && table.clusteredIndexList.Count > 0 ? "CLUSTERED" : "NONCLUSTERED";
            sqlBuilder.AppendLine($"    ,CONSTRAINT [PK_{table.szTableName}] PRIMARY KEY {szClusteredOption} ({string.Join(", ", primaryKeyColumns)})");
        }

        sqlBuilder.AppendLine(");");

        // 產生叢集索引（如果不是主鍵）
        if (table.clusteredIndexList != null && table.clusteredIndexList.Count > 0 && primaryKeyColumns.Count == 0)
        {
            var indexColumns = table.clusteredIndexList.Select(c => $"[{c}]");
            sqlBuilder.AppendLine($"CREATE CLUSTERED INDEX [IX_{table.szTableName}_Clustered] ON [{table.szTableName}] ({string.Join(", ", indexColumns)});");
        }

        return sqlBuilder.ToString();
    }

    /// <summary>
    /// 產生欄位定義字串
    /// </summary>
    /// <param name="column">欄位模型</param>
    /// <returns>欄位定義 SQL</returns>
    private string GenerateColumnDefinition(DatabaseColumnModel column)
    {
        var definition = new StringBuilder();
        definition.Append($"[{column.szName}] ");

        // 資料型態
        if (column.nLength.HasValue && (column.szType.ToLower().Contains("varchar") || column.szType.ToLower().Contains("char")))
        {
            if (column.nLength.Value == -1)
            {
                // -1 代表 MAX 長度
                definition.Append($"{column.szType}(MAX)");
            }
            else
            {
                definition.Append($"{column.szType}({column.nLength})");
            }
        }
        else
        {
            definition.Append(column.szType);
        }

        // IDENTITY
        if (column.isIdentity)
        {
            definition.Append(" IDENTITY(1,1)");
        }

        // NULL/NOT NULL
        if (!column.isNullable)
        {
            definition.Append(" NOT NULL");
        }
        else
        {
            definition.Append(" NULL");
        }

        // 預設值
        if (!string.IsNullOrEmpty(column.szDefault))
        {
            if (column.szDefault.Equals("getdate()", StringComparison.OrdinalIgnoreCase))
            {
                definition.Append(" DEFAULT GETDATE()");
            }
            else if (column.szType.ToLower() == "bit")
            {
                // 將 boolean 值轉換為 SQL Server 的 bit 型態
                var isBitValue = column.szDefault.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                                column.szDefault == "1";
                definition.Append($" DEFAULT {(isBitValue ? "1" : "0")}");
            }
            else if (int.TryParse(column.szDefault, out _) || float.TryParse(column.szDefault, out _))
            {
                definition.Append($" DEFAULT {column.szDefault}");
            }
            else
            {
                definition.Append($" DEFAULT '{column.szDefault}'");
            }
        }

        return definition.ToString();
    }
}