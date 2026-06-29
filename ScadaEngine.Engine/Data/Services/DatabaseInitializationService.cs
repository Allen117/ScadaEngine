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

            // 執行欄位遷移（既有表格的欄位改名）
            await MigrateColumnsAsync(connection);

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
    /// 執行欄位遷移 — 處理既有表格的欄位改名
    /// </summary>
    private async Task MigrateColumnsAsync(SqlConnection connection)
    {
        try
        {
            // CalculatedPoints: CoordinatorName → GroupName
            var nHasOldColumn = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = 'CalculatedPoints' AND COLUMN_NAME = 'CoordinatorName'");

            if (nHasOldColumn > 0)
            {
                await connection.ExecuteAsync(
                    "EXEC sp_rename 'CalculatedPoints.CoordinatorName', 'GroupName', 'COLUMN'");
                _logger.LogInformation("欄位遷移完成: CalculatedPoints.CoordinatorName → GroupName");
            }

            // EnergyCircuit: 補上 Sign 欄位（既有 DB 升級用，預設 +1 與升級前行為一致）
            var nHasSignColumn = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = 'EnergyCircuit' AND COLUMN_NAME = 'Sign'");

            if (nHasSignColumn == 0)
            {
                var nHasTable = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                      WHERE TABLE_NAME = 'EnergyCircuit' AND TABLE_TYPE = 'BASE TABLE'");
                if (nHasTable > 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EnergyCircuit] ADD [Sign] INT NOT NULL DEFAULT 1");
                    _logger.LogInformation("欄位遷移完成: EnergyCircuit 新增 Sign 欄位（預設 1）");
                }
            }

            // EnergyCircuit: 補上 DemandSID 欄位（需量計算功率點位）
            var nHasDemandSidColumn = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = 'EnergyCircuit' AND COLUMN_NAME = 'DemandSID'");
            if (nHasDemandSidColumn == 0)
            {
                var nHasEnergyTable = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                      WHERE TABLE_NAME = 'EnergyCircuit' AND TABLE_TYPE = 'BASE TABLE'");
                if (nHasEnergyTable > 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EnergyCircuit] ADD [DemandSID] NVARCHAR(100) NULL");
                    _logger.LogInformation("欄位遷移完成: EnergyCircuit 新增 DemandSID 欄位");
                }
            }

            // TimeSchedules: 補上 ExcludeDates / IncludeDates 欄位
            var nHasTimeSchedulesTable = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                  WHERE TABLE_NAME = 'TimeSchedules' AND TABLE_TYPE = 'BASE TABLE'");

            if (nHasTimeSchedulesTable > 0)
            {
                var nHasExcludeDates = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'TimeSchedules' AND COLUMN_NAME = 'ExcludeDates'");
                if (nHasExcludeDates == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [TimeSchedules] ADD [ExcludeDates] NVARCHAR(MAX) NULL");
                    _logger.LogInformation("欄位遷移完成: TimeSchedules 新增 ExcludeDates 欄位");
                }

                var nHasIncludeDates = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'TimeSchedules' AND COLUMN_NAME = 'IncludeDates'");
                if (nHasIncludeDates == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [TimeSchedules] ADD [IncludeDates] NVARCHAR(MAX) NULL");
                    _logger.LogInformation("欄位遷移完成: TimeSchedules 新增 IncludeDates 欄位");
                }
            }

            // EventLog: 補上 MessageKey / MessageArgs 欄位（i18n 結構化警報訊息）
            var nHasEventLogTable = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                  WHERE TABLE_NAME = 'EventLog' AND TABLE_TYPE = 'BASE TABLE'");
            if (nHasEventLogTable > 0)
            {
                var nHasMessageKey = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'MessageKey'");
                if (nHasMessageKey == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [MessageKey] NVARCHAR(64) NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 MessageKey 欄位");
                }

                var nHasMessageArgs = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'MessageArgs'");
                if (nHasMessageArgs == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [MessageArgs] NVARCHAR(512) NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 MessageArgs 欄位");
                }

                // EventLog: 補上通知摘要欄位（NotifyChannel / NotifyStatus / NotifyDetail / NotifyRelatedEventId）
                var nHasNotifyChannel = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'NotifyChannel'");
                if (nHasNotifyChannel == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [NotifyChannel] NVARCHAR(10) NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 NotifyChannel 欄位");
                }
                var nHasNotifyStatus = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'NotifyStatus'");
                if (nHasNotifyStatus == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [NotifyStatus] TINYINT NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 NotifyStatus 欄位");
                }
                var nHasNotifyDetail = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'NotifyDetail'");
                if (nHasNotifyDetail == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [NotifyDetail] NVARCHAR(500) NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 NotifyDetail 欄位");
                }
                var nHasNotifyRelatedEventId = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'EventLog' AND COLUMN_NAME = 'NotifyRelatedEventId'");
                if (nHasNotifyRelatedEventId == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [EventLog] ADD [NotifyRelatedEventId] BIGINT NULL");
                    _logger.LogInformation("欄位遷移完成: EventLog 新增 NotifyRelatedEventId 欄位");
                }
            }

            // LineNotifyTargets: 補上 Language 欄位
            var nHasLineTargetsTable = await connection.QuerySingleAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                  WHERE TABLE_NAME = 'LineNotifyTargets' AND TABLE_TYPE = 'BASE TABLE'");
            if (nHasLineTargetsTable > 0)
            {
                var nHasLineLanguage = await connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = 'LineNotifyTargets' AND COLUMN_NAME = 'Language'");
                if (nHasLineLanguage == 0)
                {
                    await connection.ExecuteAsync(
                        "ALTER TABLE [LineNotifyTargets] ADD [Language] NVARCHAR(10) NOT NULL DEFAULT 'zh-TW'");
                    _logger.LogInformation("欄位遷移完成: LineNotifyTargets 新增 Language 欄位（預設 zh-TW）");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "欄位遷移時發生錯誤（可忽略若欄位已正確）");
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