using FluentModbus;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Communication.Mqtt;
using System.Collections.Concurrent;
using System.Net;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Services;

/// <summary>
/// Modbus 批量讀取分組
/// </summary>
public class ModbusBatchGroup
{
    /// <summary>
    /// Modbus 功能碼
    /// </summary>
    public byte nFunctionCode { get; set; }

    /// <summary>
    /// 批次起始地址
    /// </summary>
    public ushort nStartAddress { get; set; }

    /// <summary>
    /// 批次暫存器數量
    /// </summary>
    public ushort nRegisterCount { get; set; }

    /// <summary>
    /// 此批次包含的點位清單
    /// </summary>
    public List<ModbusTagModel> tagList { get; set; } = new();
}

/// <summary>
/// Modbus 通訊服務，負責與單個 Modbus 設備的連線與資料讀取
/// </summary>
public class ModbusCommunicationService : IDisposable
{
    private readonly ModbusDeviceConfigModel _deviceConfig;
    private readonly ILogger<ModbusCommunicationService> _logger;
    private readonly MqttPublishService? _mqttPublishService;
    private ModbusTcpClient? _modbusClient;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private bool _isDisposed = false;
    private DateTime _dtLastSuccessfulRead = DateTime.MinValue;
    private DateTime _dtFirstConnectionFailure = DateTime.MinValue;
    private DateTime _dtLastReconnectAttempt = DateTime.MinValue;
    /// <summary>
    /// 取得重連延遲時間（connectTimeout 的 20 倍）
    /// </summary>
    private TimeSpan ReconnectDelay 
    { 
        get 
        {
            var delayMs = _deviceConfig.nConnectTimeout * 20;
            return TimeSpan.FromMilliseconds(delayMs);
        }
    }
    
    /// <summary>
    /// 上一次發布的數值快取，用於偵測值變化 (SID -> Value)
    /// </summary>
    private readonly ConcurrentDictionary<string, float> _lastPublishedValues = new();

    /// <summary>
    /// 上一次發布的品質狀態快取，用於偵測品質變化 (SID -> Quality)
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _lastPublishedQuality = new();

    /// <summary>
    /// 設備連線狀態
    /// </summary>
    public bool IsConnected { get; private set; } = false;

    /// <summary>
    /// 設備 IP 地址
    /// </summary>
    public string DeviceIP => _deviceConfig.szIP;

    /// <summary>
    /// 設備埠號
    /// </summary>
    public int DevicePort => _deviceConfig.nPort;

    /// <summary>
    /// 點位數量
    /// </summary>
    public int TagCount => _deviceConfig.tagList.Count;

