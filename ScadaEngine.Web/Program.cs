using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Data.Services;
using ScadaEngine.Engine.Data.Repositories;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Web.Services;
using Serilog;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// 支援以 Windows Service 模式執行
builder.Host.UseWindowsService();

// 設定監聽 URL（部署時使用，launchSettings.json 只在開發時有效）
builder.WebHost.UseUrls("http://0.0.0.0:5038");

// 配置 Serilog 日誌
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);
});

// 多語系：resx 集中放在 Resources/，IStringLocalizer / IViewLocalizer / DataAnnotations 都吃這份
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Add services to the container.
builder.Services
    .AddControllersWithViews(options =>
    {
        options.Filters.Add<ScadaEngine.Web.Filters.PageAccessFilter>();
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(ScadaEngine.Web.Resources.SharedResource));
    });

// 支援的語系（zh-TW 為預設，缺 key 時 fallback 至 zh-TW）
var aSupportedCultures = new[]
{
    new CultureInfo("zh-TW"),
    new CultureInfo("en")
};
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("zh-TW", "zh-TW");
    options.SupportedCultures = aSupportedCultures;
    options.SupportedUICultures = aSupportedCultures;
    options.FallBackToParentUICultures = true;

    // Provider 順序：Cookie → QueryString → AcceptLanguage
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
});

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

// 全域授權後備：預設所有端點都要登入，未標 [AllowAnonymous] 者一律擋
// （一次堵住所有「忘了加 [Authorize]」的匿名破口；白名單：Login / I18n / Setup / 靜態檔）
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// 註冊來自 Engine 專案的資料庫服務（用於登入功能）
builder.Services.AddSingleton<DatabaseConfigService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<DatabaseConfigService>>();
    // 優先使用本地 Setting/，部署時由腳本複製；開發時 fallback 到 Engine 專案
    var localDbConfig = Path.Combine(AppContext.BaseDirectory, "Setting", "dbSetting.json");
    var engineDbConfig = Path.Combine("..", "ScadaEngine.Engine", "Setting", "dbSetting.json");
    var dbConfigPath = File.Exists(localDbConfig) ? localDbConfig : engineDbConfig;
    return new DatabaseConfigService(logger, dbConfigPath);
});

builder.Services.AddSingleton<DatabaseSchemaService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<DatabaseSchemaService>>();
    var localSchema = Path.Combine(AppContext.BaseDirectory, "DatabaseSchema", "DatabaseSchema.json");
    var engineSchema = Path.Combine("..", "ScadaEngine.Engine", "DatabaseSchema", "DatabaseSchema.json");
    var schemaPath = File.Exists(localSchema) ? localSchema : engineSchema;
    return new DatabaseSchemaService(logger, schemaPath);
});
builder.Services.AddSingleton<DatabaseInitializationService>();
builder.Services.AddScoped<IDataRepository, SqlServerDataRepository>();

// 註冊 Engine 專案的通訊服務配置服務（Web 即時監控需要）
builder.Services.AddSingleton<MqttConfigService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<MqttConfigService>>();
    var localMqttConfig = Path.Combine(AppContext.BaseDirectory, "MqttSetting", "MqttSetting.json");
    var devMqttConfig = Path.Combine("MqttSetting", "MqttSetting.json");
    var mqttConfigPath = File.Exists(localMqttConfig) ? localMqttConfig : devMqttConfig;
    return new MqttConfigService(logger, mqttConfigPath);
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
builder.Services.AddScoped<ScadaEngine.Web.Services.EventLogService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.LogicFlowService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.AlarmRuleService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.AccountSettingService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.CalcPointService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.ScheduleSettingService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.LineTargetService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.EmailGroupService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.EnergyCircuitService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.WaterCircuitService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.EnergyReportService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.RefrigerationTonReportService>();
builder.Services.AddScoped<ScadaEngine.Web.Services.DbCoordinatorService>();
// OPC UA 來源設定（Scoped：依賴 IDataRepository 與 IStringLocalizer）
builder.Services.AddScoped<ScadaEngine.Web.Services.OpcUaCoordinatorService>();
// Designer 列範本 JSON 讀寫（Singleton：內含 SemaphoreSlim 檔案鎖）
builder.Services.AddSingleton<ScadaEngine.Web.Services.DesignerTemplateService>();

// Modbus 點位熱編輯 — 讀寫 Engine 執行目錄 Modbus JSON（原子替換，Engine watcher 自動重載）
builder.Services.AddSingleton<ScadaEngine.Web.Services.ModbusConfigFileService>();
// Scoped：依賴 IStringLocalizer<T>（Scoped），且 exporter 本身無狀態
builder.Services.AddScoped<ScadaEngine.Web.Services.EnergyReportExcelExporter>();
builder.Services.AddScoped<ScadaEngine.Web.Services.RefrigerationTonReportExcelExporter>();

