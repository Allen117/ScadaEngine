using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Mqtt.Extensions;

/// <summary>
/// MQTT 服務依賴注入擴充方法
/// </summary>
public static class MqttServiceExtensions
{
    /// <summary>
    /// 註冊 MQTT 相關服務至依賴注入容器
    /// </summary>
    /// <param name="services">服務集合</param>
    /// <returns>服務集合</returns>
    public static IServiceCollection AddMqttServices(this IServiceCollection services)
    {
        // 註冊 MQTT 配置服務為單例
        services.AddSingleton<MqttConfigService>();

        // 註冊 MQTT 發布服務為單例
        services.AddSingleton<MqttPublishService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<MqttPublishService>>();
            var configService = provider.GetRequiredService<MqttConfigService>();
            
            // 載入 MQTT 配置
            var mqttSetting = configService.LoadConfigAsync().GetAwaiter().GetResult();
            
            return new MqttPublishService(logger, mqttSetting.MqttConfig);
        });

        // 註冊 MQTT 控制訂閱服務為背景服務
        services.AddHostedService<MqttControlSubscribeService>();

        return services;
    }

    /// <summary>
    /// 初始化 MQTT 連線
    /// </summary>
    /// <param name="serviceProvider">服務提供者</param>
    /// <returns>初始化任務</returns>
    public static async Task InitializeMqttServiceAsync(this IServiceProvider serviceProvider)
    {
        try
        {
            var mqttService = serviceProvider.GetService<MqttPublishService>();
            if (mqttService != null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<MqttPublishService>>();
                logger.LogInformation("正在初始化 MQTT 連線...");
                
                var isInitialized = await mqttService.InitializeAsync();
                if (isInitialized)
                {
                    logger.LogInformation("MQTT 連線初始化成功");
                }
                else
                {
                    logger.LogWarning("MQTT 連線初始化失敗，將在無 MQTT 模式下運行");
                }
            }
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "MQTT 初始化過程中發生錯誤");
        }
    }
}