namespace ScadaEngine.Web.Services;

/// <summary>
/// 電費逐時計價背景服務（Web 端）。
/// 啟動時 catch-up 近 N 天（appsettings ElectricityCost:CatchUpDays，預設 7），
/// 主迴圈每 30 秒檢查是否到 XX:05（晚於 Engine 葉子聚合的 XX:02），
/// 到達就以 UPSERT 語意重算近 48 小時 rolling window（吸收 Engine 的 hourly 回填）。
/// 未選採用方案（szActivePlanId 空）時不計算，選定後下一輪自動生效。
/// Singleton BackgroundService — ElectricityCostService 為 Scoped，透過 CreateScope 取用。
/// </summary>
public class ElectricityCostAggregationService : BackgroundService
{
    /// <summary>觸發時點（每小時第 5 分鐘）— 晚於 Engine EnergyLeafHourly 的 XX:02</summary>
    private const int TRIGGER_MINUTE = 5;

    /// <summary>每輪重算的 rolling window（小時）— 吸收 Engine catch-up 回填的舊小時</summary>
    private const int ROLLING_WINDOW_HOURS = 48;

    private readonly ILogger<ElectricityCostAggregationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _nCatchUpDays;

    public ElectricityCostAggregationService(
        ILogger<ElectricityCostAggregationService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _nCatchUpDays = configuration.GetValue<int?>("ElectricityCost:CatchUpDays") ?? 7;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "電費逐時計價服務啟動，CatchUpDays={CatchUp}, RollingWindowHours={Window}, TriggerMinute=XX:{Min:D2}",
            _nCatchUpDays, ROLLING_WINDOW_HOURS, TRIGGER_MINUTE);

        // 等 Web 啟動完（DB schema 同步等）再跑 catch-up
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // 啟動 catch-up：近 N 天（更早的靠 TariffSetting 頁重算按鈕）
        if (_nCatchUpDays > 0)
        {
            try
            {
                var dtNow = DateTime.Now;
                var dtCurrentHour = new DateTime(dtNow.Year, dtNow.Month, dtNow.Day, dtNow.Hour, 0, 0);
                await RecalculateAsync(dtCurrentHour.AddDays(-_nCatchUpDays), dtCurrentHour, "catch-up", stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "電費計價啟動 catch-up 失敗");
            }
        }

        // 主迴圈：每 30 秒檢查是否到 XX:05
        DateTime? dtLastTriggeredHour = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dtNow = DateTime.Now;
                if (dtNow.Minute >= TRIGGER_MINUTE)
                {
                    var dtTargetHour = new DateTime(dtNow.Year, dtNow.Month, dtNow.Day, dtNow.Hour, 0, 0);
                    if (dtLastTriggeredHour != dtTargetHour)
                    {
                        await RecalculateAsync(dtTargetHour.AddHours(-ROLLING_WINDOW_HOURS), dtTargetHour, "hourly", stoppingToken);
                        dtLastTriggeredHour = dtTargetHour;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "電費計價主迴圈發生錯誤");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("電費逐時計價服務已停止");
    }

    private async Task RecalculateAsync(DateTime dtFrom, DateTime dtToExclusive, string szReason, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var costService = scope.ServiceProvider.GetRequiredService<ElectricityCostService>();

        var result = await costService.RecalculateRangeAsync(dtFrom, dtToExclusive, ct);
        if (!result.isSuccess)
        {
            // no_active_plan 為正常狀態（尚未選方案）— 只記 Debug 避免每小時洗版
            if (result.szError == "no_active_plan")
                _logger.LogDebug("電費計價（{Reason}）跳過：尚未選擇採用方案", szReason);
            else
                _logger.LogWarning("電費計價（{Reason}）失敗：{Error}", szReason, result.szError);
            return;
        }

        _logger.LogInformation(
            "電費計價（{Reason}）完成：{From:MM-dd HH:00} ~ {To:MM-dd HH:00}，{Hours} 小時、{Rows} 列",
            szReason, result.dtFrom, result.dtToExclusive, result.nHours, result.nRows);
    }
}
