using Newtonsoft.Json;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Data.Repositories;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Services;

/// <summary>
/// Modbus 設定檔讀取服務，負責載入 JSON 配置並建立設備物件
/// </summary>
public class ModbusConfigService
{
    private readonly ILogger<ModbusConfigService> _logger;
    private readonly string _szConfigFolderPath;
    private readonly SqlServerDataRepository _dataRepository;

    public ModbusConfigService(ILogger<ModbusConfigService> logger, SqlServerDataRepository dataRepository)
    {
        _logger = logger;
        _szConfigFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modbus");
        _dataRepository = dataRepository;
    }

    /// <summary>
    /// 載入指定資料夾下所有 Modbus JSON 設定檔
    /// </summary>
    /// <returns>設備配置清單</returns>
    public async Task<List<ModbusDeviceConfigModel>> LoadAllDeviceConfigsAsync()
    {
        var configList = new List<ModbusDeviceConfigModel>();

        try
        {
            // 檢查設定檔資料夾是否存在
            if (!Directory.Exists(_szConfigFolderPath))
            {
                _logger.LogWarning("Modbus 設定檔資料夾不存在: {FolderPath}", _szConfigFolderPath);
                return configList;
            }

            // 搜尋所有 JSON 檔案
            var jsonFiles = Directory.GetFiles(_szConfigFolderPath, "*.json", SearchOption.TopDirectoryOnly);
            
            if (jsonFiles.Length == 0)
            {
                _logger.LogWarning("未找到任何 Modbus 設定檔案於: {FolderPath}", _szConfigFolderPath);
                return configList;
            }

            _logger.LogInformation("找到 {Count} 個 Modbus 設定檔案", jsonFiles.Length);

            // 並行載入所有設定檔
            var loadTasks = jsonFiles.Select(async szFilePath =>
            {
                try
                {
                    var config = await LoadSingleDeviceConfigAsync(szFilePath);
                    if (config != null)
                    {
                        // 生成點位 SID
                        await GenerateTagSIDsAsync(config, szFilePath);
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "載入設定檔失敗: {FilePath}", szFilePath);
                }
                return null;
            });

            var results = await Task.WhenAll(loadTasks);
            configList.AddRange(results.Where(config => config != null)!);

            _logger.LogInformation("成功載入 {Count} 個有效的 Modbus 設備配置", configList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入 Modbus 設定檔時發生錯誤");
        }

        return configList;
    }

    /// <summary>
    /// 載入單個 Modbus 設定檔
    /// </summary>
    /// <param name="szFilePath">設定檔路徑</param>
    /// <returns>設備配置物件</returns>
    private async Task<ModbusDeviceConfigModel?> LoadSingleDeviceConfigAsync(string szFilePath)
    {
        try
        {
            var szJsonContent = await File.ReadAllTextAsync(szFilePath);
            
            if (string.IsNullOrWhiteSpace(szJsonContent))
            {
                _logger.LogWarning("設定檔內容為空: {FilePath}", szFilePath);
                return null;
            }

            // 反序列化 JSON 設定
            var config = JsonConvert.DeserializeObject<ModbusDeviceConfigModel>(szJsonContent);
            
            if (config == null)
            {
                _logger.LogWarning("無法解析設定檔 JSON 格式: {FilePath}", szFilePath);
                return null;
            }

            // 映射 JSON 屬性到 Model 屬性
            MapJsonToConfigModel(config, szJsonContent);

            // 驗證設定有效性
            if (!config.Validate())
            {
                _logger.LogWarning("設定檔驗證失敗: {FilePath}", szFilePath);
                return null;
            }

            _logger.LogDebug("成功載入設定檔: {FilePath}, IP: {IP}, 點位數量: {TagCount}", 
                           szFilePath, config.szIP, config.tagList.Count);

            return config;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON 格式錯誤於設定檔: {FilePath}", szFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入設定檔時發生未預期錯誤: {FilePath}", szFilePath);
        }

        return null;
    }

