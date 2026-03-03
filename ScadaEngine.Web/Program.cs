using Microsoft.AspNetCore.Authentication.Cookies;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Data.Services;
using ScadaEngine.Engine.Data.Repositories;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Web.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog 日誌
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// 配置視圖引擎以支援 Features 資料夾結構
builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
{
    options.ViewLocationFormats.Clear();
    options.ViewLocationFormats.Add("/Views/{1}/{0}.cshtml");
    options.ViewLocationFormats.Add("/Views/Shared/{0}.cshtml");
    options.ViewLocationFormats.Add("/Features/{1}/Views/{0}.cshtml");
});

// 配置 Cookie 認證
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Index";
        options.LogoutPath = "/Login/Logout";
        options.AccessDeniedPath = "/Login/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
        options.Cookie.Name = "ScadaAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// 註冊來自 Engine 專案的資料庫服務（用於登入功能）
builder.Services.AddSingleton<DatabaseConfigService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<DatabaseConfigService>>();
    // 指向 Engine 專案的資料庫設定檔
    var engineDbConfigPath = Path.Combine("..", "ScadaEngine.Engine", "Setting", "dbSetting.json");
    return new DatabaseConfigService(logger, engineDbConfigPath);
});

builder.Services.AddSingleton<DatabaseSchemaService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<DatabaseSchemaService>>();
    // 指向 Engine 專案的資料庫架構檔
    var engineSchemaPath = Path.Combine("..", "ScadaEngine.Engine", "DatabaseSchema", "DatabaseSchema.json");
    return new DatabaseSchemaService(logger, engineSchemaPath);
});
builder.Services.AddSingleton<DatabaseInitializationService>();
builder.Services.AddScoped<IDataRepository, SqlServerDataRepository>();

// 註冊 Engine 專案的通訊服務配置服務（Web 即時監控需要）
builder.Services.AddSingleton<MqttConfigService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<MqttConfigService>>();
    // 使用 Web 專案自己的 MQTT 設定檔
    var webMqttConfigPath = Path.Combine("MqttSetting", "MqttSetting.json");
    return new MqttConfigService(logger, webMqttConfigPath);
});
// Web 專案不需要 ModbusConfigService，僅訂閱 MQTT 即時資料
// builder.Services.AddSingleton<ModbusConfigService>(...);

// 暫時註釋通訊服務，避免啟動時死鎖
/*
// 註冊通訊服務 (延遲實例化，避免啟動時立即連線)
builder.Services.AddTransient<MqttPublishService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<MqttPublishService>>();
    var mqttConfigService = serviceProvider.GetRequiredService<MqttConfigService>();
    // 移除同步等待，改為在使用時才載入配置
    return new MqttPublishService(logger, null);
});

builder.Services.AddTransient<MqttControlSubscribeService>();
builder.Services.AddTransient<ModbusCollectionManager>();
*/

//註冊Web專用服務（用於登入功能與即時監控）
builder.Services.AddScoped<ScadaEngine.Web.Services.WebDatabaseService>();

// 註冊即時監控 MQTT 訂閱服務
builder.Services.AddSingleton<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>(provider => 
    provider.GetRequiredService<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>());

var app = builder.Build();

Console.WriteLine("正在啟動 SCADA Web 應用程式...");

try
{
    Console.WriteLine("配置 HTTP 請求管道...");
    
    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    Console.WriteLine("設置中介軟體...");
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    // 認證和授權中介軟體
    app.UseAuthentication();
    app.UseAuthorization();

    Console.WriteLine("設置路由...");
    // 根路徑重導向至登入頁
    app.MapGet("/", () => Results.Redirect("/Login"));

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Login}/{action=Index}/{id?}");

    Console.WriteLine("準備啟動 Web 伺服器...");
    Console.WriteLine("應用程式將在以下 URL 可用:");
    Console.WriteLine("- HTTP: http://localhost:5038");
    Console.WriteLine("- HTTPS: https://localhost:7189");
    Console.WriteLine("啟動 Web 伺服器...");

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"啟動應用程式時發生錯誤: {ex.Message}");
    Console.WriteLine($"完整錯誤信息: {ex}");
    throw;
}