    public ModbusCommunicationService(ModbusDeviceConfigModel deviceConfig, ILogger<ModbusCommunicationService> logger, MqttPublishService? mqttPublishService = null)
    {
        _deviceConfig = deviceConfig ?? throw new ArgumentNullException(nameof(deviceConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttPublishService = mqttPublishService;
    }

    /// <summary>
    /// 建立 Modbus 連線
    /// </summary>
    /// <returns>連線成功回傳 true</returns>
    /// <summary>
/// 建立 Modbus 連線
/// 🔥 修正：記錄重連嘗試時間
/// </summary>
public async Task<bool> ConnectAsync()
{
    await _connectionSemaphore.WaitAsync();
    try
    {
        if (_isDisposed)
            return false;

        // 🔥 修正：記錄重連嘗試時間
        _dtLastReconnectAttempt = DateTime.Now;
        _logger.LogInformation("嘗試連線 Modbus: {IP}:{Port}, 嘗試時間={AttemptTime}", 
                             _deviceConfig.szIP, _deviceConfig.nPort, _dtLastReconnectAttempt.ToString("HH:mm:ss.fff"));

        // 如果已連線，先斷開
        if (_modbusClient != null)
        {
            DisconnectInternal();
        }

        _modbusClient = new ModbusTcpClient();
        
        try
        {
            // 解析 IP 地址
            if (!IPAddress.TryParse(_deviceConfig.szIP, out var ipAddress))
            {
                _logger.LogError("無效的 IP 地址: {IP}", _deviceConfig.szIP);
                return false;
            }

            // 設定連線參數
            var endPoint = new IPEndPoint(ipAddress, _deviceConfig.nPort);
            _modbusClient.ConnectTimeout = _deviceConfig.nConnectTimeout;
            _modbusClient.ReadTimeout = _deviceConfig.nConnectTimeout;
            _modbusClient.WriteTimeout = _deviceConfig.nConnectTimeout;

            // 建立連線
            _modbusClient.Connect(endPoint);
            
            IsConnected = true;
            _dtLastSuccessfulRead = DateTime.Now;
            
            // 🔥 修正：連線成功時重置失敗追蹤
            _dtFirstConnectionFailure = DateTime.MinValue;

            _logger.LogInformation("Modbus 連線建立成功: {IP}:{Port}, 成功時間={SuccessTime}", 
                                 _deviceConfig.szIP, _deviceConfig.nPort, _dtLastSuccessfulRead.ToString("HH:mm:ss.fff"));
            _logger.LogInformation("連線恢復，重置連線失敗狀態: {IP}:{Port}", _deviceConfig.szIP, _deviceConfig.nPort);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modbus 連線建立失敗: {IP}:{Port}, 嘗試時間={AttemptTime}", 
                           _deviceConfig.szIP, _deviceConfig.nPort, _dtLastReconnectAttempt.ToString("HH:mm:ss.fff"));
            
            DisconnectInternal();
            
            // 🔥 修正：連線失敗時記錄失敗時間
            RecordConnectionFailure();
            
            return false;
        }
    }
    finally
    {
        _connectionSemaphore.Release();
    }
}

    /// <summary>
    /// 斷開 Modbus 連線
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _connectionSemaphore.WaitAsync();
        try
        {
            DisconnectInternal();
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// 內部斷線實作 (不加鎖)
    /// </summary>
    private void DisconnectInternal()
    {
        if (_modbusClient != null)
        {
            try
            {
                _modbusClient.Disconnect();
                _modbusClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "斷開 Modbus 連線時發生錯誤");
            }
            finally
            {
                _modbusClient = null;
                IsConnected = false;
            }
        }
    }

    /// <summary>
    /// 讀取所有點位資料並發布至 MQTT - 並行處理多個 ModbusID
    /// </summary>
    /// <returns>即時資料清單</returns>
    public async Task<List<RealtimeDataModel>> ReadAllTagsAsync()
    {
        var resultList = new List<RealtimeDataModel>();

        _logger.LogInformation("[DEBUG] ReadAllTagsAsync 開始: IsConnected={IsConnected}, Client={Client}", 
                              IsConnected, _modbusClient != null ? "存在" : "null");

        // 即使未連線也要嘗試讀取，以產生 Bad Quality 資料
        // if (!IsConnected || _modbusClient == null)
        // {
        //     _logger.LogWarning("Modbus 未連線，無法讀取資料: {IP}", _deviceConfig.szIP);
        //     return resultList;
        // }

        try
        {
            var modbusIds = _deviceConfig.GetModbusIdArray();
            _logger.LogInformation("開始並行處理 {Count} 個 ModbusId: {ModbusIds}", modbusIds.Length, string.Join(",", modbusIds));

            // 建立並行任務清單，為每個 ModbusId 建立獨立的執行緒
            var parallelTasks = modbusIds.Select(async (nModbusId, index) =>
            {
                try
                {
                    _logger.LogInformation("啟動執行緒輪詢 ModbusId: {ModbusId} (執行緒 {ThreadId}/{Total})", 
                        nModbusId, index + 1, modbusIds.Length);

                    // 在背景執行緒中讀取當前 ModbusId 的資料
                    var tagDataList = await Task.Run(() => ReadTagsForModbusId(nModbusId));

                    if (tagDataList?.Count > 0)
                    {
                        _logger.LogDebug("執行緒 ModbusId {ModbusId} 讀取完成: {TagCount} 個點位", nModbusId, tagDataList.Count);

                        // 偵測值變化並立即發布該 ModbusId 的變化資料到 MQTT Broker
                        if (_mqttPublishService != null && _mqttPublishService.IsConnected)
                        {
                            var changedDataList = DetectValueChanges(tagDataList);
                            
                            if (changedDataList.Count > 0)
                            {
                                var nPublishedCount = await _mqttPublishService.PublishBatchRealtimeDataAsync(changedDataList);
                                _logger.LogDebug("執行緒 ModbusId {ModbusId} 已發布 {PublishedCount}/{ChangeCount} 筆變化資料至 MQTT (原始總數: {TotalCount})", 
                                               nModbusId, nPublishedCount, changedDataList.Count, tagDataList.Count);
                            }
                            else
                            {
                                _logger.LogTrace("執行緒 ModbusId {ModbusId}: 沒有值變化，跳過 MQTT 發布", nModbusId);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("執行緒 ModbusId {ModbusId}: MQTT 服務未啟用或未連線，跳過發布", nModbusId);
                        }

                        return tagDataList;
                    }
                    else
                    {
                        _logger.LogWarning("執行緒 ModbusId {ModbusId} 未讀取到任何資料", nModbusId);
                        return new List<RealtimeDataModel>();
                    }
                }
                catch (Exception ex)
                {
                    // 檢查是否為連線相關錯誤
                    if (IsConnectionRelatedError(ex))
                    {
                        _logger.LogError(ex, "執行緒 ModbusId {ModbusId} 連線失敗，標記為斷線狀態", nModbusId);
                        IsConnected = false; // 標記連線狀態為斷線
                        RecordConnectionFailure(); // 記錄連線失敗時間（僅第一次）
                    }
                    else
                    {
                        _logger.LogError(ex, "執行緒輪詢 ModbusId {ModbusId} 時發生錯誤", nModbusId);
                    }
                    return new List<RealtimeDataModel>();
                }
            }).ToArray();

            // 等待所有並行任務完成
            var allResults = await Task.WhenAll(parallelTasks);

            // 合併所有執行緒的結果
            foreach (var taskResults in allResults)
            {
                if (taskResults?.Count > 0)
                {
                    resultList.AddRange(taskResults);
                }
            }

            // 在 ReadAllTagsAsync 方法的最後：
            // 原代碼：
            // if (resultList.Count > 0)
            // {
            //     _dtLastSuccessfulRead = DateTime.Now;
            // }

            // 🔥 修正為：只統計真正成功讀取的資料（排除 Bad Quality）
            var nSuccessCount = resultList.Count(r => r.IsReadSuccess);
            if (nSuccessCount > 0)
            {
                _dtLastSuccessfulRead = DateTime.Now;
                _dtFirstConnectionFailure = DateTime.MinValue; // 🔥 重置失敗追蹤
                _logger.LogDebug("成功讀取 {SuccessCount} 個點位，更新最後成功時間: {Time}", 
                            nSuccessCount, _dtLastSuccessfulRead.ToString("HH:mm:ss.fff"));
            }
            else if (resultList.Count > 0)
            {
                // 有資料但都是 Bad Quality
                _logger.LogWarning("讀取到 {Count} 個點位但全部為 Bad Quality，不更新成功時間", resultList.Count);
            }

            _logger.LogTrace("完成並行輪詢所有 ModbusId，總計讀取 {Count} 個點位資料", resultList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "讀取 Modbus 資料時發生錯誤: {IP}", _deviceConfig.szIP);
            
            // 連線可能已斷開，標記為未連線
            IsConnected = false;
        }

        return resultList;
    }

    /// <summary>
    /// 讀取指定 ModbusId 的所有點位資料 - 使用智慧批量讀取
    /// </summary>
    /// <param name="nModbusId">Modbus 站號</param>
    /// <returns>即時資料清單</returns>
    private List<RealtimeDataModel> ReadTagsForModbusId(byte nModbusId)
    {
        var resultList = new List<RealtimeDataModel>();
        _logger.LogInformation("開始讀取 ModbusId {ModbusId} 的資料，總計 {TagCount} 個點位", nModbusId, _deviceConfig.tagList.Count);

        try
        {
            // 首先驗證所有點位是否正確解析
            var validTags = new List<ModbusTagModel>();
            foreach (var tag in _deviceConfig.tagList)
            {
                if (tag.ParseAddress() && tag.ParseRatioAndRegisterCount())
                {
                    validTags.Add(tag);
                    _logger.LogTrace("點位 {TagName} 驗證通過: 功能碼={FC}, 地址={Addr}, 暫存器數={RegCount}",
                                    tag.szName, tag.nFunctionCode, tag.nParsedAddress, tag.nRegisterCount);
                }
                else
                {
                    _logger.LogWarning("點位 {TagName} 驗證失敗: 地址={Address}", tag.szName, tag.szAddress);
                }
            }

            if (validTags.Count == 0)
            {
                _logger.LogWarning("ModbusId {ModbusId} 沒有有效的點位配置", nModbusId);
                return resultList;
            }

            _logger.LogDebug("ModbusId {ModbusId} 有效點位數: {ValidCount}/{TotalCount}", nModbusId, validTags.Count, _deviceConfig.tagList.Count);

            // 使用智慧批量讀取分組
            var batchGroups = OptimizeBatchReads(validTags, nModbusId);
            _logger.LogDebug("ModbusId {ModbusId} 分割為 {BatchCount} 個批次讀取", nModbusId, batchGroups.Count);

            // 嘗試批量讀取
            bool hasConnectionError = false;
            foreach (var batchGroup in batchGroups)
            {
                try
                {
                    var batchResults = ReadBatchGroup(batchGroup, nModbusId);
                    resultList.AddRange(batchResults);
                    
                    // 檢查是否包含 Bad quality 資料（可能是連線失敗）
                    var badQualityCount = batchResults.Count(r => r.szQuality == "Bad");
                    if (badQualityCount > 0)
                    {
                        hasConnectionError = true;
                        _logger.LogWarning("批次 {BatchIndex} 包含 {BadCount} 個 Bad 品質資料點", 
                                         batchGroups.IndexOf(batchGroup) + 1, badQualityCount);
                    }
                }
                catch (Exception ex)
                {
                    // 如果是連線錯誤，已在 ReadBatchGroup 中處理並返回 Bad quality 資料
                    if (IsConnectionRelatedError(ex))
                    {
                        hasConnectionError = true;
                        _logger.LogError(ex, "ModbusId {ModbusId} 批次讀取發生連線錯誤", nModbusId);
                        // 不重新拋出，繼續處理其他批次
                    }
                    else
                    {
                        _logger.LogWarning(ex, "ModbusId {ModbusId} 批次讀取失敗，繼續下一個批次", nModbusId);
                    }
                }
            }

            // 如果批量讀取沒有結果且非連線錯誤，回退到單點位讀取
            if (resultList.Count == 0 && validTags.Count > 0 && !hasConnectionError)
            {
                _logger.LogWarning("ModbusId {ModbusId} 批量讀取失敗，回退到單點位讀取模式", nModbusId);
                
                for (int i = 0; i < validTags.Count; i++)
                {
                    var tag = validTags[i];
                    var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
                    if (nGlobalTagIndex == -1) nGlobalTagIndex = i;
                    
                    var singleResult = ReadSingleTag(tag, nModbusId, nGlobalTagIndex);
                    if (singleResult != null)
                    {
                        resultList.Add(singleResult);
                        _logger.LogTrace("單點位讀取成功: {TagName} = {Value}", tag.szName, singleResult.fValue);
                    }
                    else
                    {
                        _logger.LogWarning("單點位讀取失敗: {TagName}", tag.szName);
                    }
                }
            }

            // 統計實際的成功和失敗數量
            var nSuccessCount = resultList.Count(r => r.IsReadSuccess);
            var nBadQualityCount = resultList.Count(r => !r.IsReadSuccess);
            
            _logger.LogInformation("ModbusId {ModbusId} 讀取完成: 成功 {SuccessCount}/{TotalCount} 個點位 (Bad Quality: {BadCount})", 
                                 nModbusId, nSuccessCount, validTags.Count, nBadQualityCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "讀取 ModbusId {ModbusId} 時發生未預期錯誤", nModbusId);
        }

        return resultList;
    }

    /// <summary>
    /// 智慧批量讀取：將連續地址分組成最多100暫存器的批次
    /// </summary>
    /// <param name="tagList">點位清單</param>
    /// <param name="nModbusId">Modbus 站號</param>
    /// <returns>批量讀取分組</returns>
    private List<ModbusBatchGroup> OptimizeBatchReads(List<ModbusTagModel> tagList, byte nModbusId)
    {
        var batchGroups = new List<ModbusBatchGroup>();
        _logger.LogDebug("開始為 ModbusId {ModbusId} 優化批量讀取，總計 {TagCount} 個點位", nModbusId, tagList.Count);

        // 按功能碼分組
        var functionGroups = tagList.GroupBy(tag => tag.nFunctionCode);

        foreach (var funcGroup in functionGroups)
        {
            _logger.LogDebug("處理功能碼 {FunctionCode} 的 {TagCount} 個點位", funcGroup.Key, funcGroup.Count());
            
            // 按地址排序
            var sortedTags = funcGroup.OrderBy(tag => tag.nParsedAddress).ToList();

            // 分割成多個批次，每批次最多100個暫存器
            var nCurrentBatchStart = -1;
            var nCurrentBatchEnd = -1;
            var currentBatchTags = new List<ModbusTagModel>();

            for (int i = 0; i < sortedTags.Count; i++)
            {
                var tag = sortedTags[i];
                var nTagStartAddress = tag.nParsedAddress;
                var nTagEndAddress = tag.nParsedAddress + tag.nRegisterCount - 1;

                _logger.LogTrace("處理點位 {TagName}: 地址 {StartAddr}-{EndAddr}, 需要 {RegCount} 個暫存器",
                                tag.szName, nTagStartAddress, nTagEndAddress, tag.nRegisterCount);

                // 檢查是否可以加入當前批次
                if (nCurrentBatchStart == -1)
                {
                    // 第一個點位，開始新批次
                    nCurrentBatchStart = nTagStartAddress;
                    nCurrentBatchEnd = nTagEndAddress;
                    currentBatchTags.Add(tag);
                    _logger.LogTrace("開始新批次: {Start}-{End}", nCurrentBatchStart, nCurrentBatchEnd);
                }
                else
                {
                    // 計算如果加入這個點位後的批次總長度
                    var nNewBatchEnd = Math.Max(nCurrentBatchEnd, nTagEndAddress);
                    var nNewBatchLength = nNewBatchEnd - nCurrentBatchStart + 1;

                    // 檢查是否超過100暫存器限制
                    if (nNewBatchLength <= 100)
                    {
                        // 可以加入當前批次
                        nCurrentBatchEnd = nNewBatchEnd;
                        currentBatchTags.Add(tag);
                        _logger.LogTrace("加入當前批次: 新範圍 {Start}-{End}, 長度 {Length}",
                                        nCurrentBatchStart, nCurrentBatchEnd, nNewBatchLength);
                    }
                    else
                    {
                        // 超過限制，完成當前批次並開始新批次
                        var nCurrentBatchLength = nCurrentBatchEnd - nCurrentBatchStart + 1;
                        
                        _logger.LogDebug("批次已滿，創建批次: 功能碼={FC}, 地址={Start}, 長度={Length}, 包含={TagCount}個點位",
                                        funcGroup.Key, nCurrentBatchStart, nCurrentBatchLength, currentBatchTags.Count);

                        batchGroups.Add(new ModbusBatchGroup
                        {
                            nFunctionCode = funcGroup.Key,
                            nStartAddress = (ushort)nCurrentBatchStart,
                            nRegisterCount = (ushort)nCurrentBatchLength,
                            tagList = new List<ModbusTagModel>(currentBatchTags)
                        });

                        // 開始新批次
                        nCurrentBatchStart = nTagStartAddress;
                        nCurrentBatchEnd = nTagEndAddress;
                        currentBatchTags.Clear();
                        currentBatchTags.Add(tag);
                        _logger.LogTrace("開始新批次: {Start}-{End}", nCurrentBatchStart, nCurrentBatchEnd);
                    }
                }
            }

            // 處理最後一個批次 (真正的最後批次才加3個額外暫存器)
            if (currentBatchTags.Count > 0)
            {
                var nFinalBatchLength = nCurrentBatchEnd - nCurrentBatchStart + 1;
                
                // 只在處理最後一個功能碼組的最後批次時才加3個額外暫存器
                var isLastFunctionGroup = funcGroup.Key == functionGroups.Max(g => g.Key);
                if (isLastFunctionGroup)
                {
                    if (funcGroup.Key == 3 || funcGroup.Key == 4) // 只對保持寄存器和輸入寄存器加3
                    {
                        nFinalBatchLength += 3;
                        _logger.LogDebug("最終批次額外加3個暫存器確保Double資料完整");
                    }
                }
                
                _logger.LogDebug("創建最終批次: 功能碼={FC}, 地址={Start}, 長度={Length}, 包含={TagCount}個點位",
                                funcGroup.Key, nCurrentBatchStart, nFinalBatchLength, currentBatchTags.Count);

                batchGroups.Add(new ModbusBatchGroup
                {
                    nFunctionCode = funcGroup.Key,
                    nStartAddress = (ushort)nCurrentBatchStart,
                    nRegisterCount = (ushort)nFinalBatchLength,
                    tagList = new List<ModbusTagModel>(currentBatchTags)
                });
            }
        }

        _logger.LogInformation("ModbusId {ModbusId} 批量優化完成: {BatchCount} 個批次", nModbusId, batchGroups.Count);
        return batchGroups;
    }
/// <summary>
/// 讀取批次組資料並智慧分配給各個點位
/// 🔥 修正：解決一開始斷線和中途斷線的 Bad Quality 推播問題
/// </summary>
private List<RealtimeDataModel> ReadBatchGroup(ModbusBatchGroup batchGroup, byte nModbusId)
{
    _logger.LogInformation("[DEBUG] 開始 ReadBatchGroup: IP={IP}, FC={FC}, Addr={Addr}, Count={Count}", 
                          _deviceConfig.szIP, batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    var resultList = new List<RealtimeDataModel>();

    if (batchGroup.tagList.Count == 0)
    {
        _logger.LogWarning("批次組沒有點位，跳過讀取: TagCount={Count}", batchGroup.tagList.Count);
        return resultList;
    }

    // 🔥 修正點 1：在方法開頭統一檢查連線狀態
    // 解決兩個問題：
    // 1. 一開始就斷線（_modbusClient == null）
    // 2. 中途斷線後續 batch（IsConnected == false）
    if (!IsConnected || _modbusClient == null)
    {
        _logger.LogWarning("設備未連線或 ModbusClient 為 null，直接產生 Bad Quality 資料: IsConnected={IsConnected}, Client={Client}",
                         IsConnected, _modbusClient != null ? "存在" : "null");
        
        // 記錄連線失敗（如果是首次）
        RecordConnectionFailure();
        // 🔥 關鍵：檢查是否超過延遲時間才產生 Bad Quality
        bool isTimeout = IsConnectionFailureTimeout();
        _logger.LogInformation("[DEBUG] 檢查超時狀態: IsTimeout={IsTimeout}, FirstFailure={FirstFailure}, Now={Now}, Delay={Delay}ms", 
                             isTimeout, _dtFirstConnectionFailure, DateTime.Now, ReconnectDelay.TotalMilliseconds);
        
        if (isTimeout)
        {
            _logger.LogWarning("連線失敗超過延遲時間 {Delay}ms，產生 Bad Quality 資料", ReconnectDelay.TotalMilliseconds);
                
            // 立即為所有點位產生 Bad Quality 資料
            foreach (var tag in batchGroup.tagList)
            {
                var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
                if (nGlobalTagIndex == -1) nGlobalTagIndex = 0;

                var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
                var nN = nGlobalTagIndex + 1;
                var szDynamicSID = $"{nXXX}-S{nN}";

                resultList.Add(new RealtimeDataModel
                {
                    dtTimestamp = DateTime.Now,
                    szSID = szDynamicSID,
                    szCoordinatorName = _deviceConfig.szCoordinatorName,
                    szTagName = tag.szName,
                    fValue = 0.0f,
                    szUnit = tag.szUnit,
                    szQuality = "Bad",
                    szDeviceIP = _deviceConfig.szIP,
                    nAddress = int.Parse(tag.szAddress),
                    IsReadSuccess = false
                });

                _logger.LogDebug("產生 Bad Quality 資料: {TagName}, SID={SID}", tag.szName, szDynamicSID);
            }
        }
        else
        {
            // 🔥 延遲期間：不產生任何資料，直接返回空清單
            _logger.LogDebug("連線失敗未超過延遲時間 {ElapsedMs}ms < {RequiredMs}ms，不產生任何資料", 
                        (DateTime.Now - _dtFirstConnectionFailure).TotalMilliseconds, 
                        ReconnectDelay.TotalMilliseconds);
            
            // 返回空清單，不會觸發值變化偵測，也不會推播
            //_logger.LogDebug("連線失敗未超過延遲時間，不產生 Bad Quality 資料");
            // // 🔥 重要：未超過延遲時間，產生 Good Quality 但標記為失敗的資料
            // _logger.LogDebug("連線失敗未超過延遲時間 {ElapsedMs}ms < {RequiredMs}ms，暫不產生 Bad Quality", 
            //                (DateTime.Now - _dtFirstConnectionFailure).TotalMilliseconds, ReconnectDelay.TotalMilliseconds);
            
            // // 為所有點位產生 Good Quality（但 IsReadSuccess = false）的資料
            // foreach (var tag in batchGroup.tagList)
            // {
            //     var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
            //     if (nGlobalTagIndex == -1) nGlobalTagIndex = 0;

            //     var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
            //     var nN = nGlobalTagIndex + 1;
            //     var szDynamicSID = $"{nXXX}-S{nN}";

            //     resultList.Add(new RealtimeDataModel
            //     {
            //         dtTimestamp = DateTime.Now,
            //         szSID = szDynamicSID,
            //         szTagName = tag.szName,
            //         fValue = 0.0f,
            //         szUnit = tag.szUnit,
            //         szQuality = "Good",  // 🔥 暫時保持 Good
            //         szDeviceIP = _deviceConfig.szIP,
            //         nAddress = int.Parse(tag.szAddress),
            //         IsReadSuccess = false  // 🔥 但標記為失敗
            //     });
                
            //     _logger.LogTrace("產生延遲期間資料 (Good Quality 但未成功讀取): {TagName}, SID={SID}", tag.szName, szDynamicSID);
            // }
        }
        
        return resultList;
    }

    try
    {
        ushort[] batchRawData;
        _logger.LogDebug("開始讀取批次: 功能碼={FunctionCode}, 起始地址={StartAddress}, 暫存器數量={RegisterCount}, 點位數={TagCount}",
                       batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount, batchGroup.tagList.Count);

        // 根據功能碼執行批次讀取
        switch (batchGroup.nFunctionCode)
        {
            case 3: // Holding Registers
                {
                    var registerData = _modbusClient.ReadHoldingRegisters(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    batchRawData = ConvertBytesToUshorts(registerData.ToArray(), batchGroup.nRegisterCount);
                    _logger.LogTrace("Holding Registers 讀取成功: {DataLength} 個暫存器", batchRawData.Length);
                    break;
                }

            case 4: // Input Registers
                {
                    var registerData = _modbusClient.ReadInputRegisters(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    batchRawData = ConvertBytesToUshorts(registerData.ToArray(), batchGroup.nRegisterCount);
                    _logger.LogTrace("Input Registers 讀取成功: {DataLength} 個暫存器", batchRawData.Length);
                    break;
                }

            case 1: // Coils
                {
                    _logger.LogDebug("開始批量讀取 Coils: 起始地址={StartAddr}, 數量={Count}", batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    var coilMemory = _modbusClient.ReadCoils(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    var coilBytes = coilMemory.ToArray();
                    batchRawData = ConvertBitsToUshorts(coilBytes, batchGroup.nRegisterCount);
                    _logger.LogTrace("Coils 讀取成功: {DataLength} 個位元", batchRawData.Length);
                    break;
                }
                
            case 2: // Discrete Inputs
                {
                    _logger.LogDebug("開始批量讀取 Discrete Inputs: 起始地址={StartAddr}, 數量={Count}", batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    var discreteMemory = _modbusClient.ReadDiscreteInputs(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
                    var discreteBytes = discreteMemory.ToArray();
                    batchRawData = ConvertBitsToUshorts(discreteBytes, batchGroup.nRegisterCount);
                    _logger.LogTrace("Discrete Inputs 讀取成功: {DataLength} 個位元", batchRawData.Length);
                    break;
                }
                
            default:
                _logger.LogWarning("不支援的功能碼批量讀取: {FunctionCode}", batchGroup.nFunctionCode);
                return resultList;
        }

        _logger.LogDebug("批次讀取原始資料成功，開始分配給 {TagCount} 個點位", batchGroup.tagList.Count);

        // 將批量讀取的資料分配給各個點位
        foreach (var tag in batchGroup.tagList)
        {
            var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
            if (nGlobalTagIndex == -1) 
            {
                _logger.LogWarning("找不到點位 {TagName} 的全域索引", tag.szName);
                nGlobalTagIndex = 0;
            }

            var nRelativeOffset = tag.nParsedAddress - batchGroup.nStartAddress;
            
            _logger.LogTrace("處理點位 {TagName}: 地址={Address}, 相對偏移={Offset}, 需要={RegCount}個暫存器",
                            tag.szName, tag.nParsedAddress, nRelativeOffset, tag.nRegisterCount);
            
            if (nRelativeOffset >= 0 && nRelativeOffset + tag.nRegisterCount <= batchRawData.Length)
            {
                var tagRawData = new ushort[tag.nRegisterCount];
                Array.Copy(batchRawData, nRelativeOffset, tagRawData, 0, tag.nRegisterCount);

                var realtimeData = CreateRealtimeDataFromTag(tag, tagRawData, nModbusId, nGlobalTagIndex);
                if (realtimeData != null)
                {
                    resultList.Add(realtimeData);
                    _logger.LogTrace("點位 {TagName} 處理成功: 值={Value} {Unit}", 
                                    tag.szName, realtimeData.fValue, realtimeData.szUnit);
                }
                else
                {
                    _logger.LogWarning("點位 {TagName} 建立即時資料失敗", tag.szName);
                }
            }
            else
            {
                _logger.LogError("點位 {TagName} 的地址範圍超出批次資料: 相對偏移={RelativeOffset}, 暫存器數量={RegisterCount}, 批次大小={BatchSize}",
                                 tag.szName, nRelativeOffset, tag.nRegisterCount, batchRawData.Length);
            }
        }

        _logger.LogDebug("批次讀取完成: 成功處理 {SuccessCount}/{TotalTags} 個點位", 
                       resultList.Count, batchGroup.tagList.Count);
    }
    catch (Exception ex)
    {
        _logger.LogError("[DEBUG] ReadBatchGroup 發生異常: Type={Type}, Message={Message}", ex.GetType().Name, ex.Message);
        
        bool isConnectionError = IsConnectionRelatedError(ex);
        _logger.LogInformation("[DEBUG] 異常類型檢查: IsConnectionError={IsConnectionError}", isConnectionError);
        
        if (isConnectionError)
        {
            _logger.LogError(ex, "批次讀取連線失敗: FunctionCode={FunctionCode}, StartAddress={StartAddress}, Count={RegisterCount}", 
                        batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
            
            // 標記連線狀態為斷線
            IsConnected = false;
            RecordConnectionFailure();
            DisconnectInternal();
            
            // 🔥 關鍵：檢查是否超過延遲時間
            bool isTimeout = IsConnectionFailureTimeout();
            _logger.LogInformation("[DEBUG] Catch 區塊檢查超時: IsTimeout={IsTimeout}, FirstFailure={FirstFailure}, Delay={Delay}ms", 
                                isTimeout, _dtFirstConnectionFailure, ReconnectDelay.TotalMilliseconds);
            
            if (isTimeout)
            {
                _logger.LogWarning("連線失敗超過延遲時間，標記為 Bad 品質");
                
                // 為該批次的所有點位建立 Bad Quality 資料
                foreach (var tag in batchGroup.tagList)
                {
                    var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
                    if (nGlobalTagIndex == -1) nGlobalTagIndex = 0;

                    var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
                    var nN = nGlobalTagIndex + 1;
                    var szDynamicSID = $"{nXXX}-S{nN}";

                    resultList.Add(new RealtimeDataModel
                    {
                        dtTimestamp = DateTime.Now,
                        szSID = szDynamicSID,
                        szCoordinatorName = _deviceConfig.szCoordinatorName,
                        szTagName = tag.szName,
                        fValue = 0.0f,
                        szUnit = tag.szUnit,
                        szQuality = "Bad",
                        szDeviceIP = _deviceConfig.szIP,
                        nAddress = int.Parse(tag.szAddress),
                        IsReadSuccess = false
                    });
                }
            }
            else
            {
                // 🔥 延遲期間：不產生任何資料
                _logger.LogDebug("連線失敗未超過延遲時間 {ElapsedMs}ms < {RequiredMs}ms，不產生任何資料", 
                            (DateTime.Now - _dtFirstConnectionFailure).TotalMilliseconds, 
                            ReconnectDelay.TotalMilliseconds);
                
                // 不加入任何資料到 resultList，返回空清單
                // // 🔥 未超過延遲時間，產生 Good Quality 但標記為失敗的資料
                // _logger.LogDebug("連線失敗未超過延遲時間，暫不產生 Bad Quality: {ElapsedMs}ms < {RequiredMs}ms", 
                //             (DateTime.Now - _dtFirstConnectionFailure).TotalMilliseconds, ReconnectDelay.TotalMilliseconds);
                
                // foreach (var tag in batchGroup.tagList)
                // {
                //     var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
                //     if (nGlobalTagIndex == -1) nGlobalTagIndex = 0;

                //     var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
                //     var nN = nGlobalTagIndex + 1;
                //     var szDynamicSID = $"{nXXX}-S{nN}";

                //     resultList.Add(new RealtimeDataModel
                //     {
                //         dtTimestamp = DateTime.Now,
                //         szSID = szDynamicSID,
                //         szTagName = tag.szName,
                //         fValue = 0.0f,
                //         szUnit = tag.szUnit,
                //         szQuality = "Good",  // 🔥 延遲期間保持 Good
                //         szDeviceIP = _deviceConfig.szIP,
                //         nAddress = int.Parse(tag.szAddress),
                //         IsReadSuccess = false  // 🔥 但標記為失敗
                //     });
                // }
            }
        }
        else
        {
            _logger.LogError(ex, "讀取批次組資料時發生錯誤: FunctionCode={FunctionCode}, StartAddress={StartAddress}, Count={RegisterCount}", 
                        batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
        }
    }

    return resultList;
}
    // /// <summary>
    // /// 讀取批次組資料並智慧分配給各個點位
    // /// </summary>
    // /// <param name="batchGroup">批次組</param>
    // /// <param name="nModbusId">Modbus 站號</param>
    // /// <returns>即時資料清單</returns>
    // private List<RealtimeDataModel> ReadBatchGroup(ModbusBatchGroup batchGroup, byte nModbusId)
    // {
    //     _logger.LogInformation("[DEBUG] 開始 ReadBatchGroup: IP={IP}, FC={FC}, Addr={Addr}, Count={Count}", 
    //                           _deviceConfig.szIP, batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //     var resultList = new List<RealtimeDataModel>();

    //     if (batchGroup.tagList.Count == 0)
    //     {
    //         _logger.LogWarning("批次組沒有點位，跳過讀取: TagCount={Count}", batchGroup.tagList.Count);
    //         return resultList;
    //     }

    //     // 如果 ModbusClient 為 null，直接拋出異常讓 catch 處理生成 Bad Quality
    //     if (_modbusClient == null)
    //     {
    //         _logger.LogWarning("ModbusClient 為 null，將產生 Bad Quality 資料");
    //         throw new InvalidOperationException("ModbusClient 未初始化，無法讀取資料");
    //     }

    //     try
    //     {
    //         ushort[] batchRawData;
    //         _logger.LogDebug("開始讀取批次: 功能碼={FunctionCode}, 起始地址={StartAddress}, 暫存器數量={RegisterCount}, 點位數={TagCount}",
    //                        batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount, batchGroup.tagList.Count);

    //         // 根據功能碼執行批次讀取
    //         switch (batchGroup.nFunctionCode)
    //         {
    //             case 3: // Holding Registers
    //                 {
    //                     var registerData = _modbusClient.ReadHoldingRegisters(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     batchRawData = ConvertBytesToUshorts(registerData.ToArray(), batchGroup.nRegisterCount);
    //                     _logger.LogTrace("Holding Registers 讀取成功: {DataLength} 個暫存器", batchRawData.Length);
    //                     break;
    //                 }

    //             case 4: // Input Registers
    //                 {
    //                     var registerData = _modbusClient.ReadInputRegisters(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     batchRawData = ConvertBytesToUshorts(registerData.ToArray(), batchGroup.nRegisterCount);
    //                     _logger.LogTrace("Input Registers 讀取成功: {DataLength} 個暫存器", batchRawData.Length);
    //                     break;
    //                 }

    //             case 1: // Coils (單點位讀取，適用批量)
    //                 {
    //                     _logger.LogDebug("開始批量讀取 Coils: 起始地址={StartAddr}, 數量={Count}", batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     var coilMemory = _modbusClient.ReadCoils(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     var coilBytes = coilMemory.ToArray();
                        
    //                     // 將位元資料轉換為 ushort 陣列 (每個位元對應一個 ushort，值為 0 或 1)
    //                     batchRawData = ConvertBitsToUshorts(coilBytes, batchGroup.nRegisterCount);
    //                     _logger.LogTrace("Coils 讀取成功: {DataLength} 個位元", batchRawData.Length);
    //                     break;
    //                 }
    //             case 2: // Discrete Inputs (單點位讀取，適用批量)
    //                 {
    //                     _logger.LogDebug("開始批量讀取 Discrete Inputs: 起始地址={StartAddr}, 數量={Count}", batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     var discreteMemory = _modbusClient.ReadDiscreteInputs(nModbusId, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //                     var discreteBytes = discreteMemory.ToArray();
                        
    //                     // 將位元資料轉換為 ushort 陣列 (每個位元對應一個 ushort，值為 0 或 1)
    //                     batchRawData = ConvertBitsToUshorts(discreteBytes, batchGroup.nRegisterCount);
    //                     _logger.LogTrace("Discrete Inputs 讀取成功: {DataLength} 個位元", batchRawData.Length);
    //                     break;
    //                 }
    //             default:
    //                 _logger.LogWarning("不支援的功能碼批量讀取: {FunctionCode}", batchGroup.nFunctionCode);
    //                 return resultList;
    //         }

    //         _logger.LogDebug("批次讀取原始資料成功，開始分配給 {TagCount} 個點位", batchGroup.tagList.Count);

    //         // 將批量讀取的資料分配給各個點位
    //         foreach (var tag in batchGroup.tagList)
    //         {
    //             var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
    //             if (nGlobalTagIndex == -1) 
    //             {
    //                 _logger.LogWarning("找不到點位 {TagName} 的全域索引", tag.szName);
    //                 nGlobalTagIndex = 0;
    //             }

    //             // 計算此點位在批次資料中的相對位置
    //             var nRelativeOffset = tag.nParsedAddress - batchGroup.nStartAddress;
                
    //             _logger.LogTrace("處理點位 {TagName}: 地址={Address}, 相對偏移={Offset}, 需要={RegCount}個暫存器",
    //                             tag.szName, tag.nParsedAddress, nRelativeOffset, tag.nRegisterCount);
                
    //             // 確保不會超出陣列範圍
    //             if (nRelativeOffset >= 0 && nRelativeOffset + tag.nRegisterCount <= batchRawData.Length)
    //             {
    //                 // 提取此點位對應的暫存器資料
    //                 var tagRawData = new ushort[tag.nRegisterCount];
    //                 Array.Copy(batchRawData, nRelativeOffset, tagRawData, 0, tag.nRegisterCount);

    //                 // 計算物理量並建立即時資料
    //                 var realtimeData = CreateRealtimeDataFromTag(tag, tagRawData, nModbusId, nGlobalTagIndex);
    //                 if (realtimeData != null)
    //                 {
    //                     resultList.Add(realtimeData);
    //                     _logger.LogTrace("點位 {TagName} 處理成功: 值={Value} {Unit}", 
    //                                     tag.szName, realtimeData.fValue, realtimeData.szUnit);
    //                 }
    //                 else
    //                 {
    //                     _logger.LogWarning("點位 {TagName} 建立即時資料失敗", tag.szName);
    //                 }
    //             }
    //             else
    //             {
    //                 _logger.LogError("點位 {TagName} 的地址範圍超出批次資料: 相對偏移={RelativeOffset}, 暫存器數量={RegisterCount}, 批次大小={BatchSize}",
    //                                  tag.szName, nRelativeOffset, tag.nRegisterCount, batchRawData.Length);
    //             }
    //         }

    //         _logger.LogDebug("批次讀取完成: 成功處理 {SuccessCount}/{TotalTags} 個點位", 
    //                        resultList.Count, batchGroup.tagList.Count);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError("[DEBUG] ReadBatchGroup 發生異常: Type={Type}, Message={Message}", ex.GetType().Name, ex.Message);
    //         // 檢查是否為連線相關錯誤
    //         bool isConnectionError = IsConnectionRelatedError(ex);
    //         _logger.LogInformation("[DEBUG] 異常類型檢查: IsConnectionError={IsConnectionError}", isConnectionError);
            
    //         if (isConnectionError)
    //         {
    //             _logger.LogError(ex, "批次讀取連線失敗: FunctionCode={FunctionCode}, StartAddress={StartAddress}, Count={RegisterCount}", 
    //                            batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
                
    //             // 標記連線狀態為斷線並記錄失敗時間
    //             IsConnected = false;
    //             RecordConnectionFailure(); // 記錄連線失敗時間（僅第一次）
                
    //             // 檢查是否已超過延遲時間才標記為 Bad quality
    //             bool isTimeout = IsConnectionFailureTimeout();
    //             _logger.LogInformation("[DEBUG] 檢查超時: IsTimeout={IsTimeout}, FirstFailure={FirstFailure}, Now={Now}, Delay={Delay}ms", 
    //                                  isTimeout, _dtFirstConnectionFailure, DateTime.Now, ReconnectDelay.TotalMilliseconds);
                
    //             if (isTimeout)
    //             {
    //                 _logger.LogWarning("連線失敗超過延遲時間，標記為 Bad 品質: {Delay}ms", ReconnectDelay.TotalMilliseconds);
    //                 DisconnectInternal();   // 🔥 新增：清理連線資源，確保可重新連線
                    
    //                 // 為該批次的所有點位建立 quality = "Bad" 的資料
    //                 foreach (var tag in batchGroup.tagList)
    //                 {
    //                     var nGlobalTagIndex = _deviceConfig.tagList.FindIndex(t => t.szName == tag.szName);
    //                     if (nGlobalTagIndex == -1) nGlobalTagIndex = 0;

    //                     var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
    //                     var nN = nGlobalTagIndex + 1;
    //                     var szDynamicSID = $"{nXXX}-S{nN}";

    //                     resultList.Add(new RealtimeDataModel
    //                     {
    //                         dtTimestamp = DateTime.Now,
    //                         szSID = szDynamicSID,
    //                         szTagName = tag.szName,
    //                         fValue = 0.0f,
    //                         szUnit = tag.szUnit,
    //                         szQuality = "Bad",
    //                         szDeviceIP = _deviceConfig.szIP,
    //                         nAddress = int.Parse(tag.szAddress),
    //                         IsReadSuccess = false  // 標記為失敗
    //                     });
    //                 }
    //             }
    //             else
    //             {
    //                 _logger.LogDebug("連線失敗未超過延遲時間，不產生 Bad Quality 資料: {ElapsedMs}ms < {RequiredMs}ms", 
    //                                (DateTime.Now - _dtFirstConnectionFailure).TotalMilliseconds, ReconnectDelay.TotalMilliseconds);
    //             }
    //         }
    //         else
    //         {
    //             _logger.LogError(ex, "讀取批次組資料時發生錯誤: FunctionCode={FunctionCode}, StartAddress={StartAddress}, Count={RegisterCount}", 
    //                            batchGroup.nFunctionCode, batchGroup.nStartAddress, batchGroup.nRegisterCount);
    //         }
    //     }

    //     return resultList;
    // }

    /// <summary>
    /// 將位元組陣列轉換為 ushort 陣列 (Modbus Big Endian 格式)
    /// </summary>
    /// <param name="bytes">位元組陣列</param>
    /// <param name="nRegisterCount">預期的暫存器數量</param>
    /// <returns>ushort 陣列</returns>
    private ushort[] ConvertBytesToUshorts(byte[] bytes, int nRegisterCount)
    {
        var ushortArray = new ushort[nRegisterCount];
        for (int i = 0; i < nRegisterCount && i * 2 + 1 < bytes.Length; i++)
        {
            // Modbus 使用 Big Endian：高位元組在前
            ushortArray[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
        }
        _logger.LogTrace("Modbus 字節轉換: {ByteCount} bytes → {RegisterCount} registers: {Data}",
                        Math.Min(bytes.Length, nRegisterCount * 2), nRegisterCount, string.Join(",", ushortArray));
        return ushortArray;
    }
    /// <summary>
    /// 將位元組陣列轉換為 ushort 陣列 (用於 Coils 和 Discrete Inputs)
    /// 每個位元對應一個 ushort，值為 0 或 1
    /// </summary>
    /// <param name="bytes">位元組陣列</param>
    /// <param name="nBitCount">預期的位元數量</param>
    /// <returns>ushort 陣列</returns>
    private ushort[] ConvertBitsToUshorts(byte[] bytes, int nBitCount)
    {
        var ushortArray = new ushort[nBitCount];
        
        for (int i = 0; i < nBitCount; i++)
        {
            var nByteIndex = i / 8;      // 確定是第幾個位元組
            var nBitIndex = i % 8;       // 確定是位元組內的第幾個位元
            
            if (nByteIndex < bytes.Length)
            {
                // 檢查對應位元是否為 1
                var isSet = (bytes[nByteIndex] & (1 << nBitIndex)) != 0;
                ushortArray[i] = (ushort)(isSet ? 1 : 0);
            }
            else
            {
                ushortArray[i] = 0; // 超出範圍的位元設為 0
            }
        }
        
        _logger.LogTrace("Modbus 位元轉換: {ByteCount} bytes → {BitCount} bits: {Data}",
                        bytes.Length, nBitCount, string.Join(",", ushortArray));
        return ushortArray;
    }

    /// <summary>
    /// 從點位模型建立即時資料
    /// </summary>
    /// <param name="tag">點位模型</param>
    /// <param name="rawData">原始暫存器資料</param>
    /// <param name="nModbusId">Modbus 站號</param>
    /// <param name="nTagIndex">點位索引</param>
    /// <returns>即時資料模型</returns>
    private RealtimeDataModel? CreateRealtimeDataFromTag(ModbusTagModel tag, ushort[] rawData, byte nModbusId, int nTagIndex)
    {
        try
        {
            // 計算物理量
            var fPhysicalValue = tag.CalculatePhysicalValue(rawData);

            // 動態生成 SID
            var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
            var nN = nTagIndex + 1;
            var szDynamicSID = $"{nXXX}-S{nN}";

            return new RealtimeDataModel
            {
                dtTimestamp = DateTime.Now,
                szSID = szDynamicSID,
                szCoordinatorName = _deviceConfig.szCoordinatorName,
                szTagName = tag.szName,
                fValue = fPhysicalValue,
                szUnit = tag.szUnit,
                szQuality = "Good",
                szDeviceIP = _deviceConfig.szIP,
                nAddress = int.Parse(tag.szAddress),
                IsReadSuccess = true  // 成功讀取並處理
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理點位資料時發生錯誤: {TagName}", tag.szName);
            return null;
        }
    }

    /// <summary>
    /// 讀取單個點位資料
    /// </summary>
    /// <param name="tag">點位模型</param>
    /// <param name="nModbusId">Modbus 站號</param>
    /// <param name="nTagIndex">點位索引，用於 SID 計算</param>
    /// <returns>即時資料模型</returns>
    private RealtimeDataModel? ReadSingleTag(ModbusTagModel tag, byte nModbusId, int nTagIndex)
    {
        if (_modbusClient == null)
        {
            _logger.LogWarning("ModbusClient 未初始化，無法讀取點位 {TagName}", tag.szName);
            return null;
        }

        try
        {
            _logger.LogTrace("開始讀取點位 {TagName}: 功能碼={FC}, 地址={Addr}, 暫存器數={RegCount}",
                            tag.szName, tag.nFunctionCode, tag.nParsedAddress, tag.nRegisterCount);
                            
            ushort[] rawData;

            // 根據功能碼執行對應的讀取操作
            switch (tag.nFunctionCode)
            {
                case 3: // Holding Registers
                    {
                        _logger.LogTrace("執行 ReadHoldingRegisters: ModbusId={Id}, 地址={Addr}, 數量={Count}",
                                        nModbusId, tag.nParsedAddress, tag.nRegisterCount);
                        var registerData = _modbusClient.ReadHoldingRegisters(nModbusId, (ushort)tag.nParsedAddress, (ushort)tag.nRegisterCount);
                        var bytes = registerData.ToArray();
                        rawData = ConvertBytesToUshorts(bytes, tag.nRegisterCount);
                        _logger.LogTrace("Holding Register 讀取成功: {Data}", string.Join(",", rawData));
                        break;
                    }

                case 4: // Input Registers
                    {
                        _logger.LogTrace("執行 ReadInputRegisters: ModbusId={Id}, 地址={Addr}, 數量={Count}",
                                        nModbusId, tag.nParsedAddress, tag.nRegisterCount);
                        var registerData = _modbusClient.ReadInputRegisters(nModbusId, (ushort)tag.nParsedAddress, (ushort)tag.nRegisterCount);
                        var bytes = registerData.ToArray();
                        rawData = ConvertBytesToUshorts(bytes, tag.nRegisterCount);
                        _logger.LogTrace("Input Register 讀取成功: {Data}", string.Join(",", rawData));
                        break;
                    }

                case 1: // Coils
                    {
                        _logger.LogTrace("執行 ReadCoils: ModbusId={Id}, 地址={Addr}", nModbusId, tag.nParsedAddress);
                        var coilMemory = _modbusClient.ReadCoils(nModbusId, (ushort)tag.nParsedAddress, 1);
                        var coilBytes = coilMemory.ToArray();
                        
                        if (coilBytes == null || coilBytes.Length == 0)
                        {
                            _logger.LogWarning("Coil 讀取回傳空資料: ModbusId={Id}, 地址={Addr}", nModbusId, tag.nParsedAddress);
                            throw new InvalidOperationException($"Coil 讀取回傳空資料");
                        }
                        
                        // 檢查 bit 值 (0 或 1)
                        var bitValue = (coilBytes[0] & 0x01) != 0 ? 1 : 0;
                        rawData = new ushort[] { (ushort)bitValue };
                        _logger.LogTrace("Coil 讀取成功: 原始位元組={Bytes}, 解析值={Value}", 
                                        string.Join(",", coilBytes.Select(b => $"0x{b:X2}")), rawData[0]);
                        break;
                    }

                case 2: // Discrete Inputs
                    {
                        _logger.LogTrace("執行 ReadDiscreteInputs: ModbusId={Id}, 地址={Addr}", nModbusId, tag.nParsedAddress);
                        var discreteMemory = _modbusClient.ReadDiscreteInputs(nModbusId, (ushort)tag.nParsedAddress, 1);
                        var discreteBytes = discreteMemory.ToArray();
                        
                        if (discreteBytes == null || discreteBytes.Length == 0)
                        {
                            _logger.LogWarning("Discrete Input 讀取回傳空資料: ModbusId={Id}, 地址={Addr}", nModbusId, tag.nParsedAddress);
                            throw new InvalidOperationException($"Discrete Input 讀取回傳空資料");
                        }
                        
                        // 檢查 bit 值 (0 或 1)
                        var bitValue = (discreteBytes[0] & 0x01) != 0 ? 1 : 0;
                        rawData = new ushort[] { (ushort)bitValue };
                        _logger.LogTrace("Discrete Input 讀取成功: 原始位元組={Bytes}, 解析值={Value}", 
                                        string.Join(",", discreteBytes.Select(b => $"0x{b:X2}")), rawData[0]);
                        break;
                    }

                default:
                    _logger.LogWarning("不支援的功能碼: {FunctionCode} 於點位: {TagName}", tag.nFunctionCode, tag.szName);
                    return null;
            }

            // 計算物理量
            var fPhysicalValue = tag.CalculatePhysicalValue(rawData);
            _logger.LogTrace("點位 {TagName} 物理量計算: 原始值={Raw} → 物理值={Physical}", 
                            tag.szName, string.Join(",", rawData), fPhysicalValue);

            // 動態生成當前 ModbusId 對應的 SID
            // SID 計算公式: XXX = DatabaseId*65536 + ModbusID*256 + 1, N 為點位順序
            var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
            var nN = nTagIndex + 1;
            var szDynamicSID = $"{nXXX}-S{nN}";

            // 建立即時資料物件
            var realtimeData = new RealtimeDataModel
            {
                dtTimestamp = DateTime.Now,
                szSID = szDynamicSID,
                szTagName = tag.szName,
                fValue = fPhysicalValue,
                szUnit = tag.szUnit,
                szQuality = "Good",
                szDeviceIP = _deviceConfig.szIP,
                nAddress = int.Parse(tag.szAddress)
            };

            _logger.LogDebug("讀取點位成功: {TagName} = {Value} {Unit} (SID: {SID})", 
                           tag.szName, fPhysicalValue, tag.szUnit, szDynamicSID);

            return realtimeData;
        }
        catch (Exception ex)
        {
            // 檢查是否為連線相關錯誤
            if (IsConnectionRelatedError(ex))
            {
                _logger.LogError(ex, "讀取點位時發生連線錯誤: {TagName} (地址: {Address}, 功能碼: {FunctionCode})", 
                               tag.szName, tag.szAddress, tag.nFunctionCode);
                IsConnected = false; // 標記連線狀態為斷線
                RecordConnectionFailure(); // 記錄連線失敗時間（僅第一次）
            }
            else
            {
                _logger.LogError(ex, "讀取點位失敗: {TagName} (地址: {Address}, 功能碼: {FunctionCode})", 
                               tag.szName, tag.szAddress, tag.nFunctionCode);
            }

            // 檢查是否需要返回 Bad quality 資料
            var szQuality = "Good";
            var isReadSuccess = false; // 連線錯誤時預設為失敗
            
            if (IsConnectionRelatedError(ex))
            {
                // 只有超過 20倍 timeout 時間才標記為 Bad Quality
                if (IsConnectionFailureTimeout())
                {
                    szQuality = "Bad";
                    DisconnectInternal();   // 🔥 新增：清理連線資源，確保可重新連線
                    _logger.LogWarning("連線失敗超過延遲時間，標記為 Bad 品質資料: {TagName}", tag.szName);
                }
                else
                {
                    // 未超過延遲時間，暫時保持 Good 品質但標記為失敗
                    szQuality = "Good";
                    _logger.LogDebug("連線失敗未超過延遲時間，暫時保持 Good 品質: {TagName}", tag.szName);
                }
            }
            else
            {
                // 非連線錯誤也標記為失敗
                _logger.LogWarning("讀取失敗，保持 Good 品質但標記失敗: {TagName}", tag.szName);
            }

            // 返回錯誤狀態的資料
            var nXXX = _deviceConfig.nDatabaseId * 65536 + nModbusId * 256 + 1;
            var nN = nTagIndex + 1;
            var szDynamicSID = $"{nXXX}-S{nN}";
            
            return new RealtimeDataModel
            {
                dtTimestamp = DateTime.Now,
                szSID = szDynamicSID,
                szTagName = tag.szName,
                fValue = 0.0f,
                szUnit = tag.szUnit,
                szQuality = szQuality,
                szDeviceIP = _deviceConfig.szIP,
                nAddress = int.Parse(tag.szAddress),
                IsReadSuccess = isReadSuccess  // 標記實際讀取是否成功
            };
        }
    }

    /// <summary>
    /// 檢查連線失敗是否已超過延遲時間
    /// </summary>
    /// <returns>超過延遲時間回傳 true</returns>
    private bool IsConnectionFailureTimeout()
    {
        if (_dtFirstConnectionFailure == DateTime.MinValue)
            return false;
            
        return DateTime.Now - _dtFirstConnectionFailure > ReconnectDelay;
    }

    /// <summary>
    /// 記錄連線失敗開始時間
    /// </summary>
    private void RecordConnectionFailure()
    {
        if (_dtFirstConnectionFailure == DateTime.MinValue)
        {
            _dtFirstConnectionFailure = DateTime.Now;
            _logger.LogWarning("[DEBUG] 開始記錄連線失敗時間: {IP}:{Port}, 延遲時間={Delay}ms, FirstFailureTime={FirstFailureTime}", 
                             _deviceConfig.szIP, _deviceConfig.nPort, ReconnectDelay.TotalMilliseconds, _dtFirstConnectionFailure.ToString("HH:mm:ss.fff"));
        }
        else
        {
            _logger.LogTrace("[DEBUG] 連線失敗時間已存在，不重複記錄: {IP}:{Port}, ExistingTime={ExistingTime}", 
                           _deviceConfig.szIP, _deviceConfig.nPort, _dtFirstConnectionFailure.ToString("HH:mm:ss.fff"));
        }
    }

    /// <summary>
/// 檢查連線是否需要重新建立
/// 🔥 修正：加入重連嘗試時間間隔控制，避免頻繁重連
/// </summary>
/// <returns>需要重連回傳 true</returns>
public bool ShouldReconnect()
{
    // 如果已連線，不需要重連
    if (IsConnected)
        return false;

    var now = DateTime.Now;
    
    // 🔥 修正：檢查距離上次重連嘗試是否已經過了延遲時間
    // 避免在短時間內頻繁嘗試重連
    if (_dtLastReconnectAttempt != DateTime.MinValue)
    {
        var timeSinceLastAttempt = now - _dtLastReconnectAttempt;
        if (timeSinceLastAttempt < ReconnectDelay)
        {
            _logger.LogTrace("距離上次重連嘗試時間過短，暫不重連: {ElapsedMs}ms < {RequiredMs}ms", 
                           timeSinceLastAttempt.TotalMilliseconds, ReconnectDelay.TotalMilliseconds);
            return false;
        }
    }

    // 如果連線從未成功或最後成功讀取時間超過重連延遲，則允許重連
    bool canReconnect = _dtLastSuccessfulRead == DateTime.MinValue || 
                       now - _dtLastSuccessfulRead > ReconnectDelay;
    
    if (canReconnect)
    {
        _logger.LogDebug("設備 {IP}:{Port} 滿足重連條件: 上次成功={LastSuccess}, 上次嘗試={LastAttempt}, 當前={Now}, 延遲={Delay}ms", 
                       _deviceConfig.szIP, _deviceConfig.nPort, 
                       _dtLastSuccessfulRead == DateTime.MinValue ? "從未連線" : _dtLastSuccessfulRead.ToString("HH:mm:ss"),
                       _dtLastReconnectAttempt == DateTime.MinValue ? "從未嘗試" : _dtLastReconnectAttempt.ToString("HH:mm:ss"),
                       now.ToString("HH:mm:ss"), ReconnectDelay.TotalMilliseconds);
    }
    
    return canReconnect;
}

    /// <summary>
    /// 取得設備狀態資訊
    /// </summary>
    /// <returns>設備狀態</returns>
    public object GetDeviceStatus()
    {
        return new
        {
            IP = _deviceConfig.szIP,
            Port = _deviceConfig.nPort,
            IsConnected = IsConnected,
            TagCount = _deviceConfig.tagList.Count,
            LastSuccessfulRead = _dtLastSuccessfulRead,
            ModbusIds = string.Join(",", _deviceConfig.GetModbusIdArray())
        };
    }

    /// <summary>
    /// 偵測值變化或品質變化，只返回有變化的資料點
    /// </summary>
    /// <param name="currentDataList">當前讀取的資料清單</param>
    /// <returns>發生值變化或品質變化的資料清單</returns>
    private List<RealtimeDataModel> DetectValueChanges(List<RealtimeDataModel> currentDataList)
    {
        var changedDataList = new List<RealtimeDataModel>();

        foreach (var currentData in currentDataList)
        {
            if (string.IsNullOrEmpty(currentData.szSID))
                continue;

            bool hasValueChanged = false;
            bool hasQualityChanged = false;

            // 檢查值是否發生變化
            if (_lastPublishedValues.TryGetValue(currentData.szSID, out var lastValue))
            {
                // 比較浮點數值，使用小的容差值避免浮點數精度問題
                if (Math.Abs(currentData.fValue - lastValue) > 0.0001f)
                {
                    hasValueChanged = true;
                }
            }
            else
            {
                // 第一次讀取值，視為有變化
                hasValueChanged = true;
            }

            // 檢查品質是否發生變化
            if (_lastPublishedQuality.TryGetValue(currentData.szSID, out var lastQuality))
            {
                if (!string.Equals(currentData.szQuality, lastQuality, StringComparison.OrdinalIgnoreCase))
                {
                    hasQualityChanged = true;
                }
            }
            else
            {
                // 第一次讀取品質，視為有變化
                hasQualityChanged = true;
            }

            bool hasChanged = hasValueChanged || hasQualityChanged;

            if (hasChanged)
            {
                // 更新值快取
                _lastPublishedValues.AddOrUpdate(currentData.szSID, currentData.fValue, (szKey, szOldValue) => currentData.fValue);
                
                // 更新品質快取
                _lastPublishedQuality.AddOrUpdate(currentData.szSID, currentData.szQuality, (szKey, szOldQuality) => currentData.szQuality);
                
                // 添加到變化清單
                changedDataList.Add(currentData);
                
                // 記錄變化詳情
                var szChangeReason = "";
                if (hasValueChanged && hasQualityChanged)
                {
                    szChangeReason = "值和品質都變化";
                }
                else if (hasValueChanged)
                {
                    szChangeReason = "值變化";
                }
                else if (hasQualityChanged)
                {
                    szChangeReason = "品質變化";
                }
                
                _logger.LogTrace("偵測到變化: SID={SID}, 原因={Reason}, 值={NewValue}, 品質={NewQuality}", 
                               currentData.szSID, szChangeReason, currentData.fValue.ToString("F4"), currentData.szQuality);
            }
        }

        return changedDataList;
    }

    /// <summary>
    /// 判斷是否為連線相關錯誤
    /// </summary>
    /// <param name="ex">異常物件</param>
    /// <returns>是否為連線相關錯誤</returns>
    private static bool IsConnectionRelatedError(Exception ex)
    {
        // 檢查常見的連線相關異常
        return ex is System.Net.Sockets.SocketException ||
               ex is System.IO.IOException ||
               ex is TimeoutException ||
               ex is InvalidOperationException ||  // 添加 InvalidOperationException
               ex is NullReferenceException ||     // 添加 NullReferenceException (ModbusClient為null時)
               ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("transport", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("ModbusClient", StringComparison.OrdinalIgnoreCase);  // 添加 ModbusClient 檢查
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            
            Task.Run(async () =>
            {
                await DisconnectAsync();
                _connectionSemaphore.Dispose();
                
                // 清理值變化快取
                _lastPublishedValues.Clear();
            });
        }
    }
}