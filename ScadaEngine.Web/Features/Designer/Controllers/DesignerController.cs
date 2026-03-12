using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

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
    /// 取得所有可綁定的 Modbus 點位清單（供儀錶板選擇）
    /// </summary>
    [HttpGet("/Designer/Points")]
    public async Task<IActionResult> GetPoints()
    {
        var points = await _repository.GetAllModbusPointsAsync();
        return Json(points.Select(p => new
        {
            szSid  = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit,
            fMin   = p.fMin ?? 0f,
            fMax   = p.fMax ?? 100f
        }));
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

/// <summary>POST /Designer/Save 的請求格式</summary>
public class SaveDesignDto
{
    public string                    szName { get; set; } = "未命名設計";
    public List<ScadaDesignPageModel> pages { get; set; } = new();
}
