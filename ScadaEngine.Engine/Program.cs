using ScadaEngine.Engine;
using ScadaEngine.Engine.Services;
using ScadaEngine.Engine.Communication.Modbus.Extensions;
using ScadaEngine.Engine.Communication.Mqtt.Extensions;
using ScadaEngine.Engine.Data.Extensions;
using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.DependencyInjection;
// 檢查是否以 Windows Service 運行
var isWindowsService = WindowsServiceHelpers.IsWindowsService();

// 配置 Serilog，Windows Service 時使用檔案日誌
var logConfigBuilder = new LoggerConfiguration();
if (isWindowsService)
{
    // Windows Service 模式：主要使用檔案日誌
    logConfigBuilder
        .WriteTo.File(
            path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log", "ScadaEngine-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.File(
            path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log", "ScadaEngine-Error-.log"),
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 90
        )
        .MinimumLevel.Information();
}
else
{
    // 開發模式：使用設定檔 + Console
    logConfigBuilder.ReadFrom.Configuration(Host.CreateApplicationBuilder(args).Configuration);
}

Log.Logger = logConfigBuilder.CreateLogger();

try
{
    Log.Information("SCADA 引擎服務啟動中... (模式: {ServiceMode})", isWindowsService ? "Windows Service" : "Console Application");

    var builder = Host.CreateApplicationBuilder(args);

    // 配置 Windows Service 支援
    if (isWindowsService)
    {
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SCADA Engine Service";
        });
    }

    // 使用 Serilog 作為日誌提供者
    builder.Services.AddSerilog();

    // 註冊 Worker 背景服務
    builder.Services.AddHostedService<Worker>();

    // 註冊 Modbus 相關服務
    builder.Services.AddModbusServices();

    // 註冊 MQTT 相關服務
    builder.Services.AddMqttServices();

    // 註冊資料庫相關服務 (包含儲存服務)
    builder.Services.AddDataServices();

    // 註冊條件控制服務
    builder.Services.AddHostedService<ConditionControlService>();

    var host = builder.Build();

    // 初始化資料庫服務
    await host.Services.InitializeDatabaseAsync();

    // 初始化 MQTT 連線
    await host.Services.InitializeMqttServiceAsync();

    Log.Information("SCADA 引擎服務初始化完成，開始運行");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SCADA 引擎服務啟動失敗");
}
finally
{
    Log.Information("SCADA 引擎服務已停止");
    await Log.CloseAndFlushAsync();
}
