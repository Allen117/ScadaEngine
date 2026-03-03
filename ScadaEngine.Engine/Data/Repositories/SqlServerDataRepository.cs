using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Dapper;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Data.Services;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Models;
namespace ScadaEngine.Engine.Data.Repositories;

/// <summary>
/// SQL Server 資料存取實作類別，負責實際的資料庫操作
/// </summary>
public class SqlServerDataRepository : IDataRepository, IDisposable
{
    private readonly ILogger<SqlServerDataRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;
    private bool _isDisposed = false;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="configService">資料庫配置服務</param>
    public SqlServerDataRepository(ILogger<SqlServerDataRepository> logger, DatabaseConfigService configService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <summary>
    /// 初始化資料庫連線字串
    /// </summary>
    /// <returns>初始化成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            _szConnectionString = await _configService.GetConnectionStringAsync();
            _logger.LogInformation("資料庫連線字串已初始化");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化資料庫連線字串時發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 測試資料庫連線是否正常
    /// </summary>
    /// <returns>連線正常回傳 true，失敗回傳 false</returns>
    public async Task<bool> TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            
            var result = await connection.QuerySingleAsync<int>("SELECT 1");
            
            _logger.LogInformation("資料庫連線測試成功");
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "資料庫連線測試失敗");
            return false;
        }
    }

    /// <summary>
    /// 儲存即時資料至資料庫 (批量處理)
    /// </summary>
    /// <param name="realtimeDataList">即時資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> SaveRealtimeDataAsync(IEnumerable<RealtimeDataModel> realtimeDataList)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        var nSuccessCount = 0;

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // SQL 插入語句 (假設有對應的資料表)
            const string szSql = @"
                INSERT INTO RealtimeData (SID, TagName, Value, Unit, Quality, DeviceIP, Timestamp, Address)
                VALUES (@SID, @TagName, @Value, @Unit, @Quality, @DeviceIP, @Timestamp, @Address)";

            foreach (var data in realtimeDataList)
            {
                try
                {
                    var parameters = new
                    {
                        SID = data.szSID,
                        TagName = data.szTagName,
                        Value = data.fValue,
                        Unit = data.szUnit,
                        Quality = data.szQuality,
                        DeviceIP = data.szDeviceIP,
                        Timestamp = data.dtTimestamp,
                        Address = data.nAddress
                    };

                    var nRowsAffected = await connection.ExecuteAsync(szSql, parameters);
                    if (nRowsAffected > 0)
                    {
                        nSuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "儲存即時資料失敗: SID={SID}", data.szSID);
                }
            }

            _logger.LogInformation("批量儲存即時資料完成: 成功={SuccessCount}, 總計={TotalCount}", nSuccessCount, realtimeDataList.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量儲存即時資料時發生錯誤");
        }

        return nSuccessCount;
    }

    /// <summary>
    /// 儲存單筆即時資料至資料庫
    /// </summary>
    /// <param name="realtimeData">即時資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveRealtimeDataAsync(RealtimeDataModel realtimeData)
    {
        var result = await SaveRealtimeDataAsync(new[] { realtimeData });
        return result > 0;
    }

    /// <summary>
    /// 儲存設備配置至資料庫
    /// </summary>
    /// <param name="deviceConfig">設備配置</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveConfigAsync(ModbusDeviceConfigModel deviceConfig)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // SQL 插入語句 (假設有對應的設備配置資料表)
            const string szSql = @"
                INSERT INTO DeviceConfig (TypeId, IP, Port, ModbusId, ConnectTimeout)
                VALUES (@TypeId, @IP, @Port, @ModbusId, @ConnectTimeout)
                ON DUPLICATE KEY UPDATE 
                Port=@Port, ModbusId=@ModbusId, ConnectTimeout=@ConnectTimeout";

            var parameters = new
            {
                IP = deviceConfig.szIP,
                Port = deviceConfig.nPort,
                ModbusId = deviceConfig.szModbusId,
                ConnectTimeout = deviceConfig.nConnectTimeout
            };

            var nRowsAffected = await connection.ExecuteAsync(szSql, parameters);
            
            _logger.LogInformation("設備配置儲存完成: IP={IP}, 影響行數={RowsAffected}", deviceConfig.szIP, nRowsAffected);
            return nRowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存設備配置時發生錯誤: IP={IP}", deviceConfig.szIP);
            return false;
        }
    }

    /// <summary>
    /// 查詢指定時間範圍的歷史資料
    /// </summary>
    /// <param name="szSID">點位識別碼</param>
    /// <param name="dtStartTime">開始時間</param>
    /// <param name="dtEndTime">結束時間</param>
    /// <returns>歷史資料清單</returns>
    public async Task<IEnumerable<RealtimeDataModel>> GetHistoryDataAsync(string szSID, DateTime dtStartTime, DateTime dtEndTime)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT SID, TagName, Value, Unit, Quality, DeviceIP, Timestamp, Address
                FROM RealtimeData 
                WHERE SID = @SID AND Timestamp BETWEEN @StartTime AND @EndTime
                ORDER BY Timestamp DESC";

            var parameters = new
            {
                SID = szSID,
                StartTime = dtStartTime,
                EndTime = dtEndTime
            };

            var result = await connection.QueryAsync<RealtimeDataModel>(szSql, parameters);
            
            _logger.LogDebug("查詢歷史資料完成: SID={SID}, 筆數={Count}", szSID, result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢歷史資料時發生錯誤: SID={SID}", szSID);
            return Enumerable.Empty<RealtimeDataModel>();
        }
    }

    /// <summary>
    /// 取得所有設備配置
    /// </summary>
    /// <returns>設備配置清單</returns>
    public async Task<IEnumerable<ModbusDeviceConfigModel>> GetDeviceConfigsAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = "SELECT TypeId, IP, Port, ModbusId, ConnectTimeout FROM DeviceConfig";

            var result = await connection.QueryAsync<ModbusDeviceConfigModel>(szSql);
            
            _logger.LogDebug("查詢設備配置完成: 筆數={Count}", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢設備配置時發生錯誤");
            return Enumerable.Empty<ModbusDeviceConfigModel>();
        }
    }

    #region Coordinator 相關方法

    /// <summary>
    /// 新增或更新 Coordinator 配置
    /// </summary>
    /// <param name="coordinator">Coordinator 模型</param>
    /// <returns>資料庫 ID</returns>
    public async Task<int> SaveCoordinatorAsync(CoordinatorModel coordinator)
    {
        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // 檢查是否已存在相同的 Name 配置
            const string szCheckSql = "SELECT Id FROM ModbusCoordinator WHERE Name = @Name";
            var existingId = await connection.QuerySingleOrDefaultAsync<int?>(szCheckSql, new { Name = coordinator.szName });

            if (existingId.HasValue)
            {
                // 更新現有記錄的 ModbusID 和 DelayTime
                const string szUpdateSql = @"
                    UPDATE ModbusCoordinator 
                    SET ModbusID = @ModbusID, DelayTime = @DelayTime
                    WHERE Id = @Id";

                await connection.ExecuteAsync(szUpdateSql, new 
                { 
                    ModbusID = coordinator.szModbusID,
                    DelayTime = coordinator.nDelayTime,
                    Id = existingId.Value
                });

                _logger.LogDebug("更新 Coordinator 配置: ID={Id}, Name={Name}, ModbusID={ModbusID}", existingId.Value, coordinator.szName, coordinator.szModbusID);
                return existingId.Value;
            }
            else
            {
                // 新增記錄
                const string szInsertSql = @"
                    INSERT INTO ModbusCoordinator (Name, ModbusID, DelayTime, MonitorEnabled) 
                    VALUES (@Name, @ModbusID, @DelayTime, @MonitorEnabled);
                    SELECT SCOPE_IDENTITY();";

                var newId = await connection.QuerySingleAsync<int>(szInsertSql, new 
                { 
                    Name = coordinator.szName,
                    ModbusID = coordinator.szModbusID,
                    DelayTime = coordinator.nDelayTime,
                    MonitorEnabled = coordinator.isMonitorEnabled
                });

                _logger.LogDebug("新增 Coordinator 配置: ID={Id}, Name={Name}, ModbusID={ModbusID}", newId, coordinator.szName, coordinator.szModbusID);
                return newId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存 Coordinator 配置時發生錯誤: {Name}", coordinator.szName);
            throw;
        }
    }

    /// <summary>
    /// 根據 ModbusID 查詢 Coordinator
    /// </summary>
    /// <param name="szModbusID">Modbus ID</param>
    /// <returns>Coordinator 模型</returns>
    public async Task<CoordinatorModel?> GetCoordinatorByModbusIdAsync(string szModbusID)
    {
        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = "SELECT Id, Name, ModbusID, DelayTime, MonitorEnabled FROM ModbusCoordinator WHERE ModbusID = @ModbusID";
            var result = await connection.QuerySingleOrDefaultAsync<CoordinatorModel>(szSql, new { ModbusID = szModbusID });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢 Coordinator 配置時發生錯誤: ModbusID={ModbusID}", szModbusID);
            return null;
        }
    }

    /// <summary>
    /// 取得 ModbusCoordinator 資料表的所有設備清單
    /// </summary>
    public async Task<IEnumerable<CoordinatorModel>> GetAllCoordinatorsAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            await InitializeAsync();

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT Id,
                       Name          AS szName,
                       ModbusID      AS szModbusID,
                       DelayTime     AS nDelayTime,
                       MonitorEnabled AS isMonitorEnabled
                FROM ModbusCoordinator
                ORDER BY Id";

            var result = await connection.QueryAsync<CoordinatorModel>(szSql);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢所有 Coordinator 時發生錯誤");
            return [];
        }
    }

    #endregion

    #region 歷史資料操作方法

    /// <summary>
    /// 批量儲存歷史資料至 HistoryData 資料表
    /// </summary>
    /// <param name="historyDataList">歷史資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> SaveHistoryDataAsync(IEnumerable<HistoryDataModel> historyDataList)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        var nSuccessCount = 0;

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // SQL 插入語句，對應 DatabaseSchema.json 中的 HistoryData 表格結構
            // 使用 MERGE 避免重複插入相同的 SID + Timestamp 組合
            const string szSql = @"
                MERGE HistoryData AS target
                USING (VALUES (@SID, @Value, @Quality, @Timestamp)) AS source (SID, Value, Quality, Timestamp)
                ON target.SID = source.SID AND target.Timestamp = source.Timestamp
                WHEN MATCHED THEN
                    UPDATE SET Value = source.Value, Quality = source.Quality
                WHEN NOT MATCHED THEN
                    INSERT (SID, Value, Quality, Timestamp)
                    VALUES (source.SID, source.Value, source.Quality, source.Timestamp);";

            foreach (var data in historyDataList)
            {
                try
                {
                    var parameters = new
                    {
                        SID = data.szSID,
                        Value = data.fValue,
                        Quality = data.nQuality,
                        Timestamp = data.dtTimestamp
                    };

                    var nRowsAffected = await connection.ExecuteAsync(szSql, parameters);
                    if (nRowsAffected > 0)
                    {
                        nSuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "儲存歷史資料失敗: SID={SID}, Timestamp={Timestamp}", 
                        data.szSID, data.dtTimestamp);
                }
            }

            _logger.LogInformation("批量儲存歷史資料完成: 成功={SuccessCount}, 總計={TotalCount}", 
                nSuccessCount, historyDataList.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量儲存歷史資料時發生錯誤");
        }

        return nSuccessCount;
    }

    /// <summary>
    /// 儲存單筆歷史資料至 HistoryData 資料表
    /// </summary>
    /// <param name="historyData">歷史資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveHistoryDataAsync(HistoryDataModel historyData)
    {
        var result = await SaveHistoryDataAsync(new[] { historyData });
        return result > 0;
    }

    #endregion

    #region 最新資料操作方法

    /// <summary>
    /// 批量儲存最新資料至 LatestData 資料表
    /// </summary>
    /// <param name="latestDataList">最新資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> SaveLatestDataAsync(IEnumerable<LatestDataModel> latestDataList)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        var nSuccessCount = 0;

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // SQL MERGE 語句，對應 DatabaseSchema.json 中的 LatestData 表格結構
            const string szSql = @"
                MERGE LatestData AS target
                USING (VALUES (@SID, @Value, @Timestamp, @Quality)) AS source (SID, Value, Timestamp, Quality)
                ON target.SID = source.SID
                WHEN MATCHED THEN
                    UPDATE SET Value = source.Value, Timestamp = source.Timestamp, Quality = source.Quality
                WHEN NOT MATCHED THEN
                    INSERT (SID, Value, Timestamp, Quality)
                    VALUES (source.SID, source.Value, source.Timestamp, source.Quality);";

            foreach (var data in latestDataList)
            {
                try
                {
                    var parameters = new
                    {
                        SID = data.szSID,
                        Value = data.fValue,
                        Timestamp = data.dtTimestamp,
                        Quality = data.nQuality
                    };

                    var nRowsAffected = await connection.ExecuteAsync(szSql, parameters);
                    if (nRowsAffected > 0)
                    {
                        nSuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "儲存最新資料失敗: SID={SID}, Timestamp={Timestamp}", 
                        data.szSID, data.dtTimestamp);
                }
            }

            _logger.LogInformation("批量儲存最新資料完成: 成功={SuccessCount}, 總計={TotalCount}", 
                nSuccessCount, latestDataList.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量儲存最新資料時發生錯誤");
        }

        return nSuccessCount;
    }

    /// <summary>
    /// 儲存單筆最新資料至 LatestData 資料表
    /// </summary>
    /// <param name="latestData">最新資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveLatestDataAsync(LatestDataModel latestData)
    {
        var result = await SaveLatestDataAsync(new[] { latestData });
        return result > 0;
    }

    #endregion

    /// <summary>
    /// 取得最新的即時資料 (用於Web監控介面)
    /// </summary>
    /// <param name="nLimit">限制筆數</param>
    /// <returns>最新資料清單</returns>
    public async Task<IEnumerable<LatestDataModel>> GetLatestDataAsync(int nLimit = 100)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT TOP (@Limit) SID, Value, Quality, Timestamp
                FROM LatestData 
                ORDER BY Timestamp DESC";

            var result = await connection.QueryAsync<LatestDataModel>(szSql, new { Limit = nLimit });
            
            _logger.LogDebug("查詢最新資料完成: 筆數={Count}", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢最新資料時發生錯誤");
            return Enumerable.Empty<LatestDataModel>();
        }
    }

    /// <summary>
    /// 取得最新資料的時間戳記
    /// </summary>
    /// <returns>最新時間戳記</returns>
    public async Task<DateTime> GetLatestTimestampAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = "SELECT ISNULL(MAX(Timestamp), '1900-01-01') FROM LatestData";
            var result = await connection.QuerySingleAsync<DateTime>(szSql);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢最新時間戳記時發生錯誤");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 取得總點位數量
    /// </summary>
    /// <returns>點位總數</returns>
    public async Task<int> GetTotalTagCountAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = "SELECT COUNT(DISTINCT SID) FROM LatestData";
            var result = await connection.QuerySingleAsync<int>(szSql);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢點位總數時發生錯誤");
            return 0;
        }
    }

    /// <summary>
    /// 取得 Users 資料表中的使用者數量
    /// </summary>
    /// <returns>使用者總數</returns>
    public async Task<int> GetUserCountAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = "SELECT COUNT(*) FROM Users";
            var result = await connection.QuerySingleAsync<int>(szSql);
            
            _logger.LogDebug("Users 表中共有 {UserCount} 位使用者", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢 Users 表使用者數量時發生錯誤");
            return 0; // 錯誤時回傳 0，這樣會顯示測試帳號提示
        }
    }

    /// <summary>
    /// 驗證使用者帳號密碼 (密碼以 SHA256 hex 儲存於 PasswordHash 欄位)
    /// </summary>
    public async Task<bool> ValidateUserAsync(string szUsername, string szPassword)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT PasswordHash FROM Users
                WHERE Username = @Username AND IsActive = 1";

            var storedHash = await connection.QuerySingleOrDefaultAsync<string>(
                szSql, new { Username = szUsername });

            if (storedHash == null)
                return false;

            var inputHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(szPassword))).ToLower();

            return string.Equals(storedHash.Trim(), inputHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "驗證使用者 {Username} 時發生錯誤", szUsername);
            return false;
        }
    }

    #region ModbusPoints 相關方法

    /// <summary>
    /// 儲存 ModbusPoints 到資料庫 (批量處理，採用先刪後插入策略)
    /// </summary>
    /// <param name="nCoordinatorId">Coordinator ID</param>
    /// <param name="pointList">ModbusPoint 清單</param>
    /// <returns>成功處理的點位數量</returns>
    public async Task<int> SaveModbusPointsAsync(int nCoordinatorId, IEnumerable<ModbusPointModel> pointList)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        var points = pointList.ToList();
        if (!points.Any())
        {
            _logger.LogWarning("ModbusPoints 清單為空，跳過儲存作業");
            return 0;
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            // 使用交易確保資料一致性
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Step 1: 刪除 ModbusPoints 表的所有內容
                var szDeleteSql = @"DELETE FROM ModbusPoints";
                
                var nDeletedCount = await connection.ExecuteAsync(szDeleteSql, transaction: transaction);

                _logger.LogInformation("已清空 ModbusPoints 表，刪除 {Count} 個舊點位", nDeletedCount);

                // Step 2: 批量插入新點位
                const string szInsertSql = @"
                    INSERT INTO ModbusPoints 
                    (SID, Name, Address, DataType, Ratio, Unit, Min, Max)
                    VALUES 
                    (@SID, @Name, @Address, @DataType, @Ratio, @Unit, @Min, @Max)";

                var nInsertedCount = 0;
                var nValidationFailures = 0;
                var nInsertFailures = 0;

                foreach (var point in points)
                {
                    if (point.Validate())
                    {
                        try
                        {
                            await connection.ExecuteAsync(szInsertSql, new
                            {
                                SID = point.szSID,
                                Name = point.szName,
                                Address = point.szAddress,
                                DataType = point.szDataType,
                                Ratio = point.fRatio,
                                Unit = point.szUnit,
                                Min = point.fMin,
                                Max = point.fMax
                            }, transaction);

                            nInsertedCount++;
                        }
                        catch (Exception ex)
                        {
                            nInsertFailures++;
                            _logger.LogError(ex, "插入點位失敗: SID={SID}, Name={Name}, Address={Address}", 
                                point.szSID, point.szName, point.szAddress);
                        }
                    }
                    else
                    {
                        nValidationFailures++;
                        _logger.LogWarning("點位驗證失敗，跳過: SID={SID}, Name={Name}, Address={Address}, DataType={DataType}", 
                            point.szSID, point.szName, point.szAddress, point.szDataType);
                    }
                }

                // 提交交易
                await transaction.CommitAsync();

                _logger.LogInformation("成功儲存 Coordinator {CoordinatorId} 的點位到 ModbusPoints 表: " +
                    "總共={Total}, 成功={Success}, 驗證失敗={ValidationFail}, 插入失敗={InsertFail}", 
                    nCoordinatorId, points.Count, nInsertedCount, nValidationFailures, nInsertFailures);

                if (nValidationFailures > 0 || nInsertFailures > 0)
                {
                    _logger.LogWarning("發現問題: {Total} 個點位中有 {Problems} 個未成功插入", 
                        points.Count, nValidationFailures + nInsertFailures);
                }

                return nInsertedCount;
            }
            catch
            {
                // 發生錯誤時回滾交易
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存 ModbusPoints 時發生錯誤: CoordinatorId={CoordinatorId}", nCoordinatorId);
            return 0;
        }
    }

    /// <summary>
    /// 查詢指定 Coordinator 的所有 ModbusPoints
    /// </summary>
    /// <param name="nCoordinatorId">Coordinator ID</param>
    /// <returns>ModbusPoint 清單</returns>
    public async Task<IEnumerable<ModbusPointModel>> GetModbusPointsByCoordinatorAsync(int nCoordinatorId)
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT SID, Name, Address, DataType, Ratio, Unit, Min, Max
                FROM ModbusPoints 
                WHERE SID LIKE @SidPrefix
                ORDER BY SID";

            // SID 格式為 XXX-SN，計算該 DatabaseId 對應的 SID 前綴
            var szSidPrefix = $"{nCoordinatorId * 65536}%";
            
            var result = await connection.QueryAsync<ModbusPointModel>(szSql, 
                new { SidPrefix = szSidPrefix });

            _logger.LogDebug("查詢 Coordinator {CoordinatorId} 的點位完成: 筆數={Count}", 
                nCoordinatorId, result.Count());

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢 ModbusPoints 時發生錯誤: CoordinatorId={CoordinatorId}", nCoordinatorId);
            return Enumerable.Empty<ModbusPointModel>();
        }
    }

    /// <summary>
    /// 取得所有已設定的 ModbusPoints (用於Web初始化全點位清單)
    /// </summary>
    public async Task<IEnumerable<ModbusPointModel>> GetAllModbusPointsAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
        {
            await InitializeAsync();
        }

        try
        {
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szSql = @"
                SELECT SID, Name, Address, DataType, Ratio, Unit, Min, Max
                FROM ModbusPoints
                ORDER BY SID";

            var result = await connection.QueryAsync<ModbusPointModel>(szSql);

            _logger.LogDebug("查詢所有 ModbusPoints 完成: 筆數={Count}", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢所有 ModbusPoints 時發生錯誤");
            return Enumerable.Empty<ModbusPointModel>();
        }
    }

    #endregion

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _logger.LogInformation("SQL Server 資料存取服務已釋放資源");
        }
    }
}