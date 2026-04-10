using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Engine.Services;
using System.Diagnostics;
using System.Linq;

namespace ScadaEngine.Engine;

/// <summary>
/// SCADA 引擎主要工作服務，整合 Modbus 採集、MQTT 發布及資料儲存功能
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ModbusCollectionManager _modbusCollectionManager;
    private readonly HistoryDataStorageService _historyDataStorageService;
    private readonly RealtimeDataStorageService _realtimeDataStorageService;
    private readonly AlarmMonitorService _alarmMonitorService;
    private readonly CalculatedPointService _calculatedPointService;
    private readonly IServiceProvider _serviceProvider;
    private Process? _pythonProcess;

    public Worker(ILogger<Worker> logger, ModbusCollectionManager modbusCollectionManager,
                  HistoryDataStorageService historyDataStorageService,
                  RealtimeDataStorageService realtimeDataStorageService,
                  AlarmMonitorService alarmMonitorService,
                  CalculatedPointService calculatedPointService,
                  IServiceProvider serviceProvider)
    {
        _logger = logger;
        _modbusCollectionManager = modbusCollectionManager;
        _historyDataStorageService = historyDataStorageService;
        _realtimeDataStorageService = realtimeDataStorageService;
        _alarmMonitorService = alarmMonitorService;
        _calculatedPointService = calculatedPointService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 背景服務主要執行方法
    /// </summary>
    /// <param name="stoppingToken">停止令牌</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SCADA 引擎工作服務啟動 at: {time}", DateTimeOffset.Now);

        try
        {
            // 啟動 Python 演算法服務
            StartPythonAlgorithmService();

            // 訂閱 Modbus 資料採集事件
            _modbusCollectionManager.DataCollected += OnModbusDataCollected;
            _modbusCollectionManager.DeviceStatusChanged += OnModbusDeviceStatusChanged;

            // 啟動 Modbus 採集管理器
            await _modbusCollectionManager.StartAsync();

            _logger.LogInformation("Modbus 採集管理器已啟動");

            // 主迴圈：監控系統狀態
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 每分鐘記錄一次系統狀態
                    LogSystemStatus();

                    // 等待 60 秒或直到收到停止信號
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常停止，跳出迴圈
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "主迴圈執行時發生錯誤");
                    
                    // 發生錯誤時短暫等待後繼續
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SCADA 引擎工作服務執行時發生嚴重錯誤");
            throw;
        }
        finally
        {
            // 清理資源
            await CleanupResourcesAsync();
        }
    }

    /// <summary>
    /// 處理 Modbus 資料採集事件
    /// </summary>
    /// <param name="sender">事件來源</param>
    /// <param name="e">事件參數</param>
    private async void OnModbusDataCollected(object? sender, RealtimeDataCollectedEventArgs e)
    {
        try
        {
            _logger.LogTrace("收到 Modbus 資料: 設備 {DeviceKey}, 點位數量 {TagCount}",
                           e.DeviceKey, e.RealtimeDataList.Count);

            // 1. 添加資料到歷史資料儲存服務 (每分鐘自動儲存)
            _historyDataStorageService.AddRealtimeDataBatch(e.RealtimeDataList);

            // 2. 添加資料到即時資料儲存服務 (每五秒自動儲存/覆蓋)
            _realtimeDataStorageService.AddRealtimeDataBatch(e.RealtimeDataList);

            // 3. 警報監控 — 比對 AlarmRules 規則，觸發/恢復時寫入 EventLog
            await _alarmMonitorService.EvaluateBatchAsync(e.RealtimeDataList);

            // 4. 計算點位 — 根據公式計算衍生值，發布至 MQTT 並儲存
            var calcResults = _calculatedPointService.CalculateAndPublish(e.RealtimeDataList);

            // 5. 計算點位也要進行警報檢查
            if (calcResults.Count > 0)
                await _alarmMonitorService.EvaluateBatchAsync(calcResults);

            foreach (var data in e.RealtimeDataList)
            {
                _logger.LogTrace("點位資料: {TagName} = {Value} {Unit} (品質: {Quality})",
                               data.szTagName, data.fValue, data.szUnit, data.szQuality);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理 Modbus 資料採集事件時發生錯誤");
        }
    }

    /// <summary>
    /// 處理 Modbus 設備狀態變更事件
    /// </summary>
    /// <param name="sender">事件來源</param>
    /// <param name="e">事件參數</param>
    private void OnModbusDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        try
        {
            _logger.LogInformation("Modbus 設備狀態變更: {DeviceKey} -> {Status}", 
                                 e.DeviceKey, e.Status);

            // TODO: 在此處理設備狀態變更
            // 1. 記錄設備狀態到資料庫
            // 2. 發布設備狀態到 MQTT
            // 3. 觸發設備離線警報
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理 Modbus 設備狀態變更事件時發生錯誤");
        }
    }

    /// <summary>
    /// 記錄系統狀態
    /// </summary>
    private void LogSystemStatus()
    {
        try
        {
            var deviceStatusDict = _modbusCollectionManager.GetAllDeviceStatus();

            _logger.LogInformation("系統狀態報告 - 管理設備數量: {DeviceCount}", deviceStatusDict.Count);

            foreach (var kvp in deviceStatusDict)
            {
                _logger.LogDebug("設備 {DeviceKey} 狀態: {@Status}", kvp.Key, kvp.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "記錄系統狀態時發生錯誤");
        }
    }

    /// <summary>
    /// 清理資源
    /// </summary>
    private async Task CleanupResourcesAsync()
    {
        try
        {
            _logger.LogInformation("開始清理 SCADA 引擎資源");

            // 取消事件訂閱
            _modbusCollectionManager.DataCollected -= OnModbusDataCollected;
            _modbusCollectionManager.DeviceStatusChanged -= OnModbusDeviceStatusChanged;

            // 停止 Modbus 採集管理器
            await _modbusCollectionManager.StopAsync();

            // 釋放歷史資料儲存服務 (自動儲存剩餘資料)
            _historyDataStorageService.Dispose();

            // 釋放即時資料儲存服務 (自動儲存剩餘資料)
            _realtimeDataStorageService.Dispose();

            _logger.LogInformation("SCADA 引擎資源清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理 SCADA 引擎資源時發生錯誤");
        }
    }

    /// <summary>
    /// 啟動 Python 演算法 FastAPI 服務
    /// </summary>
    private void StartPythonAlgorithmService()
    {
        try
        {
            // 尋找 Algorithms 資料夾（開發時用原始碼路徑，部署時用 output 路徑）
            var szAlgoDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Algorithms");
            if (!Directory.Exists(szAlgoDir))
                szAlgoDir = Path.Combine(Directory.GetCurrentDirectory(), "Algorithms");
            if (!Directory.Exists(szAlgoDir))
            {
                _logger.LogWarning("找不到 Algorithms 資料夾，Python 演算法服務未啟動");
                return;
            }

            var szMainPy = Path.Combine(szAlgoDir, "main.py");
            if (!File.Exists(szMainPy))
            {
                _logger.LogWarning("找不到 Algorithms/main.py，Python 演算法服務未啟動");
                return;
            }

            // ★ 優先使用隨附的可攜式 Python（離線工廠環境不需安裝 Python）
            var szPythonExe = ResolvePythonExe();
            _logger.LogInformation("使用 Python: {PythonPath}", szPythonExe);

            _pythonProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = szPythonExe,
                    Arguments = "-m uvicorn main:app --host 127.0.0.1 --port 8100",
                    WorkingDirectory = szAlgoDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _pythonProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogInformation("[Python] {Output}", e.Data);
            };
            _pythonProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[Python] {Output}", e.Data);
            };
            _pythonProcess.Exited += (s, e) =>
            {
                _logger.LogWarning("Python 演算法服務已意外結束 (ExitCode={ExitCode})", _pythonProcess?.ExitCode);
            };

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            _logger.LogInformation("Python 演算法服務已啟動 (PID={Pid}, Port=8100)", _pythonProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "啟動 Python 演算法服務失敗（可能未安裝 Python），LogicFlow 演算法節點將無法使用");
            _pythonProcess = null;
        }
    }

    /// <summary>
    /// 停止 Python 演算法服務
    /// </summary>
    private void StopPythonAlgorithmService()
    {
        if (_pythonProcess == null || _pythonProcess.HasExited) return;
        try
        {
            _pythonProcess.Kill(entireProcessTree: true);
            _pythonProcess.WaitForExit(3000);
            _logger.LogInformation("Python 演算法服務已停止");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 Python 演算法服務時發生錯誤");
        }
        finally
        {
            _pythonProcess.Dispose();
            _pythonProcess = null;
        }
    }

    /// <summary>
    /// 解析 Python 執行檔路徑：PythonRuntime（可攜式）→ 系統 PATH
    /// </summary>
    private string ResolvePythonExe()
    {
        // 1. 隨附的可攜式 Python（部署包內 PythonRuntime/python.exe）
        var szBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        var szBundled = Path.Combine(szBaseDir, "PythonRuntime", "python.exe");
        if (File.Exists(szBundled)) return szBundled;

        // 2. 開發環境：專案根目錄下的 PythonRuntime
        var szDevBundled = Path.Combine(Directory.GetCurrentDirectory(), "PythonRuntime", "python.exe");
        if (File.Exists(szDevBundled)) return szDevBundled;

        // 3. 退回系統 PATH 中的 python
        return "python";
    }

    /// <summary>
    /// 服務停止時的清理工作
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SCADA 引擎工作服務正在停止");

        StopPythonAlgorithmService();

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("SCADA 引擎工作服務已停止");
    }
}
