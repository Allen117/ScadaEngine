using Microsoft.Extensions.DependencyInjection;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 需量計算背景服務。
/// 每分鐘對齊整分鐘計算各電表迴路 15min ΔkWh×4 需量（kW），
/// 結果 UPSERT 至 DemandData 表供監控預警使用，
/// 並以虛擬點位 SID（DMD-{kWhSID}）比照計算點三路發布：
/// RealtimeDataStorageService（記憶體＋LatestData）→ 條件控制/LogicFlow、
/// HistoryDataStorageService（HistoryData）→ 歷史查詢、
/// MqttPublishService → 警報引擎/Web 即時。
///
/// 設計重點：
/// - 算法：取 15min 窗口內最早一筆與最晚一筆 kWh 樣本，差值 × 4
/// - Δ &lt; 0（電表重置）→ DemandKW=0, Quality=0
/// - Quality=1 的樣本數 &lt; 2 → DemandKW=0, Quality=0（資料不足）
/// - Quality 忠實透傳：Bad 期間發布 值=0/Quality=Bad，不美化為 Good（避免誤觸發下限/卸載邏輯）
/// - 公開 ReloadDemandSidsAsync() 供外部 MQTT Reload Subscriber 呼叫
/// </summary>
public class DemandCalculatorService : BackgroundService
{
    /// <summary>需量虛擬點位 SID 前綴（DMD-{kWhSID}，與 DemandData.SID 一對一可逆推）</summary>
    public const string DEMAND_SID_PREFIX = "DMD-";

    private readonly ILogger<DemandCalculatorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RealtimeDataStorageService _realtimeDataStorageService;
    private readonly HistoryDataStorageService _historyDataStorageService;
    private readonly MqttPublishService _mqttPublishService;
    private List<string> _demandSids = new();
    private Dictionary<string, string> _demandNames = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private const int WINDOW_MINUTES = 15;
    private const int MIN_SAMPLE_COUNT = 2;
    private const int RELOAD_INTERVAL_MINUTES = 5;

