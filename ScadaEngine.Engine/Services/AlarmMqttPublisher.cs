using System.Text.Json;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Mqtt;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Engine 端警報事件 MQTT 發布器
/// Topic: SCADA/Alarm/Active/{szSID}/{szType}（Retain=true）
/// 觸發 → 發布完整 JSON；恢復 → 發布空 payload 清除 retained 訊息
/// </summary>
public class AlarmMqttPublisher
{
    private readonly ILogger<AlarmMqttPublisher> _logger;
    private readonly MqttPublishService _mqttPublishService;

    public const string TOPIC_PREFIX = "SCADA/Alarm/Active";

    public AlarmMqttPublisher(
        ILogger<AlarmMqttPublisher> logger,
        MqttPublishService mqttPublishService)
    {
        _logger = logger;
        _mqttPublishService = mqttPublishService;
    }

    /// <summary>
    /// 將 nOperator 轉為 type 字串：2=high、3=low、4=di
    /// </summary>
    public static string OperatorToType(byte? nOperator) => nOperator switch
    {
        2 => "high",
        3 => "low",
        4 => "di",
        _ => "unknown"
    };

    /// <summary>
    /// 發布警報觸發訊息（retained，含完整資訊）
    /// </summary>
    public async Task<bool> PublishAlarmActiveAsync(EventLogModel model, string? szTagName = null)
    {
        try
        {
            var szType = OperatorToType(model.nOperator);
            var szTopic = $"{TOPIC_PREFIX}/{model.szSID}/{szType}";

            var payload = JsonSerializer.Serialize(new
            {
                sid = model.szSID,
                type = szType,
                severity = model.nSeverity,
                message = model.szMessage,
                // 結構化訊息：Web 端依使用者 culture 翻譯顯示。Engine 不知道也不關心使用者語系
                messageKey = model.szMessageKey,
                messageArgs = model.szMessageArgs,
                tagName = szTagName ?? string.Empty,
                triggerValue = model.dTriggerValue,
                thresholdValue = model.dThresholdValue,
                occurredAt = model.dtOccurredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                occurredAtMs = new DateTimeOffset(model.dtOccurredAt).ToUnixTimeMilliseconds(),
                isAcknowledged = model.isAcknowledged,
                acknowledgedBy = model.szAcknowledgedBy
            }, new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false });

            return await _mqttPublishService.PublishRawJsonAsync(szTopic, payload, isRetain: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布警報觸發 MQTT 訊息失敗: SID={SID}", model.szSID);
            return false;
        }
    }

    /// <summary>
    /// 發布警報恢復訊息（空 payload + retained，清除 broker 上的 retained message）
    /// </summary>
    public async Task<bool> PublishAlarmClearedAsync(string szSID, byte? nOperator)
    {
        try
        {
            var szType = OperatorToType(nOperator);
            var szTopic = $"{TOPIC_PREFIX}/{szSID}/{szType}";
            return await _mqttPublishService.PublishRawJsonAsync(szTopic, string.Empty, isRetain: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布警報恢復 MQTT 訊息失敗: SID={SID}", szSID);
            return false;
        }
    }
}
