using ScadaEngine.Web.Features.DailyReport.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 能源日報排程背景服務 — 每日 02:00 生成前一日快照並依設定寄送（骨架仿 ElectricityCostAggregationService：
/// 30 秒輪詢 + 去重旗標）。啟動時 catch-up：最近 N 天（預設 3，appsettings DailyReport:CatchUpDays）
/// 缺快照就補生成 — 補生成不補寄（避免半夜當機後隔天連發舊信）；「昨日」由主迴圈處理（含補寄）。
/// 02:00 觸發點在 Engine 水/氣 Hourly 聚合（00:03 完成前一日 23 時）之後，資料完整。
/// </summary>
public class DailyReportSchedulerService : BackgroundService
{
    /// <summary>每日觸發時（02:00 之後的第一個輪詢）</summary>
    private const int TRIGGER_HOUR = 2;

    private readonly ILogger<DailyReportSchedulerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _nCatchUpDays;

    public DailyReportSchedulerService(
        ILogger<DailyReportSchedulerService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _nCatchUpDays = configuration.GetValue<int?>("DailyReport:CatchUpDays") ?? 3;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等 Web 啟動完成（DB schema 同步）再開跑
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 啟動 catch-up：昨日之前的缺快照補生成（不寄）；昨日交給主迴圈（含寄送）
        for (var i = 2; i <= _nCatchUpDays && !stoppingToken.IsCancellationRequested; i++)
        {
            var dtReportDate = DateTime.Today.AddDays(-i);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var reportService = scope.ServiceProvider.GetRequiredService<DailyReportService>();
                if (await reportService.GetSnapshotMetaAsync(dtReportDate) != null) continue;

                var data = await reportService.BuildAsync(dtReportDate);
                await reportService.SaveSnapshotAsync(dtReportDate, data, 0 /* 未寄 — 補生成不補寄 */);
                _logger.LogInformation("日報 catch-up 補生成 ReportDate={ReportDate:yyyy-MM-dd}", dtReportDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "日報 catch-up 補生成失敗 ReportDate={ReportDate:yyyy-MM-dd}", dtReportDate);
            }
        }

        // 主迴圈：02:00 後首次輪詢觸發當日任務（生成 + 寄送昨日日報），同一天只跑一次
        DateTime? dtLastTriggeredDate = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var dtNow = DateTime.Now;
            if (dtNow.Hour >= TRIGGER_HOUR && dtLastTriggeredDate != dtNow.Date)
            {
                await RunDailyAsync(dtNow.Date.AddDays(-1), stoppingToken);
                dtLastTriggeredDate = dtNow.Date;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 生成（若缺）+ 寄送（若啟用且未寄過）指定報告日的日報。
    /// 冪等：MailStatus=1（已寄成功）/ 3（停用）不重寄；=2（失敗）不自動重試（可於設定頁手動測試寄送）。
    /// </summary>
    private async Task RunDailyAsync(DateTime dtReportDate, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<DailyReportService>();
            var mailService = scope.ServiceProvider.GetRequiredService<DailyReportMailService>();

            var setting = await reportService.GetSettingAsync();
            var meta = await reportService.GetSnapshotMetaAsync(dtReportDate);

            DailyReportData? data;
            if (meta == null)
            {
                data = await reportService.BuildAsync(dtReportDate);
                await reportService.SaveSnapshotAsync(dtReportDate, data, 0);
                _logger.LogInformation("日報快照生成完成 ReportDate={ReportDate:yyyy-MM-dd}", dtReportDate);
                meta = await reportService.GetSnapshotMetaAsync(dtReportDate);
            }
            else
            {
                data = await reportService.GetSnapshotDataAsync(dtReportDate);
            }

            if (meta == null || data == null)
            {
                _logger.LogWarning("日報快照讀取失敗，跳過寄送 ReportDate={ReportDate:yyyy-MM-dd}", dtReportDate);
                return;
            }

            if (!setting.isMailEnabled)
            {
                if (meta.nMailStatus == 0)
                    await reportService.UpdateMailStatusAsync(dtReportDate, 3, "寄送停用");
                return;
            }

            if (meta.nMailStatus != 0) return; // 已寄成功 / 已標失敗 / 停用 → 不重寄

            if (stoppingToken.IsCancellationRequested) return;
            var recipients = await reportService.GetRecipientsAsync();
            var result = await mailService.SendAsync(data, setting, recipients, isTest: false);
            await reportService.UpdateMailStatusAsync(dtReportDate, result.nMailStatus, result.szDetail);
            _logger.LogInformation("日報寄送完成 ReportDate={ReportDate:yyyy-MM-dd} {Detail}", dtReportDate, result.szDetail);
        }
        catch (OperationCanceledException)
        {
            // 關機中止 — 下次啟動 catch-up / 主迴圈補
        }
        catch (Exception ex)
        {
            // 單次失敗不重試（避免 30 秒輪詢洗版），下次重啟由 catch-up / 主迴圈補
            _logger.LogError(ex, "日報排程執行失敗 ReportDate={ReportDate:yyyy-MM-dd}", dtReportDate);
        }
    }
}