    public DemandCalculatorService(
        ILogger<DemandCalculatorService> logger,
        IServiceProvider serviceProvider,
        RealtimeDataStorageService realtimeDataStorageService,
        HistoryDataStorageService historyDataStorageService,
        MqttPublishService mqttPublishService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _realtimeDataStorageService = realtimeDataStorageService;
        _historyDataStorageService = historyDataStorageService;
        _mqttPublishService = mqttPublishService;
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
        var nDelayMs = (int)Math.Ceiling((dtNextMinute - dtNow).TotalMilliseconds);
        _logger.LogInformation("需量計算服務對齊整分鐘，等待 {DelayMs} ms", nDelayMs);

        try { await Task.Delay(nDelayMs, stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Task.Delay 可能提早數 ms 觸發，spin 到確實跨過整分鐘
        while (DateTime.Now < dtNextMinute && !stoppingToken.IsCancellationRequested)
            await Task.Delay(1, stoppingToken);

        // dtTick 記錄「預定 tick 時間」，每次固定 +1min，不從 DateTime.Now 重新截斷
        // 這樣即使某次計算花超過 1 分鐘，下一個 tick 仍是 M+1 而非 M+2（不跳分鐘）
        var dtTick = TruncateToMinute(DateTime.Now);

        int nTickCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // 每 RELOAD_INTERVAL_MINUTES 分鐘重新讀取一次 DemandSID 清單（抓動態新增）
            if (nTickCount % RELOAD_INTERVAL_MINUTES == 0)
                await LoadDemandSidsAsync();
            nTickCount++;

            try
            {
                await CalculateAllAsync(dtTick, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "需量計算主迴圈發生錯誤");
            }

            // 固定往前推一分鐘（不依賴當下 DateTime.Now 截斷，避免計算慢時跳過分鐘）
            dtTick = dtTick.AddMinutes(1);
            var nWaitMs = (int)Math.Max(0, Math.Ceiling((dtTick - DateTime.Now).TotalMilliseconds));
            try { await Task.Delay(nWaitMs, stoppingToken); }
            catch (OperationCanceledException) { break; }

            // 防止 Task.Delay 提早數 ms 觸發，確保已真正到達 dtTick
            while (DateTime.Now < dtTick && !stoppingToken.IsCancellationRequested)
                await Task.Delay(1, stoppingToken);
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
            // SID 集合與 GetDemandSidsAsync 一致，另帶電表名稱供虛擬點位組名
            var infos = (await repo.GetDemandPointInfosAsync()).ToList();
            _demandSids = infos.Select(i => i.szSID).ToList();
            _demandNames = infos.ToDictionary(i => i.szSID, i => i.szName);
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
        Dictionary<string, string> names;
        await _reloadLock.WaitAsync(ct);
        try
        {
            sids = _demandSids.ToList();
            names = new Dictionary<string, string>(_demandNames);
        }
        finally { _reloadLock.Release(); }

        if (sids.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
        var dtWindowStart = dtTick.AddMinutes(-WINDOW_MINUTES);
        var publishList = new List<RealtimeDataModel>();

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
                    var dDeltaKwh = samples[^1].fValue - samples[0].fValue;
                    var isReset = dDeltaKwh < 0;
                    result = new DemandDataModel
                    {
                        szSID         = szSid,
                        dtTimestamp   = dtTick,
                        dDemandKW     = isReset ? 0 : dDeltaKwh * 4.0,
                        dtWindowStart = dtWindowStart,
                        nSampleCount  = samples.Count,
                        nQuality      = isReset ? (byte)0 : (byte)1
                    };
                }

                await repo.UpsertDemandDataAsync(result);

                // 需量虛擬點位（DMD-{kWhSID}）：比照計算點三路發布，Quality 忠實透傳
                names.TryGetValue(szSid, out var szMeterName);
                publishList.Add(ToRealtimePoint(result, szMeterName));

                _logger.LogDebug("需量計算完成: SID={SID} DemandKW={KW:F2} Samples={Count} Quality={Q}",
                    szSid, result.dDemandKW, result.nSampleCount, result.nQuality);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "需量計算失敗: SID={SID}", szSid);
            }
        }

        if (publishList.Count == 0) return;

        // 送入儲存服務（歷史 + 最新值）＋ MQTT 發布（照抄 CalculatedPointService.CalculateAndPublish 模式）
        _historyDataStorageService.AddRealtimeDataBatch(publishList);
        _realtimeDataStorageService.AddRealtimeDataBatch(publishList);

        _ = Task.Run(async () =>
        {
            foreach (var data in publishList)
            {
                try
                {
                    await _mqttPublishService.PublishRealtimeDataAsync(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "需量虛擬點位 {SID} MQTT 發布失敗", data.szSID);
                }
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// 需量計算結果 → 虛擬點位 RealtimeDataModel 映射。
    /// SID = DMD-{kWhSID}、名稱 = {電表名} 需量（無名稱時退 kWh SID）、單位 kW、
    /// Quality 1→Good / 0→Bad（Bad 時值為 0，不美化）。
    /// </summary>
    public static RealtimeDataModel ToRealtimePoint(DemandDataModel result, string? szMeterName)
    {
        var szBaseName = string.IsNullOrWhiteSpace(szMeterName) ? result.szSID : szMeterName;
        return new RealtimeDataModel
        {
            dtTimestamp       = result.dtTimestamp,
            szSID             = DEMAND_SID_PREFIX + result.szSID,
            szTagName         = $"{szBaseName} 需量",
            fValue            = (float)result.dDemandKW,
            szUnit            = "kW",
            szQuality         = result.nQuality == 1 ? "Good" : "Bad",
            szDeviceIP        = "Demand",
            nAddress          = 0,
            szCoordinatorName = "需量",
            IsReadSuccess     = true
        };
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
