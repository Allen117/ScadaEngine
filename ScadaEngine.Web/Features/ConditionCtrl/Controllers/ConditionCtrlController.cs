using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public ConditionCtrlController(IDataRepository dataRepository, ILogger<ConditionCtrlController> logger)
    {
        _dataRepository = dataRepository;
        _logger = logger;
    }

    [HttpGet("/ConditionCtrl")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "條件控制";

        var coordinatorList = (await _dataRepository.GetAllCoordinatorsAsync()).ToList();
        var pointList = (await _dataRepository.GetAllModbusPointsAsync())
            .OrderBy(p => {
                var idx = p.szSID.IndexOf("-S", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 && int.TryParse(p.szSID[(idx + 2)..], out var n) ? n : int.MaxValue;
            })
            .ToList();
        var existingRules = (await _dataRepository.GetAllConditionControlRulesAsync()).ToList();

        var viewModel = new ConditionControlViewModel
        {
            CoordinatorList = coordinatorList,
            PointList       = pointList,
            ExistingRules   = existingRules
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
            return Ok(new { success = true, message = $"已儲存 {dtoList.Count} 筆規則" });

        return StatusCode(500, new { success = false, message = "儲存失敗，請查看 Engine 日誌" });
    }
}
