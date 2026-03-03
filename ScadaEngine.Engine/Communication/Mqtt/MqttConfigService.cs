using System.Text.Json;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;
namespace ScadaEngine.Engine.Communication.Mqtt;

/// <summary>
/// MQTT 配置服務，負責載入和管理 MQTT 設定檔案
/// </summary>
public class MqttConfigService
{
    private readonly ILogger<MqttConfigService> _logger;
    private readonly string _szConfigPath;
    
    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="szConfigPath">設定檔路徑（預設為 "./Mqtt/MqttSetting.json"）</param>
    public MqttConfigService(ILogger<MqttConfigService> logger, string szConfigPath = "./Mqtt/MqttSetting.json")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _szConfigPath = szConfigPath;
    }

    /// <summary>
    /// 載入 MQTT 配置設定
    /// </summary>
    /// <returns>MQTT 配置模型，載入失敗時回傳預設配置</returns>
    public async Task<MqttSettingModel> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_szConfigPath))
            {
                _logger.LogWarning("MQTT 設定檔案不存在: {ConfigPath}，使用預設配置", _szConfigPath);
                return CreateDefaultConfig();
            }

            var szJsonContent = await File.ReadAllTextAsync(_szConfigPath);
            
            if (string.IsNullOrWhiteSpace(szJsonContent))
            {
                _logger.LogWarning("MQTT 設定檔案為空，使用預設配置");
                return CreateDefaultConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var mqttSetting = JsonSerializer.Deserialize<MqttSettingModel>(szJsonContent, options);
            
            if (mqttSetting == null)
            {
                _logger.LogError("無法解析 MQTT 設定檔案，使用預設配置");
                return CreateDefaultConfig();
            }

            // 驗證必要參數
            if (ValidateConfig(mqttSetting))
            {
                _logger.LogInformation("成功載入 MQTT 配置: Broker={BrokerIp}:{Port}, ClientId={ClientId}", 
                    mqttSetting.MqttConfig.szBrokerIp, 
                    mqttSetting.MqttConfig.nPort, 
                    mqttSetting.MqttConfig.szClientId);
                return mqttSetting;
            }
            else
            {
                _logger.LogWarning("MQTT 配置驗證失敗，使用預設配置");
                return CreateDefaultConfig();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 MQTT 設定檔案時發生 JSON 格式錯誤");
            return CreateDefaultConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入 MQTT 設定檔案時發生錯誤");
            return CreateDefaultConfig();
        }
    }

    /// <summary>
    /// 驗證 MQTT 配置的有效性
    /// </summary>
    /// <param name="mqttSetting">MQTT 設定模型</param>
    /// <returns>配置有效回傳 true，無效回傳 false</returns>
    private bool ValidateConfig(MqttSettingModel mqttSetting)
    {
        if (mqttSetting.MqttConfig == null)
        {
            _logger.LogError("MQTT 配置區段不存在");
            return false;
        }

        if (string.IsNullOrWhiteSpace(mqttSetting.MqttConfig.szBrokerIp))
        {
            _logger.LogError("MQTT Broker IP 不可為空");
            return false;
        }

        if (mqttSetting.MqttConfig.nPort <= 0 || mqttSetting.MqttConfig.nPort > 65535)
        {
            _logger.LogError("MQTT 通訊埠範圍無效: {Port}", mqttSetting.MqttConfig.nPort);
            return false;
        }

        if (string.IsNullOrWhiteSpace(mqttSetting.MqttConfig.szClientId))
        {
            _logger.LogError("MQTT 客戶端 ID 不可為空");
            return false;
        }

        if (string.IsNullOrWhiteSpace(mqttSetting.MqttConfig.szBaseTopic))
        {
            _logger.LogError("MQTT 基礎主題不可為空");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 建立預設配置
    /// </summary>
    /// <returns>預設 MQTT 設定模型</returns>
    private MqttSettingModel CreateDefaultConfig()
    {
        return new MqttSettingModel
        {
            MqttConfig = new MqttConfigModel
            {
                szBrokerIp = "127.0.0.1",
                nPort = 1883,
                szClientId = "SCADA_Main_Engine",
                szBaseTopic = "SCADA/Realtime",
                isRetain = true
            },
            ConnectionStrings = new ConnectionStringModel
            {
                szDefaultConnection = "Server=localhost;Database=ScadaDB;User Id=sa;Password=your_password;"
            }
        };
    }

    /// <summary>
    /// 儲存 MQTT 配置至檔案
    /// </summary>
    /// <param name="mqttSetting">MQTT 設定模型</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    public async Task<bool> SaveConfigAsync(MqttSettingModel mqttSetting)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var szJsonContent = JsonSerializer.Serialize(mqttSetting, options);

            // 確保目錄存在
            var szDirectory = Path.GetDirectoryName(_szConfigPath);
            if (!string.IsNullOrEmpty(szDirectory) && !Directory.Exists(szDirectory))
            {
                Directory.CreateDirectory(szDirectory);
            }

            await File.WriteAllTextAsync(_szConfigPath, szJsonContent);
            
            _logger.LogInformation("MQTT 配置已儲存至: {ConfigPath}", _szConfigPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存 MQTT 配置時發生錯誤");
            return false;
        }
    }
}