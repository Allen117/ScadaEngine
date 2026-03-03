using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using FluentModbus;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;
using ScadaEngine.Engine.Communication.Modbus.Services;
using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Engine.Communication.Mqtt;

/// <summary>
/// MQTT 控制指令訂閱服務 - 獨立執行序
/// </summary>
public class MqttControlSubscribeService : BackgroundService, IDisposable
{
    private readonly ILogger<MqttControlSubscribeService> _logger;
    private readonly MqttConfigService _mqttConfigService;
    private readonly IServiceProvider _serviceProvider;
    private MqttConfigModel? _mqttConfig;
    private IMqttClient? _mqttClient;
    private bool _isConnected = false;
    private bool _disposed = false;

    // 控制指令清單 (線程安全)
    private readonly ConcurrentBag<MqttControlCommandModel> _controlCommandList = new();
    
    // 控制主題前綴
    private const string CONTROL_TOPIC_PREFIX = "SCADA/Control/";
    
    // 最大保留指令數量
    private const int MAX_COMMAND_HISTORY = 1000;
    
    // 清理閾值（當達到這個數量時觸發清理）
    private const int CLEANUP_THRESHOLD = 1200;
    
    // 上次清理時間
    private DateTime _lastCleanupTime = DateTime.Now;
    
    // 清理間隔（每 5 分鐘檢查一次）
    private static readonly TimeSpan CLEANUP_INTERVAL = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 建構函式
    /// </summary>
    /// <param name="logger">日誌記錄器</param>
    /// <param name="mqttConfigService">MQTT 配置服務</param>
    public MqttControlSubscribeService(
        ILogger<MqttControlSubscribeService> logger, 
        MqttConfigService mqttConfigService,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttConfigService = mqttConfigService ?? throw new ArgumentNullException(nameof(mqttConfigService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 背景服務執行方法
    /// </summary>
    /// <param name="stoppingToken">停止令牌</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT 控制訂閱服務啟動");

        try
        {
            // 載入 MQTT 配置
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            _mqttConfig = mqttSetting.MqttConfig;
            
            _logger.LogInformation("MQTT 配置載入成功: {BrokerIp}:{Port}", 
                                 _mqttConfig.szBrokerIp, _mqttConfig.nPort);

            // 等待主要 MQTT 服務初始化完成後再建立控制訂閱連線
            _logger.LogInformation("等待 5 秒讓主要 MQTT 服務先完成初始化...");
            await Task.Delay(5000, stoppingToken);

            // 初始化 MQTT 連線
            await InitializeMqttAsync();

            // 持續監控連線狀態
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 檢查連線狀態
                    if (_mqttClient?.IsConnected != true)
                    {
                        _logger.LogWarning("MQTT 連線中斷，嘗試重新連線");
                        await ReconnectMqttAsync();
                    }

                    // 在定期監控中也檢查清理（備用機制）
                    await CheckAndCleanupCommandListAsync();

                    // 每 30 秒記錄一次狀態
                    _logger.LogInformation("MQTT 控制訂閱服務運行中，已收到 {Count} 個控制指令，連線狀態: {IsConnected}", 
                                         _controlCommandList.Count, 
                                         _mqttClient?.IsConnected == true ? "已連線" : "未連線");

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT 控制訂閱服務監控迴圈發生錯誤");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 控制訂閱服務執行時發生嚴重錯誤");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>
    /// 初始化 MQTT 連線
    /// </summary>
    private async Task InitializeMqttAsync()
    {
        try
        {
            if (_mqttConfig == null)
            {
                _logger.LogError("MQTT 配置尚未載入，無法初始化連線");
                return;
            }

            _mqttClient = new MqttFactory().CreateMqttClient();

            // 設定連線選項，使用不同的 ClientId 避免衝突
            var clientId = $"{_mqttConfig.szClientId}_ControlSub_{Environment.ProcessId}";
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttConfig.szBrokerIp, _mqttConfig.nPort)
                .WithClientId(clientId)
                .WithCleanSession(true)  // 使用 CleanSession 避免與主連線衝突
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(45))  // 稍微不同的 KeepAlive 時間
                .WithTimeout(TimeSpan.FromSeconds(15))
                .Build();

            _logger.LogInformation("嘗試建立控制訂閱連線，ClientId: {ClientId}", clientId);

            // 設定事件處理器
            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            _logger.LogInformation("[DEBUG] 事件處理器已設定");

            // 執行連線
            var result = await _mqttClient.ConnectAsync(options);
            
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("MQTT 控制訂閱連線成功，ClientId: {ClientId}", clientId);
                
                // 縮短等待時間，因為已經延遲過了
                await Task.Delay(500);
                
                // 主動執行訂閱邏輯
                await SubscribeToControlTopicAsync();
            }
            else
            {
                _logger.LogError("MQTT 控制訂閱連線失敗: {ResultCode}", result.ResultCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 MQTT 控制訂閱時發生錯誤");
        }
    }

    /// <summary>
    /// 重新連線 MQTT
    /// </summary>
    private async Task ReconnectMqttAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                await _mqttClient.DisconnectAsync();
                await Task.Delay(2000); // 等待 2 秒後重連
                await InitializeMqttAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新連線 MQTT 時發生錯誤");
        }
    }

