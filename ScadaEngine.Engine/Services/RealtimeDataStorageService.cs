using System.Collections.Concurrent;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 即時資料儲存服務，負責收集即時資料並每五秒儲存至 LatestData 資料表
/// </summary>
public class RealtimeDataStorageService : IDisposable
{
    private readonly ILogger<RealtimeDataStorageService> _logger;
    private readonly IDataRepository _dataRepository;
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<string, LatestDataModel> _latestDataCache;
    private readonly object _lockObject = new();
    private bool _isDisposed = false;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="dataRepository">資料存取介面</param>
    public RealtimeDataStorageService(ILogger<RealtimeDataStorageService> logger, IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
        _latestDataCache = new ConcurrentDictionary<string, LatestDataModel>();

        // 設定每五秒觸發一次的定時器
        _timer = new Timer(SaveLatestDataCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        _logger.LogInformation("即時資料儲存服務已啟動，每五秒儲存一次最新資料");
    }

    /// <summary>
    /// 新增即時資料至暫存區
    /// </summary>
    /// <param name="realtimeData">即時資料</param>
    public void AddRealtimeData(RealtimeDataModel realtimeData)
    {
        if (realtimeData == null || string.IsNullOrEmpty(realtimeData.szSID))
        {
            return;
        }

        try
        {
            // 將即時資料轉換為最新資料格式，時間設定為秒級別
            var now = DateTime.Now;
            var latestData = new LatestDataModel(realtimeData)
            {
                // 設定為當前時間（秒級別）
                dtTimestamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second)
            };
            
            // 使用 SID 作為鍵，每個點位只保留最新的數值
            _latestDataCache.AddOrUpdate(realtimeData.szSID, latestData, (szKey, szOldValue) => latestData);
            
            _logger.LogTrace("已添加即時資料到最新資料暫存: SID={SID}, Value={Value}", 
                realtimeData.szSID, realtimeData.fValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加即時資料到最新資料暫存時發生錯誤: SID={SID}", realtimeData.szSID);
        }
    }

    /// <summary>
    /// 批量新增即時資料至暫存區
    /// </summary>
    /// <param name="realtimeDataList">即時資料清單</param>
    public void AddRealtimeDataBatch(IEnumerable<RealtimeDataModel> realtimeDataList)
    {
        if (realtimeDataList == null)
        {
            return;
        }

        foreach (var data in realtimeDataList)
        {
            AddRealtimeData(data);
        }
    }

    /// <summary>
    /// 定時器回調函式，執行最新資料儲存
    /// </summary>
    /// <param name="state">狀態物件</param>
    private async void SaveLatestDataCallback(object? state)
    {
        try
        {
            await SaveLatestDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定時儲存最新資料時發生錯誤");
        }
    }

    /// <summary>
    /// 儲存最新資料至 LatestData 資料表
    /// </summary>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> SaveLatestDataAsync()
    {
        if (_latestDataCache.IsEmpty)
        {
            _logger.LogTrace("最新資料暫存區無資料，跳過此次儲存");
            return 0;
        }

        List<LatestDataModel> dataToSave;
        
        // 鎖定暫存區，避免資料競爭
        lock (_lockObject)
        {
            dataToSave = _latestDataCache.Values.ToList();
            _latestDataCache.Clear();
        }

        _logger.LogDebug("開始儲存最新資料: 共 {Count} 筆資料", dataToSave.Count);

        try
        {
            // 呼叫資料存取層儲存最新資料 (UPSERT 模式)
            var nSuccessCount = await _dataRepository.SaveLatestDataAsync(dataToSave);
            
            if (nSuccessCount > 0)
            {
                _logger.LogDebug("最新資料儲存完成: 成功 {SuccessCount}/{TotalCount} 筆", 
                    nSuccessCount, dataToSave.Count);
            }
            else
            {
                _logger.LogWarning("最新資料儲存失敗: 0/{TotalCount} 筆", dataToSave.Count);
            }

            return nSuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存最新資料時發生錯誤");
            return 0;
        }
    }

    /// <summary>
    /// 手動觸發最新資料儲存 (測試用途)
    /// </summary>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> ManualSaveAsync()
    {
        _logger.LogInformation("手動觸發最新資料儲存");
        return await SaveLatestDataAsync();
    }

    /// <summary>
    /// 取得目前暫存區的資料筆數
    /// </summary>
    /// <returns>暫存區資料筆數</returns>
    public int GetCachedDataCount()
    {
        return _latestDataCache.Count;
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            // 停止定時器
            _timer?.Dispose();

            // 最後一次儲存剩餘資料
            if (!_latestDataCache.IsEmpty)
            {
                var saveTask = SaveLatestDataAsync();
                saveTask.Wait(TimeSpan.FromSeconds(10)); // 最多等待10秒
            }

            _logger.LogInformation("即時資料儲存服務已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "釋放即時資料儲存服務資源時發生錯誤");
        }
        finally
        {
            _isDisposed = true;
        }
    }
}