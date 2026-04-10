using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.Designer.Models;

namespace ScadaEngine.Web.Features.Designer.Controllers;

[Authorize]
public class DesignerController : Controller
{
    private readonly IDataRepository _repository;
    private readonly ILogger<DesignerController> _logger;

    public DesignerController(IDataRepository repository, ILogger<DesignerController> logger)
    {
        _repository = repository;
        _logger     = logger;
    }

    [HttpGet("/Designer")]
    public IActionResult Index()
    {
        ViewData["Title"] = "畫面設計";
        return View();
    }

    /// <summary>
    /// 取得所有可綁定的點位清單（Modbus + 計算點位，供儀錶板選擇）
    /// </summary>
    [HttpGet("/Designer/Points")]
    public async Task<IActionResult> GetPoints()
    {
        var modbusPoints = await _repository.GetAllModbusPointsAsync();
        var calcPoints = await _repository.GetAllCalculatedPointsAsync();

        var allPoints = modbusPoints.Select(p => new
        {
            szSid       = p.szSID,
            szName      = p.szName,
            szUnit      = p.szUnit,
            fMin        = p.fMin ?? 0f,
            fMax        = p.fMax ?? 100f,
            szGroupName = ""
        }).Concat(calcPoints.Where(c => c.isEnabled).Select(c => new
        {
            szSid       = c.szSID,
            szName      = c.szName,
            szUnit      = c.szUnit,
            fMin        = 0f,
            fMax        = 100f,
            szGroupName = c.szGroupName
        }));

        return Json(allPoints);
    }

    /// <summary>
    /// 取得所有設備（Coordinator）清單（供儀錶板兩步驟選擇第一步）
    /// </summary>
    [HttpGet("/Designer/Devices")]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _repository.GetAllCoordinatorsAsync();
        return Json(devices.Select(d => new
        {
            nId            = d.Id,
            szName         = d.szName,
            szModbusID     = d.szModbusID,
            szDeviceName   = d.szDeviceName
        }));
    }

    /// <summary>
    /// 讀取已發布的畫面設計（供 Designer 頁面初始化時還原狀態）
    /// </summary>
    [HttpGet("/Designer/Load")]
    public async Task<IActionResult> Load()
    {
        var pages    = await _repository.LoadPublishedDesignAsync();
        var pageList = pages.ToList();
        return Json(new { hasData = pageList.Any(), pages = pageList });
    }

    /// <summary>
    /// 儲存畫面設計至資料庫
    /// </summary>
    [HttpPost("/Designer/Save")]
    public async Task<IActionResult> Save([FromBody] SaveDesignDto dto)
    {
        try
        {
            var isOk = await _repository.SaveDesignAsync(dto.szName, dto.pages);
            return Json(new { success = isOk });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存畫面設計時發生錯誤");
            return Json(new { success = false, error = ex.Message });
        }
    }
}