    /// <summary>
    /// 訂閱控制主題
    /// </summary>
    private async Task SubscribeToControlTopicAsync()
    {
        try
        {
            if (_mqttClient?.IsConnected != true)
            {
                _logger.LogWarning("MQTT 用戶端未連線，無法訂閱主題");
                return;
            }

            // 訂閱控制主題: SCADA/Control/# (所有子主題)
            var szControlTopic = "SCADA/Control/#";
            
            var subscribeResult = await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(szControlTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());

            // 檢查訂閱結果
            if (subscribeResult.Items.Count > 0)
            {
                var resultCode = subscribeResult.Items.First().ResultCode;
                if (resultCode == MqttClientSubscribeResultCode.GrantedQoS0 || 
                    resultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
                    resultCode == MqttClientSubscribeResultCode.GrantedQoS2)
                {
                    _isConnected = true;
                    _logger.LogInformation("成功訂閱控制主題: {Topic}, 結果: {ResultCode}", szControlTopic, resultCode);
                }
                else
                {
                    _logger.LogError("訂閱控制主題失敗: {Topic}, 結果: {ResultCode}", szControlTopic, resultCode);
                }
            }
            else
            {
                _logger.LogError("訂閱控制主題沒有返回結果: {Topic}", szControlTopic);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "訂閱控制主題時發生錯誤");
        }
    }

    /// <summary>
    /// MQTT 連線成功事件處理
    /// </summary>
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        if (_mqttConfig == null) return;
        
        _logger.LogInformation("MQTT 控制訂閱已連線至 Broker: {BrokerIp}:{Port}", 
                             _mqttConfig.szBrokerIp, _mqttConfig.nPort);

