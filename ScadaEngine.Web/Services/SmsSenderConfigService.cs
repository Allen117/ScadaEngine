using System.Text.Json;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Web.Features.AlarmSetting.Models;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// SmsSetting.json 讀寫服務 — Web UI 編輯簡訊盒設定時直接寫 JSON 檔
    /// 與 Engine 共用同一份 JSON（透過相對路徑 ../ScadaEngine.Engine/Setting/）
    /// 注意：Engine 僅啟動時載入此檔，改 COM port / 啟用開關需重啟 Engine 才生效
    ///（DailyQuota / RatePerMinute 亦同；DB 收訊號碼則 60 秒內熱生效）
    /// </summary>
    public class SmsSenderConfigService
    {
        private readonly ILogger<SmsSenderConfigService> _logger;
        private readonly object _fileLock = new();

        public SmsSenderConfigService(ILogger<SmsSenderConfigService> logger)
        {
            _logger = logger;
        }

        public SmsSenderConfigDto Read()
        {
            var setting = LoadFromFile();
            return new SmsSenderConfigDto
            {
                enableNotification = setting.EnableNotification,
                comPort = setting.ComPort,
                baudRate = setting.BaudRate,
                simPin = setting.SimPin,
                ratePerMinute = setting.RatePerMinute,
                dailyQuota = setting.DailyQuota,
                sendRecovery = setting.SendRecovery,
                testSendThrottleSeconds = setting.TestSendThrottleSeconds
            };
        }

        public bool Save(SmsSenderConfigDto dto)
        {
            try
            {
                lock (_fileLock)
                {
                    var setting = new SmsSettingModel
                    {
                        EnableNotification = dto.enableNotification,
                        ComPort = string.IsNullOrWhiteSpace(dto.comPort) ? "auto" : dto.comPort.Trim(),
                        BaudRate = dto.baudRate >= 0 ? dto.baudRate : 0,
                        SimPin = (dto.simPin ?? string.Empty).Trim(),
                        RatePerMinute = dto.ratePerMinute > 0 ? dto.ratePerMinute : 10,
                        DailyQuota = dto.dailyQuota >= 0 ? dto.dailyQuota : 100,
                        SendRecovery = dto.sendRecovery,
                        TestSendThrottleSeconds = dto.testSendThrottleSeconds > 0 ? dto.testSendThrottleSeconds : 10
                    };

                    var szPath = ResolvePath();
                    var szJson = JsonSerializer.Serialize(setting, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(szPath, szJson);
                    _logger.LogInformation("已更新 SmsSetting.json: {Path}", szPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 SmsSetting.json 失敗");
                return false;
            }
        }

        /// <summary>給其他 Web 服務取用完整設定 — 測試發送 throttle 秒數等</summary>
        public SmsSettingModel LoadFromFile()
        {
            try
            {
                lock (_fileLock)
                {
                    var szPath = ResolvePath();
                    if (!File.Exists(szPath))
                    {
                        _logger.LogWarning("找不到 SmsSetting.json，使用預設值: {Path}", szPath);
                        return new SmsSettingModel { EnableNotification = false };
                    }
                    var szJson = File.ReadAllText(szPath);
                    var setting = JsonSerializer.Deserialize<SmsSettingModel>(
                        szJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return setting ?? new SmsSettingModel { EnableNotification = false };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 SmsSetting.json 失敗");
                return new SmsSettingModel { EnableNotification = false };
            }
        }

        private static string ResolvePath()
        {
            // 優先使用本地 Setting/，開發時 fallback 到 Engine 專案
            var szLocal = Path.Combine(AppContext.BaseDirectory, "Setting", "SmsSetting.json");
            if (File.Exists(szLocal)) return szLocal;
            var szEngine = Path.Combine("..", "ScadaEngine.Engine", "Setting", "SmsSetting.json");
            if (File.Exists(szEngine)) return szEngine;
            return szEngine;
        }
    }
}
