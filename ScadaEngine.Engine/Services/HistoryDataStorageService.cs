using System.Collections.Concurrent;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 歷史資料儲存服務，負責收集即時資料並定時儲存至資料庫
/// </summary>
public class HistoryDataStorageService : IDisposable
{
    private readonly ILogger<HistoryDataStorageService> _logger;
    private readonly IDataRepository _dataRepository;
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<string, HistoryDataModel> _dataCache;
    private readonly object _lockObject = new();
    private bool _isDisposed = false;

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="dataRepository">資料存取介面</param>
    public HistoryDataStorageService(ILogger<HistoryDataStorageService> logger, IDataRepository dataRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
        _dataCache = new ConcurrentDictionary<string, HistoryDataModel>();

        // 設定每分鐘觸發一次的定時器
        _timer = new Timer(SaveHistoryDataCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        _logger.LogInformation("歷史資料儲存服務已啟動，每分鐘儲存一次資料");
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
            // 將即時資料轉換為歷史資料格式，並將時間戳設為當前分鐘（秒數為0）
            var dtMinuteTimestamp = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 
                                               DateTime.Now.Hour, DateTime.Now.Minute, 0);
            
            var historyData = new HistoryDataModel(realtimeData.szSID, realtimeData.fValue, realtimeData.szQuality)
            {
                dtTimestamp = dtMinuteTimestamp
            };
            
            // 使用 SID + 分鐘時間戳作為鍵，確保每分鐘每個點位只有一筆記錄
            var szCacheKey = $"{realtimeData.szSID}_{dtMinuteTimestamp:yyyyMMddHHmm}";
            _dataCache.AddOrUpdate(szCacheKey, historyData, (szKey, szOldValue) => historyData);
            
            //_logger.LogDebug("已添加即時資料到歷史暫存: SID={SID}, Value={Value}, Minute={Minute}", 
             //   realtimeData.szSID, realtimeData.fValue, dtMinuteTimestamp.ToString("yyyy-MM-dd HH:mm"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加即時資料到歷史暫存時發生錯誤: SID={SID}", realtimeData.szSID);
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
    /// 定時器回調函式，執行歷史資料儲存
    /// </summary>
    /// <param name="state">狀態物件</param>
    private async void SaveHistoryDataCallback(object? state)
    {
        try
        {
            await SaveHistoryDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定時儲存歷史資料時發生錯誤");
        }
    }

    /// <summary>
    /// 儲存歷史資料至資料庫
    /// </summary>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> SaveHistoryDataAsync()
    {
        if (_dataCache.IsEmpty)
        {
            _logger.LogDebug("暫存區無資料，跳過此次歷史資料儲存");
            return 0;
        }

        List<HistoryDataModel> dataToSave;
        
        // 鎖定暫存區，避免資料競爭
        lock (_lockObject)
        {
            dataToSave = _dataCache.Values.ToList();
            _dataCache.Clear();
        }

        _logger.LogInformation("開始儲存歷史資料: 共 {Count} 筆資料", dataToSave.Count);

        try
        {
            // 呼叫資料存取層儲存歷史資料
            var nSuccessCount = await _dataRepository.SaveHistoryDataAsync(dataToSave);
            
            if (nSuccessCount > 0)
            {
                _logger.LogInformation("歷史資料儲存完成: 成功 {SuccessCount}/{TotalCount} 筆", 
                    nSuccessCount, dataToSave.Count);
            }
            else
            {
                _logger.LogWarning("歷史資料儲存失敗: 0/{TotalCount} 筆", dataToSave.Count);
            }

            return nSuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存歷史資料時發生錯誤");
            return 0;
        }
    }

    /// <summary>
    /// 手動觸發歷史資料儲存 (測試用途)
    /// </summary>
    /// <returns>成功儲存的資料筆數</returns>
    public async Task<int> ManualSaveAsync()
    {
        _logger.LogInformation("手動觸發歷史資料儲存");
        return await SaveHistoryDataAsync();
    }

    /// <summary>
    /// 取得目前暫存區的資料筆數
    /// </summary>
    /// <returns>暫存區資料筆數</returns>
    public int GetCachedDataCount()
    {
        return _dataCache.Count;
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
            if (!_dataCache.IsEmpty)
            {
                var saveTask = SaveHistoryDataAsync();
                saveTask.Wait(TimeSpan.FromSeconds(30)); // 最多等待30秒
            }

            _logger.LogInformation("歷史資料儲存服務已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "釋放歷史資料儲存服務資源時發生錯誤");
        }
        finally
        {
            _isDisposed = true;
        }
    }
}