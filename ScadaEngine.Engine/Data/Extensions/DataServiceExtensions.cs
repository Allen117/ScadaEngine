using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Data.Repositories;
using ScadaEngine.Engine.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using System.IO;
using ScadaEngine.Engine.Data.Services;
namespace ScadaEngine.Engine.Data.Extensions;

/// <summary>
/// 資料庫服務依賴注入擴充方法
/// </summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// 資料服務擴充方法
    /// </summary>
    /// <param name="services">服務集合</param>
    /// <returns>服務集合</returns>
    public static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        // 註冊資料庫配置服務為單例 - 智慧路徑偵測
        services.AddSingleton<DatabaseConfigService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DatabaseConfigService>>();
            
            // 使用應用程式基礎目錄作為設定檔路徑
            var szBasePath = AppDomain.CurrentDomain.BaseDirectory;
            var szConfigPath = Path.Combine(szBasePath, "Setting", "dbSetting.json");
            
            // 記錄路徑偵測結果
            logger.LogInformation("資料庫配置服務初始化");
            logger.LogInformation("應用程式基礎目錄: {BasePath}", szBasePath);
            logger.LogInformation("資料庫設定檔路徑: {ConfigPath}", szConfigPath);
            logger.LogInformation("設定檔是否存在: {FileExists}", File.Exists(szConfigPath));
            
            // 如果檔案不存在，嘗試備用路徑
            if (!File.Exists(szConfigPath))
            {
                // 備用路徑1: 開發模式的相對路徑
                var szFallbackPath1 = Path.Combine(Directory.GetCurrentDirectory(), "Setting", "dbSetting.json");
                if (File.Exists(szFallbackPath1))
                {
                    logger.LogInformation("使用備用路徑1: {FallbackPath}", szFallbackPath1);
                    szConfigPath = szFallbackPath1;
                }
                else
                {
                    // 備用路徑2: 專案根目錄
                    var szProjectRoot = Path.GetDirectoryName(szBasePath);
                    while (szProjectRoot != null && !Directory.GetFiles(szProjectRoot, "*.csproj").Any())
                    {
                        szProjectRoot = Path.GetDirectoryName(szProjectRoot);
                    }
                    
                    if (szProjectRoot != null)
                    {
                        var szFallbackPath2 = Path.Combine(szProjectRoot, "Setting", "dbSetting.json");
                        if (File.Exists(szFallbackPath2))
                        {
                            logger.LogInformation("使用備用路徑2 (專案根目錄): {FallbackPath}", szFallbackPath2);
                            szConfigPath = szFallbackPath2;
                        }
                        else
                        {
                            logger.LogWarning("所有路徑都找不到設定檔，將使用預設配置");
                        }
                    }
                    else
                    {
                        logger.LogWarning("無法找到專案根目錄，將使用預設配置");
                    }
                }
            }
            
            return new DatabaseConfigService(logger, szConfigPath);
        });

        // 註冊資料庫綱要服務為單例 - 智慧路徑偵測
        services.AddSingleton<DatabaseSchemaService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DatabaseSchemaService>>();
            
            // 使用應用程式基礎目錄作為設定檔路徑
            var szBasePath = AppDomain.CurrentDomain.BaseDirectory;
            var szSchemaPath = Path.Combine(szBasePath, "DatabaseSchema", "DatabaseSchema.json");
            
            // 記錄路徑偵測結果
            logger.LogInformation("資料庫綱要服務初始化");
            logger.LogInformation("應用程式基礎目錄: {BasePath}", szBasePath);
            logger.LogInformation("資料庫綱要檔路徑: {SchemaPath}", szSchemaPath);
            logger.LogInformation("綱要檔是否存在: {FileExists}", File.Exists(szSchemaPath));
            
            // 如果檔案不存在，嘗試備用路徑
            if (!File.Exists(szSchemaPath))
            {
                // 備用路徑1: 開發模式的相對路徑
                var szFallbackPath1 = Path.Combine(Directory.GetCurrentDirectory(), "DatabaseSchema", "DatabaseSchema.json");
                if (File.Exists(szFallbackPath1))
                {
                    logger.LogInformation("使用備用路徑1: {FallbackPath}", szFallbackPath1);
                    szSchemaPath = szFallbackPath1;
                }
                else
                {
                    // 備用路徑2: 專案根目錄
                    var szProjectRoot = Path.GetDirectoryName(szBasePath);
                    while (szProjectRoot != null && !Directory.GetFiles(szProjectRoot, "*.csproj").Any())
                    {
                        szProjectRoot = Path.GetDirectoryName(szProjectRoot);
                    }
                    
                    if (szProjectRoot != null)
                    {
                        var szFallbackPath2 = Path.Combine(szProjectRoot, "DatabaseSchema", "DatabaseSchema.json");
                        if (File.Exists(szFallbackPath2))
                        {
                            logger.LogInformation("使用備用路徑2 (專案根目錄): {FallbackPath}", szFallbackPath2);
                            szSchemaPath = szFallbackPath2;
                        }
                        else
                        {
                            logger.LogWarning("所有路徑都找不到資料庫綱要檔，將使用預設配置");
                        }
                    }
                    else
                    {
                        logger.LogWarning("無法找到專案根目錄，將使用預設配置");
                    }
                }
            }
            
            return new DatabaseSchemaService(logger, szSchemaPath);
        });

        // 註冊 MQTT 配置服務為單例 - 智慧路徑偵測
        services.AddSingleton<MqttConfigService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<MqttConfigService>>();
            
            // 使用應用程式基礎目錄作為設定檔路徑
            var szBasePath = AppDomain.CurrentDomain.BaseDirectory;
            var szMqttConfigPath = Path.Combine(szBasePath, "MqttSetting", "MqttSetting.json");
            
            // 記錄路徑偵測結果
            logger.LogInformation("MQTT 配置服務初始化");
            logger.LogInformation("應用程式基礎目錄: {BasePath}", szBasePath);
            logger.LogInformation("MQTT 設定檔路徑: {ConfigPath}", szMqttConfigPath);
            logger.LogInformation("MQTT 設定檔是否存在: {FileExists}", File.Exists(szMqttConfigPath));
            
            // 如果檔案不存在，嘗試備用路徑
            if (!File.Exists(szMqttConfigPath))
            {
                // 備用路徑1: 開發模式的相對路徑
                var szFallbackPath1 = Path.Combine(Directory.GetCurrentDirectory(), "MqttSetting", "MqttSetting.json");
                if (File.Exists(szFallbackPath1))
                {
                    logger.LogInformation("使用備用路徑1: {FallbackPath}", szFallbackPath1);
                    szMqttConfigPath = szFallbackPath1;
                }
                else
                {
                    // 備用路徑2: 專案根目錄
                    var szProjectRoot = Path.GetDirectoryName(szBasePath);
                    while (szProjectRoot != null && !Directory.GetFiles(szProjectRoot, "*.csproj").Any())
                    {
                        szProjectRoot = Path.GetDirectoryName(szProjectRoot);
                    }
                    
                    if (szProjectRoot != null)
                    {
                        var szFallbackPath2 = Path.Combine(szProjectRoot, "MqttSetting", "MqttSetting.json");
                        if (File.Exists(szFallbackPath2))
                        {
                            logger.LogInformation("使用備用路徑2 (專案根目錄): {FallbackPath}", szFallbackPath2);
                            szMqttConfigPath = szFallbackPath2;
                        }
                        else
                        {
                            logger.LogWarning("所有路徑都找不到 MQTT 設定檔，將使用預設配置");
                        }
                    }
                    else
                    {
                        logger.LogWarning("無法找到專案根目錄，將使用預設配置");
                    }
                }
            }
            
            return new MqttConfigService(logger, szMqttConfigPath);
        });

        // 註冊資料庫初始化服務為單例
        services.AddSingleton<DatabaseInitializationService>();

        // 註冊資料存取介面與實作類別為單例
        services.AddSingleton<SqlServerDataRepository>();
        services.AddSingleton<IDataRepository>(provider => provider.GetRequiredService<SqlServerDataRepository>());

        // 註冊資料儲存服務為單例
        services.AddSingleton<HistoryDataStorageService>();
        services.AddSingleton<RealtimeDataStorageService>();

        return services;
    }

    /// <summary>
    /// 初始化資料庫服務
    /// </summary>
    /// <param name="serviceProvider">服務提供者</param>
    /// <returns>初始化任務</returns>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        try
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("正在初始化資料庫服務...");

            // 1. 初始化資料庫結構
            var initService = serviceProvider.GetRequiredService<DatabaseInitializationService>();
            var isSchemaInitialized = await initService.InitializeDatabaseSchemaAsync();
            
            if (isSchemaInitialized)
            {
                logger.LogInformation("資料庫結構初始化成功");
            }
            else
            {
                logger.LogWarning("資料庫結構初始化失敗");
            }

            // 2. 初始化資料存取服務
            var dataRepository = serviceProvider.GetService<IDataRepository>();
            if (dataRepository is SqlServerDataRepository sqlRepository)
            {
                var isRepositoryInitialized = await sqlRepository.InitializeAsync();
                if (isRepositoryInitialized)
                {
                    var isConnected = await dataRepository.TestConnectionAsync();
                    if (isConnected)
                    {
                        logger.LogInformation("資料庫服務初始化成功並連線正常");
                    }
                    else
                    {
                        logger.LogWarning("資料庫服務初始化成功但連線測試失敗");
                    }
                }
                else
                {
                    logger.LogWarning("資料庫存取服務初始化失敗");
                }
            }

            // 3. 初始化資料儲存服務 (強制建立實例以啟動定時器)
            var historyService = serviceProvider.GetRequiredService<HistoryDataStorageService>();
            var realtimeService = serviceProvider.GetRequiredService<RealtimeDataStorageService>();
            logger.LogInformation("資料儲存服務已初始化: 歷史資料服務(每分鐘)和即時資料服務(每5秒)");
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "資料庫初始化過程中發生錯誤");
        }
    }
}