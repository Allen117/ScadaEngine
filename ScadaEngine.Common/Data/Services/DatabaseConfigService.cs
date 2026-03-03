using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Common.Data.Services;

/// <summary>
/// 資料庫配置服務，負責載入和管理資料庫設定檔案
/// 此為共用服務，可供 Engine、Web 和 Algorithm 專案使用
/// </summary>
public class DatabaseConfigService
{
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly string _szConfigPath;
    
    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="szConfigPath">設定檔路徑（預設為 "./Setting/dbSetting.json"）</param>
    public DatabaseConfigService(ILogger<DatabaseConfigService> logger, string szConfigPath = "./Setting/dbSetting.json")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _szConfigPath = szConfigPath;
    }

    /// <summary>
    /// 載入資料庫配置設定
    /// </summary>
    /// <returns>資料庫配置模型，載入失敗時回傳預設配置</returns>
    public async Task<DatabaseConfigModel> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_szConfigPath))
            {
                _logger.LogWarning("資料庫設定檔案不存在: {ConfigPath}，使用預設配置", _szConfigPath);
                return CreateDefaultConfig();
            }

            var szJsonContent = await File.ReadAllTextAsync(_szConfigPath);
            
            if (string.IsNullOrWhiteSpace(szJsonContent))
            {
                _logger.LogWarning("資料庫設定檔案為空，使用預設配置");
                return CreateDefaultConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
                // 移除 PropertyNamingPolicy，使用 JsonPropertyName 屬性直接對應
            };

            var dbConfig = JsonSerializer.Deserialize<DatabaseConfigModel>(szJsonContent, options);
            
            if (dbConfig == null)
            {
                _logger.LogError("無法解析資料庫設定檔案，使用預設配置");
                return CreateDefaultConfig();
            }

            // 驗證必要參數
            if (dbConfig.IsValid())
            {
                _logger.LogInformation("成功載入資料庫配置: Server={DatabaseAddress}, Database={DataBaseName}, Account={DataBaseAccount}", 
                    dbConfig.szDatabaseAddress, 
                    dbConfig.szDataBaseName, 
                    dbConfig.szDataBaseAccount);
                return dbConfig;
            }
            else
            {
                _logger.LogWarning("資料庫配置驗證失敗，使用預設配置");
                return CreateDefaultConfig();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析資料庫設定檔案時發生 JSON 格式錯誤");
            return CreateDefaultConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入資料庫設定檔案時發生錯誤");
            return CreateDefaultConfig();
        }
    }

    /// <summary>
    /// 建立預設資料庫配置
    /// </summary>
    /// <returns>預設資料庫配置模型</returns>
    private DatabaseConfigModel CreateDefaultConfig()
    {
        return new DatabaseConfigModel
        {
            szDatabaseAddress = "127.0.0.1",
            szDataBaseName = "wsnCsharp",
            szDataBaseAccount = "wsn1",
            szDataBasePassword = "wsn2"
        };
    }

    /// <summary>
    /// 儲存資料庫配置至檔案
    /// </summary>
    /// <param name="dbConfig">資料庫配置模型</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveConfigAsync(DatabaseConfigModel dbConfig)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
                // 使用 JsonPropertyName 屬性保持 JSON 格式一致性
            };

            var szJsonContent = JsonSerializer.Serialize(dbConfig, options);

            // 確保目錄存在
            var szDirectory = Path.GetDirectoryName(_szConfigPath);
            if (!string.IsNullOrEmpty(szDirectory) && !Directory.Exists(szDirectory))
            {
                Directory.CreateDirectory(szDirectory);
            }

            await File.WriteAllTextAsync(_szConfigPath, szJsonContent);
            
            _logger.LogInformation("資料庫配置已儲存至: {ConfigPath}", _szConfigPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存資料庫配置時發生錯誤");
            return false;
        }
    }

    /// <summary>
    /// 取得連線字串
    /// </summary>
    /// <returns>資料庫連線字串</returns>
    public async Task<string> GetConnectionStringAsync()
    {
        var config = await LoadConfigAsync();
        return config.BuildConnectionString();
    }
}