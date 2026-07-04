using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTnet;
using MQTTnet.Client;
using ScadaEngine.Engine.Communication.Mqtt;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.License.Controllers;

/// <summary>
/// 授權狀態 API
///   GET  /api/license/status  — 回傳最新快取狀態
///   POST /api/license/verify  — 發布 MQTT 指令，要求 Engine 立即重新驗證
/// </summary>
[Authorize]
[ApiController]
[Route("api/license")]
public class LicenseApiController : ControllerBase
{
    private readonly ILogger<LicenseApiController> _logger;
    private readonly LicenseStatusCache _licenseStatusCache;
    private readonly MqttConfigService _mqttConfigService;

    public LicenseApiController(
        ILogger<LicenseApiController> logger,
        LicenseStatusCache licenseStatusCache,
        MqttConfigService mqttConfigService)
    {
        _logger = logger;
        _licenseStatusCache = licenseStatusCache;
        _mqttConfigService = mqttConfigService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            valid = _licenseStatusCache.IsValid,
            checkedAt = _licenseStatusCache.CheckedAt,
            reason = _licenseStatusCache.Reason
        });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> TriggerVerify()
    {
        try
        {
            var mqttSetting = await _mqttConfigService.LoadConfigAsync();
            dynamic config = mqttSetting.MqttConfig;

            using var client = new MqttFactory().CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer((string)config.szBrokerIp, (int)config.nPort)
                .WithClientId($"ScadaWeb_LicenseTrigger_{Environment.ProcessId}_{Guid.NewGuid():N}")
                .WithCleanSession(true)
                .Build();

            await client.ConnectAsync(options);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic("SCADA/Sys/License/Verify")
                .WithPayload("{}")
                .WithRetainFlag(false)
                .Build();

            await client.PublishAsync(message);
            await client.DisconnectAsync();

            _logger.LogInformation("授權手動驗證指令已發布");
            return Ok(new { queued = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "發布授權驗證 MQTT 指令失敗");
            return StatusCode(500, new { error = "發布失敗，請確認 MQTT Broker 是否在線" });
        }
    }
}
