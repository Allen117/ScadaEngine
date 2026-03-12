using FluentModbus;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 條件控制服務：週期性讀取即時值，滿足條件時自動執行 Modbus 寫入。
/// 規則每 30 秒從 ConditionControlRules 資料表重新載入；
/// 設備配置每 10 分鐘從 JSON 重新載入；
/// 條件每 5 秒評估一次；每條規則觸發後有 30 秒冷卻時間。
/// </summary>
public class ConditionControlService : BackgroundService
{
    private readonly ILogger<ConditionControlService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModbusConfigService _modbusConfigService;

    // 快取
    private List<ConditionControlRuleModel> _rules = new();
    private List<ModbusDeviceConfigModel> _deviceConfigs = new();

    // 上次載入時間
    private DateTime _dtLastRuleReload = DateTime.MinValue;
    private DateTime _dtLastDeviceConfigReload = DateTime.MinValue;

    // 各規則的上次觸發時間 (ruleId → DateTime)
    private readonly Dictionary<int, DateTime> _lastTriggerTime = new();

    private static readonly TimeSpan RULE_RELOAD_INTERVAL         = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DEVICE_CONFIG_RELOAD_INTERVAL = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CHECK_INTERVAL               = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TRIGGER_COOLDOWN             = TimeSpan.FromSeconds(30);

    public ConditionControlService(
        ILogger<ConditionControlService> logger,
        IServiceProvider serviceProvider,
        ModbusConfigService modbusConfigService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _modbusConfigService = modbusConfigService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("條件控制服務啟動，等待其他服務初始化 (10 秒)...");
        await Task.Delay(10000, stoppingToken);

        // 初始載入
        await ReloadDeviceConfigsAsync();
        await ReloadRulesAsync();

        _logger.LogInformation("條件控制服務進入主迴圈");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 定期重新載入設備配置 (每 10 分鐘)
                if (DateTime.Now - _dtLastDeviceConfigReload >= DEVICE_CONFIG_RELOAD_INTERVAL)
                    await ReloadDeviceConfigsAsync();

                // 定期重新載入規則 (每 30 秒)
                if (DateTime.Now - _dtLastRuleReload >= RULE_RELOAD_INTERVAL)
                    await ReloadRulesAsync();

                // 評估條件
                if (_rules.Count > 0)
                    await CheckConditionsAsync();

                await Task.Delay(CHECK_INTERVAL, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "條件控制主迴圈發生錯誤");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("條件控制服務已停止");
    }

    // ─── 載入 ────────────────────────────────────────────────────────────────

    private async Task ReloadDeviceConfigsAsync()
    {
        try
        {
            _deviceConfigs = await _modbusConfigService.LoadAllDeviceConfigsAsync();
            _dtLastDeviceConfigReload = DateTime.Now;
            _logger.LogDebug("設備配置已重新載入: {Count} 台", _deviceConfigs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新載入設備配置時發生錯誤");
        }
    }

    private async Task ReloadRulesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();
            var all = await repo.GetAllConditionControlRulesAsync();
            _rules = all.Where(r => r.isEnabled).ToList();
            _dtLastRuleReload = DateTime.Now;
            _logger.LogInformation("條件控制規則已載入: {Count} 筆啟用規則", _rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新載入條件控制規則時發生錯誤");
        }
    }

    // ─── 條件評估 ─────────────────────────────────────────────────────────────

    private async Task CheckConditionsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDataRepository>();

            // 取得所有最新資料，建立 SID → 資料的字典以供快速查找
            var latestList = await repo.GetLatestDataAsync(nLimit: 10000);
            var latestDict = latestList.ToDictionary(d => d.szSID, d => d);

