using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;
using ScadaEngine.Web.Features.ConditionCtrl.Models;

namespace ScadaEngine.Web.Features.ConditionCtrl.Controllers;

[Authorize]
[Route("[controller]")]
public class ConditionCtrlController : Controller
{
    private readonly IDataRepository _dataRepository;
    private readonly ILogger<ConditionCtrlController> _logger;
    private readonly IStringLocalizer<ConditionCtrlController> _l;

    public ConditionCtrlController(
        IDataRepository dataRepository,
        ILogger<ConditionCtrlController> logger,
        IStringLocalizer<ConditionCtrlController> localizer)
    {
        _dataRepository = dataRepository;
        _logger = logger;
        _l = localizer;
    }

    [HttpGet("/ConditionCtrl")]
    public async Task<IActionResult> Index()
    {
        var coordinatorList = (await _dataRepository.GetAllCoordinatorsAsync()).ToList();
        var dbCoordinatorList = (await _dataRepository.GetAllDbCoordinatorsAsync()).ToList();
        var pointList = (await _dataRepository.GetAllModbusPointsAsync())
            .OrderBy(p => {
                var idx = p.szSID.IndexOf("-S", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 && int.TryParse(p.szSID[(idx + 2)..], out var n) ? n : int.MaxValue;
            })
            .ToList();
        // 合併計算點位 + DB 來源點位
        pointList.AddRange((await _dataRepository.GetAllCalculatedPointsAsync())
            .Where(c => c.isEnabled)
            .Select(c => new ModbusPointModel { szSID = c.szSID, szName = c.szName, szUnit = c.szUnit }));
        pointList.AddRange((await _dataRepository.GetAllDbPointsAsync())
            .Select(p => new ModbusPointModel { szSID = p.szSID, szName = p.szName, szUnit = p.szUnit ?? string.Empty }));

        var existingRules = (await _dataRepository.GetAllConditionControlRulesAsync()).ToList();

        var viewModel = new ConditionControlViewModel
        {
            CoordinatorList   = coordinatorList,
            DbCoordinatorList = dbCoordinatorList,
            PointList         = pointList,
            ExistingRules     = existingRules
        };

        return View(viewModel);
    }

    [HttpPost("/ConditionCtrl/SaveRules")]
    public async Task<IActionResult> SaveRules([FromBody] List<ConditionControlRuleSaveDto> dtoList)
    {
        var rules = dtoList.Select(d => new ConditionControlRuleModel
        {
            szConditionPointSID = d.ConditionPointSID,
            nOperator           = d.Operator,
            dConditionValue     = d.ConditionValue,
            szControlPointSID   = d.ControlPointSID,
            dControlValue       = d.ControlValue,
            szRemarks           = d.Remarks,
            isEnabled           = true
        });

        var isSuccess = await _dataRepository.SaveConditionControlRulesAsync(rules);

        if (isSuccess)
            return Ok(new { success = true, message = string.Format(_l["conditionctrl.api.save_success"].Value, dtoList.Count) });

        return StatusCode(500, new { success = false, message = _l["conditionctrl.api.save_failed"].Value });
    }
}
