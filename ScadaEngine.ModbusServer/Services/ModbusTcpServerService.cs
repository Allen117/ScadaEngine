using System.Net;
using System.Net.Sockets;
using FluentModbus;
using ScadaEngine.ModbusServer.Core;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Services;

/// <summary>
/// FluentModbus TCP Server 生命週期 + 推送式 register 更新：
/// 每 RegisterSweepMs 把快取值編碼（word order / NaN 規則）寫進 input register buffer，
/// Modbus 讀取端（FC4）永遠拿到最新已寫值，不做 per-request 查詢。
/// </summary>
public class ModbusTcpServerService : BackgroundService
{
    private readonly ILogger<ModbusTcpServerService> _logger;
    private readonly ModbusServerSettingModel _setting;
    private readonly PointCatalogService _catalog;
    private readonly RealtimeCacheService _cache;

    private ModbusTcpServer? _server;

    public ModbusTcpServerService(ILogger<ModbusTcpServerService> logger, ModbusServerSettingModel setting,
        PointCatalogService catalog, RealtimeCacheService cache)
    {
        _logger = logger;
        _setting = setting;
        _catalog = catalog;
        _cache = cache;
    }

    public bool IsRunning => _server != null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var order = FloatRegisterEncoder.ParseWordOrder(_setting.szWordOrder);
        _logger.LogInformation("Modbus TCP Server 啟動中：{Address}:{Port}，WordOrder={Order}，BadQuality={Bad}",
            _setting.szModbusListenAddress, _setting.nModbusListenPort, order, _setting.szBadQualityMode);

        try
        {
            while (!stoppingToken.IsCancellationRequested && !TryStartServer())
            {
                // 埠被占用等啟動失敗：30 秒後重試（安裝腳本已先檢查，此為 runtime 保險）
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            var nSweepMs = Math.Max(200, _setting.nRegisterSweepMs);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    SweepRegisters(order);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "register 掃寫失敗");
                }
                await Task.Delay(nSweepMs, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        finally
        {
            try
            {
                _server?.Stop();
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止 Modbus TCP Server 時發生錯誤");
            }
            _server = null;
        }
    }

    private byte UnitId => (byte)_setting.nUnitId;

    private bool TryStartServer()
    {
        try
        {
            var server = new ModbusTcpServer(_logger, isAsynchronous: true);

            // FluentModbus 預設只回應 unit id 0，其他 unit id 的請求會被直接斷線
            //（ScadaEngine.Tests ProbeTests 實測）。ModScan / PLC 慣用 1，
            // 故把設定檔 UnitId 註冊為唯一 unit — 對接方必須用相同 unit id 讀取。
            if (UnitId != 0)
            {
                server.AddUnit(UnitId);
                server.RemoveUnit(0);
            }

            var address = IPAddress.Parse(_setting.szModbusListenAddress);
            server.Start(new IPEndPoint(address, _setting.nModbusListenPort));
            _server = server;
            _logger.LogInformation("Modbus TCP Server 已監聽 {Address}:{Port}（FC4 Input Registers / float32）",
                _setting.szModbusListenAddress, _setting.nModbusListenPort);
            return true;
        }
        catch (SocketException ex)
        {
            _logger.LogError("Modbus TCP 埠 {Port} 無法監聽（{Message}）— 可能被其他程式占用，30 秒後重試。" +
                "可改 Setting/ModbusServerSetting.json 的 ModbusListenPort 後重啟服務",
                _setting.nModbusListenPort, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modbus TCP Server 啟動失敗，30 秒後重試");
            return false;
        }
    }

    /// <summary>把快取值全量掃寫進 input register buffer（在 server.Lock 內，與請求處理互斥）</summary>
    private void SweepRegisters(FloatWordOrder order)
    {
        var server = _server;
        if (server == null) return;

        var entries = _catalog.GetSnapshot();
        if (entries.Length == 0) return;

        var activeSids = _catalog.GetActiveSids();
        // 尚未成功掃描點位表（DB 未就緒）時無法判斷存活，全部視為存活以免誤標停用
        var isCatalogKnown = _catalog.HasScannedOk;
        var isHoldLast = _setting.IsHoldLast;
        var dtNow = DateTime.Now;

        lock (server.Lock)
        {
            var buffer = server.GetInputRegisterBuffer(UnitId);
            foreach (var entry in entries)
            {
                // 每點 2 registers，位址空間 0..65535
                if (entry.nAddress < 0 || entry.nAddress + 1 > ushort.MaxValue) continue;

                _cache.TryGetValue(entry.szSID, out var item);
                var isActive = !isCatalogKnown || activeSids.Contains(entry.szSID);
                var fValue = ServedValueRules.Compute(item, isActive, isHoldLast, _setting.nStaleSeconds, dtNow);
                FloatRegisterEncoder.WriteToBuffer(buffer, entry.nAddress, fValue, order);
            }
        }
    }
}