        // 備用訂閱機制：如果還沒訂閱成功，在這裡再次嘗試訂閱
        if (!_isConnected)
        {
            _logger.LogInformation("在連線事件中執行備用訂閱");
            await Task.Delay(500); // 短暫延遲
            await SubscribeToControlTopicAsync();
        }
    }

    /// <summary>
    /// MQTT 連線中斷事件處理
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _isConnected = false;
        _logger.LogWarning("MQTT 控制訂閱連線中斷: {Reason}", e.Reason);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 接收 MQTT 訊息事件處理
    /// </summary>
    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        _logger.LogInformation("[DEBUG] 收到 MQTT 訊息，開始處理");
        
        try
        {
            var szTopic = e.ApplicationMessage.Topic;
            var szPayload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            
            _logger.LogInformation("收到控制指令: 主題={Topic}, 內容={Payload}", szTopic, szPayload);

            // 解析主題以提取 CID
            if (!szTopic.StartsWith(CONTROL_TOPIC_PREFIX))
            {
                _logger.LogWarning("收到非控制主題的訊息: {Topic}", szTopic);
                return;
            }

            var szCID = szTopic.Substring(CONTROL_TOPIC_PREFIX.Length);
            if (string.IsNullOrEmpty(szCID))
            {
                _logger.LogError("無法從主題提取 CID: {Topic}", szTopic);
                return;
            }

            // 解析訊息內容
            var messageContent = JsonSerializer.Deserialize<MqttControlMessageModel>(szPayload);
            if (messageContent == null)
            {
                _logger.LogError("無法解析控制訊息 JSON: {Payload}", szPayload);
                return;
            }

            // 建立控制指令物件
            var controlCommand = new MqttControlCommandModel
            {
                szCID = szCID,
                szMid = messageContent.szMid,
                dValue = messageContent.GetValueAsDouble(), // 使用新的轉換方法
                szOriginalValue = messageContent.szValue,   // 保存原始字串值
                szUnit = messageContent.szUnit,             // 保存單位
                nMessageTimestamp = messageContent.nTimestamp, // 保存原始時間戳記
                dtReceived = DateTime.Now,
                szSourceTopic = szTopic
            };

            // 解析 CID 格式 (XXX-SN)
            ParseCIDInfo(controlCommand);

            // 執行 Modbus 控制邏輯
            await ExecuteModbusControlAsync(controlCommand);

            // 將控制指令加入清單，以供後續查詢和統計
            _controlCommandList.Add(controlCommand);

            // 在每次新增指令後檢查是否需要清理
            await CheckAndCleanupCommandListAsync();

            _logger.LogInformation("收到控制指令: CID={CID}, MID={MID}, 值={Value} {Unit}, 時間戳記={Timestamp}, 已收集 {Count} 個指令", 
                     szCID, messageContent.szMid, messageContent.szValue, messageContent.szUnit, 
                     messageContent.GetTimestampAsDateTime(), _controlCommandList.Count);

        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析控制指令 JSON 時發生錯誤: {Topic}", e.ApplicationMessage.Topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理控制指令時發生錯誤: {Topic}", e.ApplicationMessage.Topic);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 解析 CID 資訊 (格式: XXX-SN)
    /// </summary>
    /// <param name="controlCommand">控制指令物件</param>
    private void ParseCIDInfo(MqttControlCommandModel controlCommand)
    {
        try
        {
            var szCID = controlCommand.szCID;
            
            // CID 格式: XXX-SN，其中 XXX = DatabaseId*65536 + ModbusID*256 + 1
            var parts = szCID.Split('-');
            if (parts.Length != 2 || !parts[1].StartsWith("S"))
            {
                controlCommand.szParseError = $"CID 格式錯誤: {szCID}";
                return;
            }

            if (!int.TryParse(parts[0], out var nXXX) || !int.TryParse(parts[1].Substring(1), out var nN))
            {
                controlCommand.szParseError = $"CID 數值解析錯誤: {szCID}";
                return;
            }

            // 反推計算
            var nTemp = nXXX - 1;
            controlCommand.nDatabaseId = nTemp / 65536;
            controlCommand.nModbusId = (nTemp % 65536) / 256;
            controlCommand.nTagIndex = nN - 1; // 點位索引從 0 開始

            controlCommand.isParsed = true;

            _logger.LogDebug("解析 CID 成功: {CID} -> DatabaseId={DatabaseId}, ModbusId={ModbusId}, TagIndex={TagIndex}", 
                           szCID, controlCommand.nDatabaseId, controlCommand.nModbusId, controlCommand.nTagIndex);
        }
        catch (Exception ex)
        {
            controlCommand.szParseError = $"CID 解析異常: {ex.Message}";
            _logger.LogError(ex, "解析 CID 時發生錯誤: {CID}", controlCommand.szCID);
        }
    }

    /// <summary>
    /// 執行 Modbus 控制操作
    /// </summary>
    /// <param name="controlCommand">控制指令物件</param>
    private async Task ExecuteModbusControlAsync(MqttControlCommandModel controlCommand)
    {
        try
        {
            // 檢查 CID 是否解析成功
            if (!controlCommand.isParsed)
            {
                _logger.LogWarning("控制指令 CID 解析失敗，無法執行 Modbus 控制: {Error}", controlCommand.szParseError);
                return;
            }

            // 取得 Modbus 採集管理器
            var modbusManager = _serviceProvider.GetService<ModbusCollectionManager>();
            if (modbusManager == null)
            {
                _logger.LogError("無法取得 ModbusCollectionManager 服務，無法執行 Modbus 控制");
                return;
            }

            // 取得 Modbus 配置服務
            var configService = _serviceProvider.GetService<ModbusConfigService>();
            if (configService == null)
            {
                _logger.LogError("無法取得 ModbusConfigService 服務，無法執行 Modbus 控制");
                return;
            }

            // 根據 DatabaseId 載入對應的 Modbus 設備配置
            var deviceConfigs = await configService.LoadAllDeviceConfigsAsync();
            var targetDeviceConfig = deviceConfigs.FirstOrDefault(config => 
                config.nDatabaseId == controlCommand.nDatabaseId);

            if (targetDeviceConfig == null)
            {
                _logger.LogError("找不到 DatabaseId={DatabaseId} 的 Modbus 設備配置", controlCommand.nDatabaseId);
                return;
            }

            // 檢查 ModbusId 是否存在於設備配置中
            if (!targetDeviceConfig.szModbusId.Split(',').Select(int.Parse).Contains(controlCommand.nModbusId))
            {
                _logger.LogError("設備 DatabaseId={DatabaseId} 中找不到 ModbusId={ModbusId}", 
                               controlCommand.nDatabaseId, controlCommand.nModbusId);
                return;
            }

            // 檢查 TagIndex 是否有效
            if (controlCommand.nTagIndex < 0 || controlCommand.nTagIndex >= targetDeviceConfig.tagList.Count)
            {
                _logger.LogError("TagIndex={TagIndex} 超出有效範圍 (0-{MaxIndex})", 
                               controlCommand.nTagIndex, targetDeviceConfig.tagList.Count - 1);
                return;
            }

            var targetTag = targetDeviceConfig.tagList[controlCommand.nTagIndex];

            // 記錄控制操作資訊
            _logger.LogInformation("準備執行 Modbus 控制: IP={IP}, Port={Port}, ModbusId={ModbusId}, TagName={TagName}, Address={Address}, Value={Value}", 
                                 targetDeviceConfig.szIP, targetDeviceConfig.nPort, controlCommand.nModbusId,
                                 targetTag.szName, targetTag.szAddress, controlCommand.dValue);

            // TODO: 實作實際的 Modbus 寫入操作
            // 這裡需要根據點位類型、地址等資訊執行對應的 Modbus 寫入指令
            await ExecuteModbusWriteOperationAsync(targetDeviceConfig, targetTag, controlCommand);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "執行 Modbus 控制時發生錯誤: CID={CID}", controlCommand.szCID);
        }
    }

    /// <summary>
    /// 執行實際的 Modbus 寫入操作
    /// </summary>
    /// <param name="deviceConfig">設備配置</param>
    /// <param name="tag">點位配置</param>
    /// <param name="controlCommand">控制指令</param>
    private async Task ExecuteModbusWriteOperationAsync(ModbusDeviceConfigModel deviceConfig, ModbusTagModel tag, MqttControlCommandModel controlCommand)
    {
        using var modbusClient = new FluentModbus.ModbusTcpClient();
        
        try
        {
            // 1. 解析地址和功能碼
            if (!tag.ParseAddress())
            {
                _logger.LogError("無法解析 Modbus 地址: {Address}", tag.szAddress);
                return;
            }

            // 2. 檢查是否為可寫入的功能碼
            if (tag.nFunctionCode != 3 && tag.nFunctionCode != 1)
            {
                _logger.LogError("點位不支援寫入操作: 功能碼={FunctionCode}, 地址={Address}", tag.nFunctionCode, tag.szAddress);
                return;
            }

            // 3. 建立 Modbus 連線
            var ipEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(deviceConfig.szIP), deviceConfig.nPort);
            modbusClient.ConnectTimeout = deviceConfig.nConnectTimeout;
            modbusClient.ReadTimeout = deviceConfig.nConnectTimeout;
            modbusClient.WriteTimeout = deviceConfig.nConnectTimeout;
            _logger.LogInformation("建立 Modbus 連線: {IP}:{Port}", deviceConfig.szIP, deviceConfig.nPort);
            modbusClient.Connect(ipEndPoint);

            // 4. 根據資料型態和功能碼執行寫入操作
            if (tag.nFunctionCode == 1) // Coils (Function Code 05 - Write Single Coil)
            {
                await WriteCoilAsync(modbusClient, controlCommand.nModbusId, tag, controlCommand.dValue);
            }
            else if (tag.nFunctionCode == 3) // Holding Registers (Function Code 06/16 - Write Single/Multiple Registers)
            {
                await WriteHoldingRegisterAsync(modbusClient, controlCommand.nModbusId, tag, controlCommand.dValue);
            }

            _logger.LogInformation("Modbus 寫入操作完成: CID={CID}, Value={Value}", controlCommand.szCID, controlCommand.dValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modbus 寫入操作失敗: TagName={TagName}, Value={Value}", tag.szName, controlCommand.dValue);
            throw;
        }
        finally
        {
            try
            {
                if (modbusClient.IsConnected)
                {
                    modbusClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "關閉 Modbus 連線時發生錯誤");
            }
        }
    }

    /// <summary>
    /// 寫入 Coil (數位輸出)
    /// </summary>
    private async Task WriteCoilAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue)
    {
        try
        {
            // 轉換數值為布林值
            var isCoilOn = dValue > 0.5;
            
            _logger.LogInformation("寫入 Coil: ModbusId={ModbusId}, 地址={Address}, 值={Value} ({BoolValue})", 
                                 nModbusId, tag.nParsedAddress, dValue, isCoilOn);

            // 寫入單一 Coil (Function Code 05)
            modbusClient.WriteSingleCoil((byte)nModbusId, (ushort)tag.nParsedAddress, isCoilOn);
            
            await Task.Delay(50); // 短暫延遲確保寫入完成
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入 Coil 失敗: ModbusId={ModbusId}, 地址={Address}", nModbusId, tag.nParsedAddress);
            throw;
        }
    }

    /// <summary>
    /// 寫入 Holding Register (類比輸出)
    /// </summary>
    private async Task WriteHoldingRegisterAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue)
    {
        try
        {
            // 解析 Ratio
            if (!float.TryParse(tag.szRatio, out var fRatio))
            {
                fRatio = 1.0f;
            }

            // 根據資料型態轉換數值
            switch (tag.szDataType.ToUpper())
            {
                case "INTEGER":
                    await WriteIntegerRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio);
                    break;
                    
                case "UINTEGER":
                    await WriteUIntegerRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio);
                    break;
                    
                case "FLOATINGPT":
                    await WriteFloatRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio, false); // CDAB順序
                    break;
                    
                case "SWAPPEDFP":
                    await WriteFloatRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio, true); // ABCD順序
                    break;
                    
                case "DOUBLE":
                    await WriteDoubleRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio, false); // GHEFCDAB順序
                    break;
                    
                case "SWAPPEDDOUBLE":
                    await WriteDoubleRegisterAsync(modbusClient, nModbusId, tag, dValue, fRatio, true); // ABCDEFGH順序
                    break;
                    
                default:
                    _logger.LogError("不支援的資料型態: {DataType}", tag.szDataType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入 Holding Register 失敗: ModbusId={ModbusId}, 地址={Address}", nModbusId, tag.nParsedAddress);
            throw;
        }
    }

    /// <summary>
    /// 寫入 16 位元整數
    /// </summary>
    private async Task WriteIntegerRegisterAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue, float fRatio)
    {
        // 反向計算：原始值 = 實際值 / Ratio
        var nRawValue = (short)(dValue / fRatio);
        ushort swappedValue = (ushort)(((nRawValue & 0xFF) << 8) | ((nRawValue >> 8) & 0xFF));
        _logger.LogInformation("寫入 16-bit Integer: ModbusId={ModbusId}, 地址={Address}, 實際值={ActualValue}, 原始值={RawValue}, Ratio={Ratio}", 
                             nModbusId, tag.nParsedAddress, dValue, nRawValue, fRatio);

        // 寫入單一暫存器 (Function Code 06)
        modbusClient.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swappedValue);
        
        await Task.Delay(50);
    }

    /// <summary>
    /// 寫入 16 位元無符號整數
    /// </summary>
    private async Task WriteUIntegerRegisterAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue, float fRatio)
    {
        // 1. 反向計算原始值
        var nRawValue = (ushort)Math.Max(0, dValue / fRatio);

        // 2. 手動交換位元組 (解決 5 變 1280 的問題)
        ushort swappedValue = (ushort)((nRawValue << 8) | (nRawValue >> 8));

        _logger.LogInformation("寫入 16-bit UInteger: 地址={Address}, 原始值={RawValue}, 發送值={Swapped:X4}H", 
                            tag.nParsedAddress, nRawValue, swappedValue);

        modbusClient.WriteSingleRegister((byte)nModbusId, (ushort)tag.nParsedAddress, swappedValue);
        await Task.Delay(50);
    }

    /// <summary>
    /// 寫入 32 位元浮點數
    /// </summary>
   private async Task WriteFloatRegisterAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue, float fRatio, bool isSwapped)
    {
        float fRawValue = (float)(dValue / fRatio);
        byte[] bytes = System.BitConverter.GetBytes(fRawValue); 

        ushort[] registers = new ushort[2];

        // 取得 Word 後立即執行手動位元組交換
        if (isSwapped) // CDAB (例如 ModScan 的 Swapped FP)
        {
            registers[0] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
            registers[1] = SwapBytes(BitConverter.ToUInt16(bytes, 0));
        }
        else // ABCD (例如 ModScan 的 Floating Pt)
        {
            registers[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0)); 
            registers[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2));
        }

        modbusClient.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, registers);
        await Task.Delay(50);
    }

    private async Task WriteDoubleRegisterAsync(FluentModbus.ModbusTcpClient modbusClient, int nModbusId, ModbusTagModel tag, double dValue, float fRatio, bool isSwapped)
    {
        double dRawValue = dValue / fRatio;
        byte[] bytes = System.BitConverter.GetBytes(dRawValue);
        ushort[] registers = new ushort[4];

        if (isSwapped) 
        {
            registers[0] = SwapBytes(BitConverter.ToUInt16(bytes, 6)); 
            registers[1] = SwapBytes(BitConverter.ToUInt16(bytes, 4)); 
            registers[2] = SwapBytes(BitConverter.ToUInt16(bytes, 2)); 
            registers[3] = SwapBytes(BitConverter.ToUInt16(bytes, 0)); 
        }
        else 
        {
            registers[0] = SwapBytes(BitConverter.ToUInt16(bytes, 0)); 
            registers[1] = SwapBytes(BitConverter.ToUInt16(bytes, 2)); 
            registers[2] = SwapBytes(BitConverter.ToUInt16(bytes, 4)); 
            registers[3] = SwapBytes(BitConverter.ToUInt16(bytes, 6)); 
        }

        modbusClient.WriteMultipleRegisters((byte)nModbusId, (ushort)tag.nParsedAddress, registers);
        await Task.Delay(50);
    }

    // 輔助方法：執行 16-bit 位元組交換
    private ushort SwapBytes(ushort value)
    {
        return (ushort)((value << 8) | (value >> 8));
    }

    /// <summary>
    /// 取得所有收集到的控制指令清單
    /// </summary>
    /// <returns>控制指令清單</returns>
    public List<MqttControlCommandModel> GetControlCommands()
    {
        return _controlCommandList.ToList();
    }

    /// <summary>
    /// 清除控制指令清單
    /// </summary>
    public void ClearControlCommands()
    {
        _controlCommandList.Clear();
        _logger.LogInformation("已清除所有控制指令");
    }

    /// <summary>
    /// 取得指定時間範圍內的控制指令
    /// </summary>
    /// <param name="startTime">開始時間</param>
    /// <param name="endTime">結束時間</param>
    /// <returns>符合條件的控制指令清單</returns>
    public List<MqttControlCommandModel> GetControlCommandsByTimeRange(DateTime startTime, DateTime endTime)
    {
        return _controlCommandList
            .Where(cmd => cmd.dtReceived >= startTime && cmd.dtReceived <= endTime)
            .ToList();
    }

    /// <summary>
    /// 檢查連線狀態
    /// </summary>
    public bool IsConnected => _isConnected && _mqttClient?.IsConnected == true;

    /// <summary>
    /// 檢查並清理控制指令清單，保留最近的 1000 筆記錄
    /// </summary>
    private async Task CheckAndCleanupCommandListAsync()
    {
        try
        {
            var nCurrentCount = _controlCommandList.Count;
            var dtNow = DateTime.Now;

            // 條件1：數量超過清理閾值
            var isCountThresholdReached = nCurrentCount >= CLEANUP_THRESHOLD;

            // 條件2：距離上次清理超過指定間隔
            var isTimeIntervalReached = dtNow - _lastCleanupTime >= CLEANUP_INTERVAL;

            // 滿足任一條件即執行清理
            if (isCountThresholdReached || isTimeIntervalReached)
            {
                await PerformCommandListCleanupAsync();
                _lastCleanupTime = dtNow;

                _logger.LogInformation("控制指令清單清理完成: 觸發條件={TriggerReason}, 清理前={BeforeCount}, 清理後={AfterCount}", 
                                     isCountThresholdReached ? "數量達閾值" : "時間間隔", 
                                     nCurrentCount, 
                                     _controlCommandList.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "檢查控制指令清單清理時發生錯誤");
        }
    }

    /// <summary>
    /// 執行控制指令清單的實際清理操作
    /// </summary>
    private async Task PerformCommandListCleanupAsync()
    {
        try
        {
            var nCurrentCount = _controlCommandList.Count;

            // 如果數量未超過最大保留數，不需要清理
            if (nCurrentCount <= MAX_COMMAND_HISTORY)
            {
                return;
            }

            // 將 ConcurrentBag 轉換為有序列表（按接收時間排序）
            var orderedCommands = _controlCommandList
                .OrderByDescending(cmd => cmd.dtReceived)  // 最新的在前
                .Take(MAX_COMMAND_HISTORY)                 // 取最新的 1000 筆
                .ToList();

            // 清空原始清單
            while (_controlCommandList.TryTake(out _)) { }

            // 將保留的指令重新加入
            foreach (var command in orderedCommands)
            {
                _controlCommandList.Add(command);
            }

            await Task.CompletedTask;

            _logger.LogDebug("控制指令清單清理詳情: 原始數量={OriginalCount}, 保留數量={RetainedCount}", 
                           nCurrentCount, orderedCommands.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "執行控制指令清單清理時發生錯誤");
        }
    }

    /// <summary>
    /// 手動觸發控制指令清單清理
    /// </summary>
    public async Task ManualCleanupCommandListAsync()
    {
        _logger.LogInformation("手動觸發控制指令清單清理");
        await PerformCommandListCleanupAsync();
        _lastCleanupTime = DateTime.Now;
    }

    /// <summary>
    /// 取得控制指令清單的統計資訊
    /// </summary>
    /// <returns>包含統計資訊的物件</returns>
    public object GetCommandListStatistics()
    {
        try
        {
            var commands = _controlCommandList.ToList();
            
            if (commands.Count == 0)
            {
                return new
                {
                    nTotalCount = 0,
                    dtOldestCommand = (DateTime?)null,
                    dtNewestCommand = (DateTime?)null,
                    szLastCleanupTime = _lastCleanupTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    nMaxCapacity = MAX_COMMAND_HISTORY
                };
            }

            var dtOldest = commands.Min(c => c.dtReceived);
            var dtNewest = commands.Max(c => c.dtReceived);

            return new
            {
                nTotalCount = commands.Count,
                dtOldestCommand = dtOldest,
                dtNewestCommand = dtNewest,
                szLastCleanupTime = _lastCleanupTime.ToString("yyyy-MM-dd HH:mm:ss"),
                nMaxCapacity = MAX_COMMAND_HISTORY
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得控制指令清單統計資訊時發生錯誤");
            return new { nError = "統計資訊取得失敗", szErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// 取得收集到的控制指令數量
    /// </summary>
    public int ControlCommandCount => _controlCommandList.Count;

    /// <summary>
    /// 測試方法：發送測試訊息到控制主題 (用於除錯)
    /// </summary>
    public async Task SendTestControlMessageAsync(string szCID = "257-S1", double dValue = 99.9)
    {
        if (_mqttClient?.IsConnected != true)
        {
            _logger.LogWarning("MQTT 用戶端未連線，無法發送測試訊息");
            return;
        }

        try
        {
            var testPayload = JsonSerializer.Serialize(new
            {
                mid = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(),
                value = dValue
            });

            var testTopic = $"SCADA/Control/{szCID}";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(testTopic)
                .WithPayload(testPayload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message);
            
            _logger.LogInformation("[TEST] 已發送測試控制訊息到主題: {Topic}, 內容: {Payload}", testTopic, testPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發送測試控制訊息時發生錯誤");
        }
    }

    /// <summary>
    /// 清理資源
    /// </summary>
    private async Task CleanupAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                await _mqttClient.DisconnectAsync();
                _mqttClient.Dispose();
            }
            
            _logger.LogInformation("MQTT 控制訂閱服務已停止並清理資源");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理 MQTT 控制訂閱資源時發生錯誤");
        }
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public new void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();
            CleanupAsync().Wait(5000);
            _disposed = true;
        }
    }
}