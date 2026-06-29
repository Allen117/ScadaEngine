using Microsoft.Extensions.DependencyInjection;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 需量計算背景服務。
/// 每分鐘對齊整分鐘計算各電表迴路 15min 滑動時間加權平均功率（kW），
/// 結果 UPSERT 至 DemandData 表供監控預警使用。
///
/// 設計重點：
/// - 採 step-hold TWA：每個樣本值保持到下一個樣本，最後一段延伸到窗口尾
/// - Quality=1 的樣本數 &lt; 5 → DemandKW=0, Quality=0（資料不足）
/// - 公開 ReloadDemandSidsAsync() 供外部 MQTT Reload Subscriber 呼叫
/// </summary>
public class DemandCalculatorService : BackgroundService
{
    private readonly ILogger<DemandCalculatorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private List<string> _demandSids = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private const int WINDOW_MINUTES = 15;
    private const int MIN_SAMPLE_COUNT = 5;
    private const int RELOAD_INTERVAL_MINUTES = 5;

    public DemandCalculatorService(
        ILogger<DemandCalculatorService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("需量計算服務啟動");

        await WaitForDatabaseAsync(stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        await LoadDemandSidsAsync();

        if (_demandSids.Count == 0)
            _logger.LogInformation("目前無啟用的 DemandSID，等待 Reload 信號後方開始計算");

        // 對齊到下一個整分鐘再開始
        var dtNow = DateTime.Now;
        var dtNextMinute = new DateTime(dtNow.Year, dtNow.Month, dtNow.Day, dtNow.Hour, dtNow.Minute, 0).AddMinutes(1);
        var nDelayMs = (int)(dtNextMinute - dtNow).TotalMilliseconds;
        _logger.LogInformation("需量計算服務對齊整分鐘，等待 {DelayMs} ms", nDelayMs);

        try { await Task.Delay(nDelayMs, stoppingToken); }
        catch (OperationCanceledException) { return; }

        int nTickCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // 每 RELOAD_INTERVAL_MINUTES 分鐘重新讀取一次 DemandSID 清單（抓動態新增）
            if (nTickCount % RELOAD_INTERVAL_MINUTES == 0)
                await LoadDemandSidsAsync();
            nTickCount++;

            var dtTick = TruncateToMinute(DateTime.Now);
            try
            {
                await CalculateAllAsync(dtTick, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "需量計算主迴圈發生錯誤");
            }

            // 等到下一個整分鐘
            var dtNext = TruncateToMinute(DateTime.Now).AddMinutes(1);
            var nWaitMs = Math.Max(0, (int)(dtNext - DateTime.Now).TotalMilliseconds);
            try { await Task.Delay(nWaitMs, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("需量計算服務已停止");
    }

    /// <summary>重新載入 DemandSID 清單（供外部 Reload Subscriber 呼叫）</summary>
    public async Task ReloadDemandSidsAsync()
    {
        await LoadDemandSidsAsync();
        _logger.LogInformation("DemandSID 清單已重新載入，共 {Count} 個", _demandSids.Count);
    }

    private async Task LoadDemandSidsAsync()
    {
        await _reloadLock.WaitAsync();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
            var sids = await repo.GetDemandSidsAsync();
            _demandSids = sids.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入 DemandSID 清單失敗");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task CalculateAllAsync(DateTime dtTick, CancellationToken ct)
    {
        List<string> sids;
        await _reloadLock.WaitAsync(ct);
        try { sids = _demandSids.ToList(); }
        finally { _reloadLock.Release(); }

        if (sids.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
        var dtWindowStart = dtTick.AddMinutes(-WINDOW_MINUTES);

        foreach (var szSid in sids)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var samples = (await repo.GetHistoryTableDataAsync(szSid, dtWindowStart, dtTick))
                    .Where(p => p.nQuality == 1)
                    .OrderBy(p => p.dtTimestamp)
                    .ToList();

                DemandDataModel result;
                if (samples.Count < MIN_SAMPLE_COUNT)
                {
                    result = new DemandDataModel
                    {
                        szSID         = szSid,
                        dtTimestamp   = dtTick,
                        dDemandKW     = 0,
                        dtWindowStart = dtWindowStart,
                        nSampleCount  = samples.Count,
                        nQuality      = 0
                    };
                }
                else
                {
                    result = new DemandDataModel
                    {
                        szSID         = szSid,
                        dtTimestamp   = dtTick,
                        dDemandKW     = CalcTwa(samples, dtTick),
                        dtWindowStart = dtWindowStart,
                        nSampleCount  = samples.Count,
                        nQuality      = 1
                    };
                }

                await repo.UpsertDemandDataAsync(result);
                _logger.LogDebug("需量計算完成: SID={SID} DemandKW={KW:F2} Samples={Count} Quality={Q}",
                    szSid, result.dDemandKW, result.nSampleCount, result.nQuality);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "需量計算失敗: SID={SID}", szSid);
            }
        }
    }

    /// <summary>
    /// step-hold 時間加權平均（TWA）。
    /// 各樣本值保持到下一個樣本的時刻；最後一個樣本延伸到 windowEnd。
    /// </summary>
    private static double CalcTwa(List<HistoryDataModel> points, DateTime windowEnd)
    {
        if (points.Count == 1)
            return points[0].fValue;

        double dTotalWeighted = 0;
        double dTotalSeconds = 0;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var dDt = (points[i + 1].dtTimestamp - points[i].dtTimestamp).TotalSeconds;
            dTotalWeighted += points[i].fValue * dDt;
            dTotalSeconds  += dDt;
        }

        // 最後一段延伸到 windowEnd
        var dLastDt = (windowEnd - points[^1].dtTimestamp).TotalSeconds;
        if (dLastDt > 0)
        {
            dTotalWeighted += points[^1].fValue * dLastDt;
            dTotalSeconds  += dLastDt;
        }

        return dTotalSeconds > 0 ? dTotalWeighted / dTotalSeconds : 0;
    }

    private static DateTime TruncateToMinute(DateTime dt)
        => new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);

    private async Task WaitForDatabaseAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
                if (await repo.TestConnectionAsync())
                {
                    _logger.LogInformation("需量計算服務：DB 連線就緒");
                    return;
                }
            }
            catch { }

            _logger.LogWarning("需量計算服務：等待 DB 連線...");
            try { await Task.Delay(5000, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