// Line 測試發送（內含 throttle 字典 → 必須 Singleton 才能跨請求保留狀態）
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ScadaEngine.Web.Services.LineTestSendService>();

// Email 設定檔讀寫 + 測試寄送（Singleton：throttle 字典需跨請求保留）
builder.Services.AddSingleton<ScadaEngine.Web.Services.EmailSenderConfigService>();
builder.Services.AddSingleton<ScadaEngine.Web.Services.EmailTestSendService>();

// 註冊 C# 演算法服務（供 LogicFlow 前端預覽用）
builder.Services.AddSingleton<ScadaEngine.Engine.Services.CSharpAlgorithmService>();

// 多語系：把 .resx 打包成 JSON 給前端 i18n.js（Singleton 內部有 ConcurrentDictionary 快取）
builder.Services.AddSingleton<ScadaEngine.Web.Services.I18nResourceService>();

// 警報訊息 i18n 翻譯器（依當前 culture 套 messageKey + args → 顯示字串）
builder.Services.AddScoped<ScadaEngine.Web.Services.AlarmMessageLocalizer>();

// ScadaPage 控制動作 EventLog 寫入器（EventType=3 資訊）
builder.Services.AddScoped<ScadaEngine.Web.Services.ControlEventLogger>();

// 授權狀態快取（Singleton — 由 MqttRealtimeSubscriberService 更新，Controller/Layout 讀取）
builder.Services.AddSingleton<ScadaEngine.Web.Services.LicenseStatusCache>();

// 註冊即時監控 MQTT 訂閱服務
builder.Services.AddSingleton<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>(provider =>
    provider.GetRequiredService<ScadaEngine.Web.Services.MqttRealtimeSubscriberService>());

// 註冊未恢復警報 MQTT 訂閱服務（雙註冊：Singleton + HostedService）
builder.Services.AddSingleton<ScadaEngine.Web.Services.MqttAlarmSubscriberService>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.MqttAlarmSubscriberService>(provider =>
    provider.GetRequiredService<ScadaEngine.Web.Services.MqttAlarmSubscriberService>());

// 註冊警報規則 Reload 發布者（雙註冊：Singleton 供 AlarmRuleService 注入呼叫 + HostedService 啟動時連 broker）
builder.Services.AddSingleton<ScadaEngine.Web.Services.AlarmRuleReloadPublisher>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.AlarmRuleReloadPublisher>(provider =>
    provider.GetRequiredService<ScadaEngine.Web.Services.AlarmRuleReloadPublisher>());

// 註冊 DB 來源 Reload 發布者（雙註冊：Singleton 供 Controller 注入呼叫 + HostedService 啟動時連 broker）
builder.Services.AddSingleton<ScadaEngine.Web.Services.DbCoordinatorReloadPublisher>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.DbCoordinatorReloadPublisher>(provider =>
    provider.GetRequiredService<ScadaEngine.Web.Services.DbCoordinatorReloadPublisher>());

// 註冊 OPC UA 來源 Reload 發布者（雙註冊：Singleton 供 Controller 注入呼叫 + HostedService 啟動時連 broker）
builder.Services.AddSingleton<ScadaEngine.Web.Services.OpcUaReloadPublisher>();
builder.Services.AddHostedService<ScadaEngine.Web.Services.OpcUaReloadPublisher>(provider =>
    provider.GetRequiredService<ScadaEngine.Web.Services.OpcUaReloadPublisher>());

var app = builder.Build();

Console.WriteLine("正在啟動 SCADA Web 應用程式...");

// 啟動時同步 DB 結構（schema-driven 建表 + 補缺欄位）— 消滅 Engine/Web 部署順序依賴，誰先重啟誰補。
// 失敗僅 log 不擋啟動（DB 連不上時 Web 照常起，之後 Engine 或手動初始化可補）
try
{
    var dbInitLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var dbInitService = app.Services.GetRequiredService<DatabaseInitializationService>();
    var isDbSchemaSynced = await dbInitService.InitializeDatabaseSchemaAsync();
    if (!isDbSchemaSynced)
    {
        dbInitLogger.LogWarning("Web 啟動時資料庫結構同步失敗（不擋啟動）— 請確認 DB 連線與 DatabaseSchema.json");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Web 啟動時資料庫結構同步發生錯誤（不擋啟動）: {ex.Message}");
}

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

    // 多語系中介軟體（位置：UseRouting 之後、UseAuthentication 之前）
    var localizationOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
    app.UseRequestLocalization(localizationOptions);

    // 認證和授權中介軟體
    app.UseAuthentication();
    app.UseAuthorization();

    Console.WriteLine("設置路由...");
    // 根路徑重導向至登入頁（未登入也可達，交由 Login 流程處理）
    app.MapGet("/", () => Results.Redirect("/Login")).AllowAnonymous();

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
