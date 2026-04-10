using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.CalcPoint.Models;
using ScadaEngine.Web.Services;
using System.Text.Json;

namespace ScadaEngine.Web.Features.CalcPoint.Controllers;

[Authorize]
public class CalcPointController : Controller
{
    private readonly CalcPointService _service;
    private readonly IDataRepository _repository;

    public CalcPointController(CalcPointService service, IDataRepository repository)
    {
        _service = service;
        _repository = repository;
    }

    [HttpGet("/CalcPoint")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "計算點位";
        var calcPoints = await _service.GetAllAsync();
        var szCalcPointsJson = JsonSerializer.Serialize(calcPoints.Select(p => new
        {
            p.szSID,
            p.szName,
            p.szUnit,
            p.szGroupName,
            p.szFormula,
            p.szInputMappings,
            p.isEnabled,
            szCreatedAt = p.dtCreatedAt.ToString("yyyy-MM-dd HH:mm"),
            szUpdatedAt = p.dtUpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
        }));
        ViewData["CalcPointsJson"] = szCalcPointsJson;
        return View();
    }

    /// <summary>
    /// 取得所有可選點位（Modbus + 已有計算點位）
    /// </summary>
    [HttpGet("/CalcPoint/Points")]
    public async Task<IActionResult> GetPoints()
    {
        var modbusPoints = await _repository.GetAllModbusPointsAsync();
        var calcPoints = await _service.GetAllAsync();

        var allPoints = modbusPoints.Select(p => new
        {
            szSid = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit,
            szType = "Modbus"
        }).Concat(calcPoints.Select(p => new
        {
            szSid = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit,
            szType = "Calculated"
        }));

        return Json(allPoints);
    }

    [HttpPost("/CalcPoint/Create")]
    public async Task<IActionResult> Create([FromBody] CreateCalcPointRequest request)
    {
        var (isSuccess, szMessage, szSID) = await _service.CreateAsync(
            request.Name, request.Unit, request.GroupName,
            request.Formula, request.InputMappings);

        return isSuccess
            ? Json(new { success = true, message = szMessage, sid = szSID })
            : BadRequest(new { success = false, message = szMessage });
    }

    [HttpPost("/CalcPoint/Update")]
    public async Task<IActionResult> Update([FromBody] UpdateCalcPointRequest request)
    {
        var (isSuccess, szMessage) = await _service.UpdateAsync(
            request.SID, request.Name, request.Unit, request.GroupName,
            request.Formula, request.InputMappings, request.IsEnabled);

        return isSuccess
            ? Json(new { success = true, message = szMessage })
            : BadRequest(new { success = false, message = szMessage });
    }

    [HttpPost("/CalcPoint/Delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteCalcPointRequest request)
    {
        var (isSuccess, szMessage) = await _service.DeleteAsync(request.SID);

        return isSuccess
            ? Json(new { success = true, message = szMessage })
            : BadRequest(new { success = false, message = szMessage });
    }

    [HttpPost("/CalcPoint/Preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewCalcPointRequest request)
    {
        var (isSuccess, szMessage, fResult) = await _service.PreviewAsync(
            request.Formula, request.InputMappings);

        return Json(new { success = isSuccess, message = szMessage, result = fResult });
    }
}
