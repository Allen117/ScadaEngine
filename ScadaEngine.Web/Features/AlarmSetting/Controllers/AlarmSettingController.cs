using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.AlarmSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.AlarmSetting.Controllers;

[Authorize]
[Route("[controller]")]
public class AlarmSettingController : Controller
{
    private readonly AlarmRuleService _alarmRuleService;
    private readonly IDataRepository _dataRepository;
    private readonly ILogger<AlarmSettingController> _logger;

    public AlarmSettingController(
        AlarmRuleService alarmRuleService,
        IDataRepository dataRepository,
        ILogger<AlarmSettingController> logger)
    {
        _alarmRuleService = alarmRuleService;
        _dataRepository = dataRepository;
        _logger = logger;
    }

    /// <summary>管理頁面</summary>
    [HttpGet("/AlarmSetting")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "警報設定";

        var coordinatorList = (await _dataRepository.GetAllCoordinatorsAsync()).ToList();
        var pointList = (await _dataRepository.GetAllModbusPointsAsync())
            .OrderBy(p =>
            {
                var idx = p.szSID.IndexOf("-S", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 && int.TryParse(p.szSID[(idx + 2)..], out var n) ? n : int.MaxValue;
            })
            .ToList();

        // 合併計算點位
        var calcPoints = (await _dataRepository.GetAllCalculatedPointsAsync())
            .Where(c => c.isEnabled)
            .Select(c => new ModbusPointModel { szSID = c.szSID, szName = c.szName, szUnit = c.szUnit });
        pointList.AddRange(calcPoints);

        var rules = (await _alarmRuleService.GetAllRulesAsync()).ToList();

        ViewBag.CoordinatorList = coordinatorList;
        ViewBag.PointList = pointList;
        ViewBag.Rules = rules;

        return View();
    }

    /// <summary>取得所有規則（AJAX）</summary>
    [HttpGet("~/api/alarm-rules")]
    public async Task<IActionResult> GetAllRules()
    {
        var rules = await _alarmRuleService.GetAllRulesAsync();
        return Ok(rules);
    }

    /// <summary>新增或更新規則</summary>
    [HttpPost("~/api/alarm-rules")]
    public async Task<IActionResult> SaveRule([FromBody] AlarmRuleSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.sid))
            return BadRequest(new { success = false, message = "SID 不可為空" });

        var isSuccess = await _alarmRuleService.SaveRuleAsync(dto);
        if (isSuccess)
            return Ok(new { success = true, message = "警報規則已儲存" });

        return StatusCode(500, new { success = false, message = "儲存失敗" });
    }

    /// <summary>刪除規則</summary>
    [HttpDelete("~/api/alarm-rules/{id}")]
    public async Task<IActionResult> DeleteRule(int id)
    {
        var isSuccess = await _alarmRuleService.DeleteRuleAsync(id);
        if (isSuccess)
            return Ok(new { success = true, message = "已刪除" });

        return NotFound(new { success = false, message = "規則不存在" });
    }
}
