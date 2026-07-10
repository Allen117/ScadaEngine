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

            // 安全網：DB 本身不存在時嘗試自動建立；無權限則 log 指引後中止（不丟例外）
            var isDatabaseReady = await EnsureDatabaseExistsAsync();
            if (!isDatabaseReady)
            {
                return false;
            }

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

            // 執行欄位遷移（既有表格的欄位改名）— 必須先於欄位同步，否則改名會被誤判為缺欄位而補出空欄
            await MigrateColumnsAsync(connection);

            // Schema-driven 欄位同步：比對 INFORMATION_SCHEMA.COLUMNS 與 schema，自動補缺欄位（只加不減不改）
            await SyncMissingColumnsAsync(connection, schema);

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
    /// 確認目標資料庫存在，不存在時嘗試自動建立（安全網）。
    /// 降級順序：指定路徑建立（DbMaintenanceSetting.DataFileFolder）→ SQL Server 預設路徑 → 無權限則 log 指引並回傳 false。
    /// 新建 DB 一律明確設 RECOVERY SIMPLE，避免繼承 model 的 FULL 導致交易記錄檔無限成長。
    /// 既有 DB 完全不動（含復原模式）。
    /// </summary>
    /// <returns>DB 存在（或已成功建立）回傳 true；不存在且無法建立回傳 false</returns>
    public async Task<bool> EnsureDatabaseExistsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        var szDbName = config.szDataBaseName;

        // 快速路徑：能直接開啟目標 DB 即代表存在且有權限，零額外負擔
        try
        {
            using var probe = new SqlConnection(config.BuildConnectionString());
            await probe.OpenAsync();
            return true;
        }
        catch (SqlException ex)
        {
            _logger.LogWarning("無法開啟資料庫 {Db}（SqlError={Number}），改連 master 檢查是否需要自動建立", szDbName, ex.Number);
        }

        var masterConfig = new ScadaEngine.Common.Data.Models.DatabaseConfigModel
        {
            szDatabaseAddress = config.szDatabaseAddress,
            szDataBaseName = "master",
            szDataBaseAccount = config.szDataBaseAccount,
            szDataBasePassword = config.szDataBasePassword
        };

        try
        {
            using var connection = new SqlConnection(masterConfig.BuildConnectionString());
            await connection.OpenAsync();

            var nDbId = await connection.ExecuteScalarAsync<int?>("SELECT DB_ID(@Name)", new { Name = szDbName });
            if (nDbId.HasValue)
            {
                _logger.LogError(
                    "資料庫 {Db} 已存在但帳號 {Account} 無法開啟 — 請檢查該帳號的資料庫使用者對應與權限（可執行 Setting/install-db.ps1 修復）",
                    szDbName, config.szDataBaseAccount);
                return false;
            }

            var maintenance = DbMaintenanceSettingModel.LoadFromDefaultPaths(_logger);
            var szSafeName = szDbName.Replace("]", "]]");

            try
            {
                var szMdf = Path.Combine(maintenance.DataFileFolder, $"{szDbName}.mdf").Replace("'", "''");
                var szLdf = Path.Combine(maintenance.DataFileFolder, $"{szDbName}_log.ldf").Replace("'", "''");
                var szCreateSql =
                    $"CREATE DATABASE [{szSafeName}] " +
                    $"ON (NAME = N'{szSafeName}', FILENAME = N'{szMdf}') " +
                    $"LOG ON (NAME = N'{szSafeName}_log', FILENAME = N'{szLdf}')";
                await connection.ExecuteAsync(szCreateSql, commandTimeout: 120);
                _logger.LogInformation("已自動建立資料庫 {Db}（資料檔路徑: {Folder}）", szDbName, maintenance.DataFileFolder);
            }
            catch (SqlException exCreate) when (!IsCreateDatabasePermissionDenied(exCreate))
            {
                // 路徑不存在 / SQL 服務帳號無資料夾寫入權 → 退 SQL Server 預設路徑再試
                _logger.LogWarning(exCreate,
                    "以指定路徑 {Folder} 建立資料庫失敗，改用 SQL Server 預設路徑重試（建議以系統管理員執行 Setting/install-db.ps1 建立資料夾與權限）",
                    maintenance.DataFileFolder);
                await connection.ExecuteAsync($"CREATE DATABASE [{szSafeName}]", commandTimeout: 120);
                _logger.LogInformation("已自動建立資料庫 {Db}（SQL Server 預設路徑）", szDbName);
            }

            await connection.ExecuteAsync($"ALTER DATABASE [{szSafeName}] SET RECOVERY SIMPLE");
            return true;
        }
        catch (SqlException ex) when (IsCreateDatabasePermissionDenied(ex))
        {
            _logger.LogError(
                "資料庫 {Db} 不存在，且帳號 {Account} 無 CREATE DATABASE 權限 — " +
                "請以系統管理員執行 Setting/install-db.ps1，或請 DBA 建立資料庫（或暫時授予 dbcreator）後重新啟動",
                szDbName, config.szDataBaseAccount);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "檢查/自動建立資料庫 {Db} 時發生錯誤", szDbName);
            return false;
        }
    }

    /// <summary>SqlException 262 = CREATE DATABASE permission denied in database 'master'</summary>
    private static bool IsCreateDatabasePermissionDenied(SqlException ex)
    {
        return ex.Number == 262;
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
    /// 執行欄位遷移 — 僅處理既有表格的欄位改名（sp_rename 無法從 schema diff 推斷，維持手寫）。
    /// 補缺欄位一律走 SyncMissingColumnsAsync（schema-driven），不要在此新增手寫 ADD 段。
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "欄位遷移時發生錯誤（可忽略若欄位已正確）");
        }
    }

    /// <summary>
    /// Schema-driven 欄位同步 — 逐表比對 INFORMATION_SCHEMA.COLUMNS 與 schema 定義，缺欄位自動 ALTER TABLE ADD。
    /// 既有欄位原則上不改（型別 / nullability 不一致僅 log warning 讓漂移可見），
    /// 唯一例外：同型別 + 同 nullability 的字元欄位「純加寬」（如 nvarchar(100)→(500)/MAX）為無損操作，自動 ALTER COLUMN；縮短僅警告。
    /// IDENTITY / PK 欄位無法事後 ADD，缺了只 log error；NOT NULL 且無 Default 的欄位跳過並 log error（避免 ALTER 失敗）。
    /// </summary>
    /// <param name="connection">資料庫連線</param>
    /// <param name="schema">資料庫綱要模型</param>
    private async Task SyncMissingColumnsAsync(SqlConnection connection, DatabaseSchemaModel schema)
    {
        foreach (var table in schema.tableList)
        {
            try
            {
                // 查出該表目前所有欄位（表不存在則為空清單 → 跳過，建表流程已涵蓋）
                var dbColumns = (await connection.QueryAsync<(string ColumnName, string DataType, string IsNullable, int? CharMaxLength)>(
                    @"SELECT COLUMN_NAME AS ColumnName, DATA_TYPE AS DataType, IS_NULLABLE AS IsNullable, CHARACTER_MAXIMUM_LENGTH AS CharMaxLength
                      FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_NAME = @TableName",
                    new { TableName = table.szTableName })).ToList();

                if (dbColumns.Count == 0)
                {
                    continue;
                }

                var dbColumnMap = dbColumns.ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

                foreach (var column in table.columnList)
                {
                    if (dbColumnMap.TryGetValue(column.szName, out var dbColumn))
                    {
                        await CheckExistingColumnDriftAsync(connection, table, column, dbColumn);
                        continue;
                    }

                    // IDENTITY / PK 欄位無法事後 ADD
                    if (column.isIdentity || column.isPrimaryKey)
                    {
                        _logger.LogError(
                            "欄位同步跳過: {TableName}.{ColumnName} 為 IDENTITY/PK 欄位，無法自動補上，請人工遷移",
                            table.szTableName, column.szName);
                        continue;
                    }

                    // NOT NULL 且無 Default：對非空表 ADD 必失敗，跳過並要求補 Default
                    if (!column.isNullable && string.IsNullOrEmpty(column.szDefault))
                    {
                        _logger.LogError(
                            "欄位同步跳過: {TableName}.{ColumnName} 定義為 NOT NULL 但無 Default，請在 DatabaseSchema.json 補上 Default 後重啟",
                            table.szTableName, column.szName);
                        continue;
                    }

                    try
                    {
                        var szAlterSql = $"ALTER TABLE [{table.szTableName}] ADD {GenerateColumnDefinition(column)}";
                        await connection.ExecuteAsync(szAlterSql);
                        _logger.LogInformation("欄位同步完成: {TableName} 新增欄位 {ColumnName}", table.szTableName, column.szName);
                    }
                    catch (Exception ex)
                    {
                        // Engine / Web 同時啟動的 ALTER race 或權限不足：單欄失敗不中斷整批
                        _logger.LogWarning(ex, "欄位同步失敗: {TableName}.{ColumnName}（若另一端已補上可忽略）",
                            table.szTableName, column.szName);
                    }
                }

                // DB 有、schema 沒有的欄位（人工加過的）：不動，只讓漂移可見
                var schemaColumnNames = new HashSet<string>(table.columnList.Select(c => c.szName), StringComparer.OrdinalIgnoreCase);
                foreach (var dbColumn in dbColumns.Where(c => !schemaColumnNames.Contains(c.ColumnName)))
                {
                    _logger.LogWarning(
                        "欄位漂移: {TableName}.{ColumnName} 存在於 DB 但不在 DatabaseSchema.json（不自動移除，請人工確認是否回填定義）",
                        table.szTableName, dbColumn.ColumnName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步表格 {TableName} 欄位時發生錯誤", table.szTableName);
            }
        }
    }

    /// <summary>
    /// 檢查既有欄位與 schema 的漂移 — 型別 / nullability 不一致僅警告；
    /// 同型別 + 同 nullability 的字元欄位純加寬（含 → MAX）自動 ALTER COLUMN（無損），縮短僅警告。
    /// </summary>
    /// <param name="connection">資料庫連線</param>
    /// <param name="table">表格模型</param>
    /// <param name="column">schema 欄位定義</param>
    /// <param name="dbColumn">DB 現況（INFORMATION_SCHEMA.COLUMNS）</param>
    private async Task CheckExistingColumnDriftAsync(
        SqlConnection connection,
        DatabaseTableModel table,
        DatabaseColumnModel column,
        (string ColumnName, string DataType, string IsNullable, int? CharMaxLength) dbColumn)
    {
        // 型別不一致：只警告，不自動改（改型別有資料毀損風險）
        if (!string.Equals(dbColumn.DataType, column.szType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "欄位型別漂移: {TableName}.{ColumnName} DB={DbType} schema={SchemaType}（不自動修改，請人工確認）",
                table.szTableName, column.szName, dbColumn.DataType, column.szType);
            return;
        }

        // Nullability 不一致：只警告（NULL→NOT NULL 需先處理既有 NULL 值，是資料決策）
        var isDbNullable = string.Equals(dbColumn.IsNullable, "YES", StringComparison.OrdinalIgnoreCase);
        if (isDbNullable != column.isNullable)
        {
            _logger.LogWarning(
                "欄位 Nullability 漂移: {TableName}.{ColumnName} DB={DbNullable} schema={SchemaNullable}（不自動修改，請人工確認）",
                table.szTableName, column.szName, isDbNullable ? "NULL" : "NOT NULL", column.isNullable ? "NULL" : "NOT NULL");
            return;
        }

        // 長度比對：僅字元型別（varchar/nvarchar/char/nchar）且兩邊都有長度資訊才比
        var isCharType = column.szType.ToLower().Contains("varchar") || column.szType.ToLower().Contains("char");
        if (!isCharType || !column.nLength.HasValue || !dbColumn.CharMaxLength.HasValue)
        {
            return;
        }

        var nSchemaLen = column.nLength.Value;   // -1 = MAX
        var nDbLen = dbColumn.CharMaxLength.Value; // -1 = MAX
        if (nSchemaLen == nDbLen)
        {
            return;
        }

        // 純加寬（schema 比 DB 寬，含 → MAX）：無損 metadata 操作，自動執行
        var isWiden = nDbLen != -1 && (nSchemaLen == -1 || nSchemaLen > nDbLen);
        if (!isWiden)
        {
            _logger.LogWarning(
                "欄位長度漂移（縮短）: {TableName}.{ColumnName} DB={DbLen} schema={SchemaLen}（縮短有截斷風險，不自動修改，請人工確認）",
                table.szTableName, column.szName, nDbLen == -1 ? "MAX" : nDbLen.ToString(), nSchemaLen == -1 ? "MAX" : nSchemaLen.ToString());
            return;
        }

        try
        {
            var szLenSpec = nSchemaLen == -1 ? "MAX" : nSchemaLen.ToString();
            var szNullSpec = column.isNullable ? "NULL" : "NOT NULL";
            await connection.ExecuteAsync(
                $"ALTER TABLE [{table.szTableName}] ALTER COLUMN [{column.szName}] {column.szType}({szLenSpec}) {szNullSpec}");
            _logger.LogInformation(
                "欄位加寬完成: {TableName}.{ColumnName} {Type}: {DbLen} → {SchemaLen}",
                table.szTableName, column.szName, column.szType, nDbLen, szLenSpec);
        }
        catch (Exception ex)
        {
            // PK / 索引 / DEFAULT 約束相依等情況可能擋 ALTER COLUMN：不中斷整批，留警告請人工處理
            _logger.LogWarning(ex,
                "欄位加寬失敗: {TableName}.{ColumnName}（可能受 PK/索引/約束相依限制，請人工加寬）",
                table.szTableName, column.szName);
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