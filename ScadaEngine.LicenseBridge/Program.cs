using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ScadaEngine.LicenseBridge.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "Log", "bridge-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(o => o.ServiceName = "ScadaEngineLicense")
        .UseSerilog((ctx, services, cfg) => cfg
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "Log", "bridge-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7))
        .ConfigureServices(services =>
        {
            services.AddHostedService<HaspVerificationService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "License Bridge 啟動失敗");
}
finally
{
    Log.CloseAndFlush();
}