            foreach (var rule in _rules)
            {
                try
                {
                    await EvaluateRuleAsync(rule, latestDict);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "評估規則 Id={RuleId} 時發生錯誤", rule.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "條件檢查時發生錯誤");
        }
    }

    private async Task EvaluateRuleAsync(
        ConditionControlRuleModel rule,
        Dictionary<string, LatestDataModel> latestDict)
    {
        // 條件點位是否有資料
        if (!latestDict.TryGetValue(rule.szConditionPointSID, out var condData))
        {
            _logger.LogDebug("條件點位 {SID} 尚無資料，跳過規則 Id={RuleId}",
                rule.szConditionPointSID, rule.Id);
            return;
        }

        // 資料品質必須為 Good
        if (condData.nQuality != 1)
        {
            _logger.LogDebug("條件點位 {SID} 品質為 Bad，跳過規則 Id={RuleId}",
                rule.szConditionPointSID, rule.Id);
            return;
        }

        // 評估條件運算子
        var fCurrent = (double)condData.fValue;
        var isMet = rule.nOperator switch
        {
            0 => fCurrent > rule.dConditionValue,
            1 => fCurrent < rule.dConditionValue,
            2 => fCurrent >= rule.dConditionValue,
            3 => fCurrent <= rule.dConditionValue,
            4 => Math.Abs(fCurrent - rule.dConditionValue) < 1e-9,
            5 => Math.Abs(fCurrent - rule.dConditionValue) >= 1e-9,
            _ => false
        };

        if (!isMet)
        {
            _logger.LogDebug("規則 Id={RuleId} 條件不成立: {Current} {Op} {Threshold}",
                rule.Id, fCurrent, rule.OperatorSymbol, rule.dConditionValue);
            return;
        }

        // 冷卻時間檢查，避免短時間內重複觸發
        if (_lastTriggerTime.TryGetValue(rule.Id, out var dtLast))
        {
            var remaining = TRIGGER_COOLDOWN - (DateTime.Now - dtLast);
            if (remaining > TimeSpan.Zero)
            {
                _logger.LogDebug("規則 Id={RuleId} 冷卻中 (剩餘 {Sec:F0} 秒)", rule.Id, remaining.TotalSeconds);
                return;
            }
        }

        _logger.LogInformation(
            "規則 Id={RuleId} 觸發: {CondSID}({Current}) {Op} {Threshold} → 寫入 {CtrlSID} = {CtrlValue}",
            rule.Id,
            rule.szConditionPointSID, fCurrent, rule.OperatorSymbol, rule.dConditionValue,
            rule.szControlPointSID, rule.dControlValue);

        var isSuccess = await ExecuteControlWriteAsync(rule);
        if (isSuccess)
            _lastTriggerTime[rule.Id] = DateTime.Now;
    }

    // ─── Modbus 寫入 ──────────────────────────────────────────────────────────

    private async Task<bool> ExecuteControlWriteAsync(ConditionControlRuleModel rule)
    {
        try
        {
            // 解析控制點位 SID → DatabaseId, ModbusId, TagIndex
            // SID 格式: "{DatabaseId*65536 + ModbusId*256 + 1}-S{N}"
            var parts = rule.szControlPointSID.Split('-');
            if (parts.Length != 2
                || !parts[1].StartsWith('S')
                || !int.TryParse(parts[0], out var nXXX)
                || !int.TryParse(parts[1][1..], out var nN))
            {
                _logger.LogError("控制點位 SID 格式不合法: {SID}", rule.szControlPointSID);
                return false;
            }

            var nTemp      = nXXX - 1;
            var nDatabaseId = nTemp / 65536;
            var nModbusId   = (nTemp % 65536) / 256;
            var nTagIndex   = nN - 1;

            // 找對應的設備配置
            var deviceConfig = _deviceConfigs.FirstOrDefault(c => c.nDatabaseId == nDatabaseId);
            if (deviceConfig == null)
            {
                _logger.LogError("找不到 DatabaseId={Id} 的設備配置，控制點位={SID}",
                    nDatabaseId, rule.szControlPointSID);
                return false;
            }

            if (nTagIndex < 0 || nTagIndex >= deviceConfig.tagList.Count)
            {
                _logger.LogError("TagIndex={Idx} 超出範圍 (0~{Max})，控制點位={SID}",
                    nTagIndex, deviceConfig.tagList.Count - 1, rule.szControlPointSID);
                return false;
            }

            var tag = deviceConfig.tagList[nTagIndex];

            await ExecuteModbusWriteAsync(deviceConfig, tag, nModbusId, rule.dControlValue);

            _logger.LogInformation("規則 Id={RuleId} Modbus 寫入成功: 點位={TagName} 值={Value}",
                rule.Id, tag.szName, rule.dControlValue);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "執行控制寫入失敗: 規則 Id={RuleId}", rule.Id);
            return false;
        }
    }

    private async Task ExecuteModbusWriteAsync(
        ModbusDeviceConfigModel deviceConfig, ModbusTagModel tag, int nModbusId, double dValue)
    {
        using var client = new ModbusTcpClient();
        try
        {
            if (!tag.ParseAddress())
            {
                _logger.LogError("無法解析 Modbus 地址: {Address}", tag.szAddress);
                return;
            }

            // 只允許 Coil (FC01) 和 Holding Register (FC03) 寫入
            if (tag.nFunctionCode != 1 && tag.nFunctionCode != 3)
            {
                _logger.LogError("點位不支援寫入操作 (功能碼={FC}): {Address}",
                    tag.nFunctionCode, tag.szAddress);
                return;
            }

            var endpoint = new System.Net.IPEndPoint(
                System.Net.IPAddress.Parse(deviceConfig.szIP), deviceConfig.nPort);

            client.ConnectTimeout = deviceConfig.nConnectTimeout;
            client.ReadTimeout    = deviceConfig.nConnectTimeout;
            client.WriteTimeout   = deviceConfig.nConnectTimeout;
            client.Connect(endpoint);

            if (tag.nFunctionCode == 1)
                await WriteCoilAsync(client, nModbusId, tag, dValue);
            else
                await WriteHoldingRegisterAsync(client, nModbusId, tag, dValue);
        }
        finally
        {
            try { if (client.IsConnected) client.Disconnect(); } catch { }
        }
    }

    private async Task WriteCoilAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag, double dValue)
    {
        client.WriteSingleCoil((byte)nModbusId, (ushort)tag.nParsedAddress, dValue > 0.5);
        await Task.Delay(50);
    }

    private async Task WriteHoldingRegisterAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag, double dValue)
    {
        if (!float.TryParse(tag.szRatio, out var fRatio))
            fRatio = 1.0f;

        switch (tag.szDataType.ToUpper())
        {
            case "INTEGER":
            {
                var nRaw    = (short)(dValue / fRatio);
                var swapped = (ushort)(((nRaw & 0xFF) << 8) | ((nRaw >> 8) & 0xFF));
                client.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swapped);
                break;
            }
            case "UINTEGER":
            {
                var uRaw    = (ushort)Math.Max(0, dValue / fRatio);
                var swapped = (ushort)((uRaw << 8) | (uRaw >> 8));
                client.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swapped);
                break;
            }
            case "FLOATINGPT":
                await WriteFloatAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: false);
                break;
            case "SWAPPEDFP":
                await WriteFloatAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: true);
                break;
            case "DOUBLE":
                await WriteDoubleAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: false);
                break;
            case "SWAPPEDDOUBLE":
                await WriteDoubleAsync(client, nModbusId, tag, dValue, fRatio, isSwapped: true);
                break;
            default:
                _logger.LogError("不支援的資料型態: {DataType}", tag.szDataType);
                break;
        }

        await Task.Delay(50);
    }

    private async Task WriteFloatAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag,
        double dValue, float fRatio, bool isSwapped)
    {
        var bytes = BitConverter.GetBytes((float)(dValue / fRatio));
        var regs  = new ushort[2];
        if (isSwapped) // CDAB
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
        }
        else // ABCD
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
        }
        client.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, regs);
        await Task.Delay(50);
    }

    private async Task WriteDoubleAsync(
        ModbusTcpClient client, int nModbusId, ModbusTagModel tag,
        double dValue, float fRatio, bool isSwapped)
    {
        var bytes = BitConverter.GetBytes(dValue / fRatio);
        var regs  = new ushort[4];
        if (isSwapped) // GHEFCDAB
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 6));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 4));
            regs[2] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[3] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
        }
        else // ABCDEFGH
        {
            regs[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
            regs[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            regs[2] = SwapBytes(BitConverter.ToUInt16(bytes, 4));
            regs[3] = SwapBytes(BitConverter.ToUInt16(bytes, 6));
        }
        client.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, regs);
        await Task.Delay(50);
    }

    private static ushort SwapBytes(ushort value) => (ushort)((value << 8) | (value >> 8));
}
