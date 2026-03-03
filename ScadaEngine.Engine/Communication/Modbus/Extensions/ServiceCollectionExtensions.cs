using ScadaEngine.Engine.Communication.Modbus.Services;

namespace ScadaEngine.Engine.Communication.Modbus.Extensions;

/// <summary>
/// Modbus 服務依賴注入擴充方法
/// </summary>
public static class ModbusServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Modbus 相關服務到 DI 容器
    /// </summary>
    /// <param name="services">服務集合</param>
    /// <returns>服務集合</returns>
    public static IServiceCollection AddModbusServices(this IServiceCollection services)
    {
        // 註冊 Modbus 設定檔服務 (單例模式)
        services.AddSingleton<ModbusConfigService>();

        // 註冊 Modbus 採集管理器 (單例模式)
        services.AddSingleton<ModbusCollectionManager>();

        return services;
    }
}