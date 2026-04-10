using System.Collections.Concurrent;
using System.Text.Json;
using NCalc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 計算點位服務 — 根據使用者自訂公式，從多個原始點位衍生計算值
/// </summary>
public class CalculatedPointService
{
    private readonly ILogger<CalculatedPointService> _logger;
    private readonly IDataRepository _dataRepository;
    private readonly RealtimeDataStorageService _realtimeDataStorageService;
    private readonly HistoryDataStorageService _historyDataStorageService;
    private readonly MqttPublishService _mqttPublishService;

    private List<CalculatedPointConfig> _calculatedPoints = new();
    private readonly object _configLock = new();
    private Timer? _reloadTimer;

    public CalculatedPointService(
        ILogger<CalculatedPointService> logger,
        IDataRepository dataRepository,
        RealtimeDataStorageService realtimeDataStorageService,
        HistoryDataStorageService historyDataStorageService,
        MqttPublishService mqttPublishService)
    {
        _logger = logger;
        _dataRepository = dataRepository;
        _realtimeDataStorageService = realtimeDataStorageService;
        _historyDataStorageService = historyDataStorageService;
        _mqttPublishService = mqttPublishService;
    }

    /// <summary>
    /// 初始化 — 載入設定並啟動定時重載
    /// </summary>
    public async Task InitializeAsync()
    {
        await ReloadConfigAsync();

        // 每 60 秒重新載入設定（Web 端改完不用重啟 Engine）
        _reloadTimer = new Timer(async _ =>
        {
            try { await ReloadConfigAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "重載計算點位設定失敗"); }
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        _logger.LogInformation("計算點位服務已初始化，共載入 {Count} 個計算點位", _calculatedPoints.Count);
    }

    /// <summary>
    /// 從 DB 重新載入計算點位設定
    /// </summary>
    private async Task ReloadConfigAsync()
    {
        try
        {
            var allPoints = await _dataRepository.GetAllCalculatedPointsAsync();
            var enabledPoints = allPoints.Where(p => p.isEnabled).ToList();

            var configs = new List<CalculatedPointConfig>();
            foreach (var point in enabledPoints)
            {
                try
                {
                    var inputMappings = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        point.szInputMappings, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (inputMappings == null || inputMappings.Count == 0)
                    {
                        _logger.LogWarning("計算點位 {SID} 的 InputMappings 為空，跳過", point.szSID);
                        continue;
                    }

                    configs.Add(new CalculatedPointConfig
                    {
                        szSID = point.szSID,
                        szName = point.szName,
                        szUnit = point.szUnit,
                        szGroupName = point.szGroupName,
                        szFormula = point.szFormula,
                        InputMappings = inputMappings
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "計算點位 {SID} 的 InputMappings JSON 解析失敗", point.szSID);
                }
            }

            lock (_configLock)
            {
                _calculatedPoints = configs;
            }

            _logger.LogDebug("計算點位設定重載完成: {Count} 個啟用", configs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入計算點位設定失敗");
        }
    }

    /// <summary>
    /// 計算所有衍生值並發布至 MQTT + 儲存，回傳計算結果供警報監控使用
    /// </summary>
    public List<RealtimeDataModel> CalculateAndPublish(IEnumerable<RealtimeDataModel> triggerDataList)
    {
        List<CalculatedPointConfig> configs;
        lock (_configLock)
        {
            if (_calculatedPoints.Count == 0) return new List<RealtimeDataModel>();
            configs = _calculatedPoints.ToList();
        }

        var latestValues = _realtimeDataStorageService.GetAllLatestValues();
        var calculatedResults = new List<RealtimeDataModel>();

        foreach (var config in configs)
        {
            try
            {
                var result = EvaluateFormula(config, latestValues);
                if (result != null)
                {
                    calculatedResults.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "計算點位 {SID} 計算失敗", config.szSID);
            }
        }

        if (calculatedResults.Count == 0) return calculatedResults;

        // 送入儲存服務（歷史 + 最新值）
        _historyDataStorageService.AddRealtimeDataBatch(calculatedResults);
        _realtimeDataStorageService.AddRealtimeDataBatch(calculatedResults);

        // 發布至 MQTT
        _ = Task.Run(async () =>
        {
            foreach (var data in calculatedResults)
            {
                try
                {
                    await _mqttPublishService.PublishRealtimeDataAsync(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "計算點位 {SID} MQTT 發布失敗", data.szSID);
                }
            }
        });

        _logger.LogTrace("計算點位處理完成: {Count} 個", calculatedResults.Count);

        return calculatedResults;
    }

    /// <summary>
    /// 執行單個計算點位的公式計算（使用 NCalc 引擎）
    /// </summary>
    private RealtimeDataModel? EvaluateFormula(CalculatedPointConfig config, IReadOnlyDictionary<string, LatestDataModel> latestValues)
    {
        var szQuality = "Good";

        // 建立 NCalc 表達式
        var expression = new Expression(config.szFormula);

        // 綁定變數參數
        foreach (var kvp in config.InputMappings)
        {
            var szVarName = kvp.Key;
            var szSourceSID = kvp.Value;

            if (!latestValues.TryGetValue(szSourceSID, out var sourceData))
            {
                szQuality = "Bad";
                expression.Parameters[szVarName] = 0.0;
                continue;
            }

            if (sourceData.nQuality == 0)
            {
                szQuality = "Bad";
            }

            expression.Parameters[szVarName] = (double)sourceData.fValue;
        }

        // 使用 NCalc 計算表達式
        float fResult;
        try
        {
            var objResult = expression.Evaluate();
            fResult = Convert.ToSingle(objResult);

            if (float.IsNaN(fResult) || float.IsInfinity(fResult))
            {
                _logger.LogWarning("計算點位 {SID} 結果無效 (NaN/Infinity)，公式: {Formula}", config.szSID, config.szFormula);
                szQuality = "Bad";
                fResult = 0f;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "計算點位 {SID} 公式計算失敗，公式: {Formula}", config.szSID, config.szFormula);
            return null;
        }

        return new RealtimeDataModel
        {
            dtTimestamp = DateTime.Now,
            szSID = config.szSID,
            szTagName = config.szName,
            fValue = fResult,
            szUnit = config.szUnit,
            szQuality = szQuality,
            szDeviceIP = "Calculated",
            nAddress = 0,
            szCoordinatorName = string.IsNullOrEmpty(config.szGroupName) ? "Calculated" : config.szGroupName,
            IsReadSuccess = true
        };
    }

    /// <summary>
    /// 計算點位設定（記憶體中的解析後版本）
    /// </summary>
    private class CalculatedPointConfig
    {
        public string szSID { get; set; } = "";
        public string szName { get; set; } = "";
        public string szUnit { get; set; } = "";
        public string szGroupName { get; set; } = "";
        public string szFormula { get; set; } = "";
        public Dictionary<string, string> InputMappings { get; set; } = new();
    }
}
