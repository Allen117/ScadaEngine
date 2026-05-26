using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.ScadaPage.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

/// <summary>
/// 控制按鈕指令接收 API
/// </summary>
[Authorize]
[ApiController]
public class ControlController : ControllerBase
{
    private readonly ILogger<ControlController> _logger;
    private readonly MqttRealtimeSubscriberService _mqttService;
    private readonly IDataRepository _repository;
    private readonly ControlEventLogger _controlEventLogger;

    public ControlController(
        ILogger<ControlController> logger,
        MqttRealtimeSubscriberService mqttService,
        IDataRepository repository,
        ControlEventLogger controlEventLogger)
    {
        _logger             = logger;
        _mqttService        = mqttService;
        _repository         = repository;
        _controlEventLogger = controlEventLogger;
    }

    /// <summary>
    /// 接收控制指令（CID + 數值），透過 MQTT 發送至 Engine 執行 Modbus 寫入
    /// </summary>
    [HttpPost("/api/control/write")]
    public async Task<IActionResult> Write([FromBody] ControlWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.cid))
            return BadRequest(new { success = false, error = "CID 不可為空" });

        var isAutoMode      = string.Equals(dto.mode, "auto",      StringComparison.OrdinalIgnoreCase);
        var isLogicFlowMode = string.Equals(dto.mode, "logicflow", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("收到控制指令 CID={Cid} Value={Value} Mode={Mode}", dto.szCid, dto.nValue, dto.mode);

        if (isAutoMode)
        {
            // 自動模式：不發送 MQTT，僅在資料庫標記為自動控制
            var isSaved = await _repository.SetAutoControlAsync(dto.szCid);
            if (!isSaved)
            {
                _logger.LogWarning("設定自動控制失敗 CID={Cid}", dto.szCid);
                return Ok(new { success = true, cid = dto.szCid, mode = "auto" });
            }

            _mqttService.UpdateManualAutoFlag(dto.szCid, isAuto: true);
            await WriteEventLogAsync(dto);
            return Ok(new { success = true, cid = dto.szCid, mode = "auto" });
        }
        else if (isLogicFlowMode)
        {
            // LogicFlow 自動控制：發送 MQTT 寫入，但不改變手動/自動旗標
            // 此分支由 Engine/LogicFlow 自動觸發，非人類操作，不寫 EventLog
            var isSuccess = await _mqttService.PublishControlCommandAsync(dto.szCid, dto.nValue);
            if (!isSuccess)
                return StatusCode(503, new { success = false, error = "MQTT 未連線，無法發送控制指令" });

            return Ok(new { success = true, cid = dto.szCid, value = dto.nValue });
        }
        else
        {
            // 手動模式：發送 MQTT 寫入 + 儲存手動控制值
            var isSuccess = await _mqttService.PublishControlCommandAsync(dto.szCid, dto.nValue);
            if (!isSuccess)
                return StatusCode(503, new { success = false, error = "MQTT 未連線，無法發送控制指令" });

            var isSaved = await _repository.SaveManualControlValueAsync(dto.szCid, dto.nValue);
            if (!isSaved)
                _logger.LogWarning("手動控制值儲存失敗 CID={Cid}", dto.szCid);

            _mqttService.UpdateManualAutoFlag(dto.szCid, isAuto: false);
            await WriteEventLogAsync(dto);
            return Ok(new { success = true, cid = dto.szCid, value = dto.nValue });
        }
    }

    /// <summary>
    /// 寫入控制動作 EventLog（actionType 為空或無法解析時忽略 — 相容舊前端）
    /// </summary>
    private async Task WriteEventLogAsync(ControlWriteDto dto)
    {
        var actionType = ControlActionTypeExtensions.ParseActionType(dto.szActionType);
        var szUsername = User.Identity?.Name ?? "anonymous";
        await _controlEventLogger.LogAsync(actionType, dto.szCid, dto.szDisplayName, dto.nValue, szUsername);
    }

    /// <summary>
    /// 取得所有手動控制值與自動模式狀態
    /// </summary>
    [HttpGet("/api/control/manual-values")]
    public async Task<IActionResult> GetManualValues()
    {
        var dict = await _repository.LoadManualControlValuesAsync();
        // 轉為前端友善格式：{ "SID": { value: 50, isAuto: false } }
        var result = dict.ToDictionary(
            kv => kv.Key,
            kv => new { value = kv.Value.dValue, isAuto = kv.Value.isAuto });
        return Ok(result);
    }
}