    /// <summary>
    /// 映射 JSON 屬性到配置模型 (處理 JSON 屬性名稱與 Model 屬性的對應)
    /// </summary>
    /// <param name="config">配置模型物件</param>
    /// <param name="szJsonContent">JSON 內容</param>
    private void MapJsonToConfigModel(ModbusDeviceConfigModel config, string szJsonContent)
    {
        try
        {
            // 使用動態物件解析 JSON 以處理屬性名稱對應
            dynamic? jsonObj = JsonConvert.DeserializeObject(szJsonContent);
            
            if (jsonObj == null) return;

            // 映射根節點屬性
            config.szIP = jsonObj.IP ?? string.Empty;
            config.nPort = jsonObj.Port ?? 502;
            config.szModbusId = jsonObj.ModbusId ?? "1";
            config.nConnectTimeout = jsonObj.connectTimeout ?? 1000;

            // 映射點位清單
            if (jsonObj.Tags != null)
            {
                config.tagList.Clear();
                
                foreach (var tagJson in jsonObj.Tags)
                {
                    var tag = new ModbusTagModel
                    {
                        szName = tagJson.Name ?? string.Empty,
                        szAddress = tagJson.Address ?? string.Empty,
                        szDataType = tagJson.DataType ?? "Integer",
                        szRatio = tagJson.Ratio?.ToString() ?? "1",
                        szUnit = tagJson.Unit ?? string.Empty,
                        szMax = tagJson.Max?.ToString() ?? string.Empty,
                        szMin = tagJson.Min?.ToString() ?? string.Empty
                    };

                    // 驗證並解析點位設定
                    if (tag.Validate())
                    {
                        config.tagList.Add(tag);
                    }
                    else
                    {
                        _logger.LogWarning("點位設定無效，已跳過: {TagName} ({Address})", tag.szName, tag.szAddress);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "映射 JSON 屬性時發生錯誤");
        }
    }

    /// <summary>
    /// 為設備配置儲存資料庫 ID 並將點位插入 ModbusPoints 表
    /// </summary>
    /// <param name="config">設備配置</param>
    /// <param name="szFilePath">設定檔路徑</param>
    private async Task GenerateTagSIDsAsync(ModbusDeviceConfigModel config, string szFilePath)
    {
        try
        {
            // 將配置儲存到資料庫並取得真實 ID
            var nDatabaseId = await GetConfigDatabaseIdAsync(config, szFilePath);
            
            // 將 DatabaseId 儲存到配置中，供後續動態 SID 生成使用
            config.nDatabaseId = nDatabaseId;

            // 將 Coordinator 名稱 (JSON 檔名) 儲存到配置中，供發布 MQTT 時附帶
            config.szCoordinatorName = Path.GetFileNameWithoutExtension(szFilePath);

            _logger.LogInformation("設備配置 DatabaseId 已設定: {DatabaseId}, IP: {IP}, ModbusId: {ModbusId}, CoordinatorName: {CoordinatorName}, 點位數量: {TagCount}",
                                 nDatabaseId, config.szIP, config.szModbusId, config.szCoordinatorName, config.tagList.Count);

            // 將所有點位插入 ModbusPoints 表
            await InsertTagsToModbusPointsAsync(config, nDatabaseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定 Database ID 時發生錯誤");
        }
    }

    /// <summary>
    /// 將點位資料插入 ModbusPoints 表
    /// </summary>
    /// <param name="config">設備配置</param>
    /// <param name="nDatabaseId">資料庫 ID</param>
    private async Task InsertTagsToModbusPointsAsync(ModbusDeviceConfigModel config, int nDatabaseId)
    {
        try
        {
            if (!config.tagList.Any())
            {
                _logger.LogWarning("設備 {DatabaseId} 沒有點位資料，跳過 ModbusPoints 插入", nDatabaseId);
                return;
            }

            var pointList = new List<ModbusPointModel>();

            // 解析多個 ModbusId (用逗號分隔)
            var modbusIds = config.szModbusId.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => int.TryParse(id, out _))
                .Select(int.Parse)
                .ToList();

            if (!modbusIds.Any())
            {
                _logger.LogWarning("設備 {DatabaseId} 的 ModbusId 格式錯誤: {ModbusId}", nDatabaseId, config.szModbusId);
                return;
            }

            // 為每個 ModbusId 生成點位
            foreach (var nModbusId in modbusIds)
            {
                for (int nTagIndex = 0; nTagIndex < config.tagList.Count; nTagIndex++)
                {
                    var tag = config.tagList[nTagIndex];

                    // 生成 SID: 
                    var szSID = (nDatabaseId * 65536 + nModbusId * 256 +1).ToString() + "-S" + (nTagIndex+1).ToString();

                    // 建立 ModbusPointModel
                    var point = ModbusPointModel.FromTag(tag, szSID);
                    
                    if (point.Validate())
                    {
                        pointList.Add(point);
                    }
                    else
                    {
                        _logger.LogWarning("點位驗證失敗，跳過: Name={Name}, Address={Address}", tag.szName, tag.szAddress);
                    }
                }
            }

            // 批量插入到 ModbusPoints 表
            var nInsertedCount = await _dataRepository.SaveModbusPointsAsync(nDatabaseId, pointList);
            
            _logger.LogInformation("設備 {DatabaseId} 成功插入 {Count} 個點位到 ModbusPoints 表", 
                nDatabaseId, nInsertedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "插入點位到 ModbusPoints 表時發生錯誤: DatabaseId={DatabaseId}", nDatabaseId);
        }
    }

    /// <summary>
    /// 將設定檔內容儲存到資料庫並取得 ID
    /// </summary>
    /// <param name="config">設備配置</param>
    /// <param name="szFilePath">設定檔路徑</param>
    /// <returns>資料庫 ID</returns>
    private async Task<int> GetConfigDatabaseIdAsync(ModbusDeviceConfigModel config, string szFilePath)
    {
        try
        {
            var szFileName = Path.GetFileNameWithoutExtension(szFilePath);
            
            // 建立 Coordinator 模型
            var coordinator = new CoordinatorModel
            {
                szName = szFileName,
                szModbusID = config.szModbusId,
                nDelayTime = config.nConnectTimeout,
                isMonitorEnabled = true
            };

            // 儲存或更新到資料庫並取得 ID
            var nDatabaseId = await _dataRepository.SaveCoordinatorAsync(coordinator);
            
            _logger.LogInformation("Coordinator 配置已儲存到資料庫: ID={Id}, Name={Name}, ModbusID={ModbusID}", 
                                 nDatabaseId, coordinator.szName, coordinator.szModbusID);
            
            return nDatabaseId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存 Coordinator 配置到資料庫時發生錯誤: {FilePath}", szFilePath);
            
            // 發生錯誤時使用檔案名稱 Hash 作為備用方案
            var szFileName = Path.GetFileNameWithoutExtension(szFilePath);
            var nHashCode = Math.Abs(szFileName.GetHashCode());
            return nHashCode % 1000 + 1;
        }
    }

    /// <summary>
    /// 重新載入指定設定檔
    /// </summary>
    /// <param name="szFilePath">設定檔路徑</param>
    /// <returns>重新載入的設備配置</returns>
    public async Task<ModbusDeviceConfigModel?> ReloadDeviceConfigAsync(string szFilePath)
    {
        _logger.LogInformation("重新載入 Modbus 設定檔: {FilePath}", szFilePath);
        
        var config = await LoadSingleDeviceConfigAsync(szFilePath);
        
        if (config != null)
        {
            await GenerateTagSIDsAsync(config, szFilePath);
            _logger.LogInformation("成功重新載入設備配置: {IP}", config.szIP);
        }
        
        return config;
    }

    /// <summary>
    /// 監控設定檔變更
    /// </summary>
    /// <param name="onConfigChanged">設定檔變更時的回調函式</param>
    /// <returns>檔案監控器</returns>
    public FileSystemWatcher CreateConfigFileWatcher(Action<string> onConfigChanged)
    {
        var watcher = new FileSystemWatcher(_szConfigFolderPath)
        {
            Filter = "*.json",
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        watcher.Changed += async (sender, e) =>
        {
            _logger.LogInformation("偵測到設定檔變更: {FilePath}", e.FullPath);
            
            // 延遲一下避免檔案被鎖定
            await Task.Delay(500);
            onConfigChanged(e.FullPath!);
        };

        watcher.Created += async (sender, e) =>
        {
            _logger.LogInformation("偵測到新設定檔: {FilePath}", e.FullPath);
            
            await Task.Delay(500);
            onConfigChanged(e.FullPath!);
        };

        return watcher;
    }
}