using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScadaEngine.Web.Features.ScadaPage.Controllers;

/// <summary>
/// 控制按鈕指令接收 API
/// </summary>
[Authorize]
[ApiController]
public class ControlController : ControllerBase
{
    private readonly ILogger<ControlController> _logger;

    public ControlController(ILogger<ControlController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 接收控制指令（CID + 數值）
    /// </summary>
    [HttpPost("/api/control/write")]
    public IActionResult Write([FromBody] ControlWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.cid))
            return BadRequest(new { success = false, error = "CID 不可為空" });

        _logger.LogInformation("收到控制指令 CID={Cid} Value={Value}", dto.szCid, dto.nValue);

        // TODO: 透過 MQTT 或其他機制將寫入指令傳送至 Engine
        // 目前僅記錄日誌，供後續整合

        return Ok(new { success = true, cid = dto.szCid, value = dto.nValue });
    }
}

public class ControlWriteDto
{
    public string cid   { get; set; } = string.Empty;
    public double value { get; set; } = 1;

    // 別名，方便 log
    public string szCid  => cid;
    public double nValue => value;
}
