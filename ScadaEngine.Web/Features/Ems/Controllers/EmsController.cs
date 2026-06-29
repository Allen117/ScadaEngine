using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Ems.Controllers;

/// <summary>
/// 能源管理 Hub — /EMS 進入點
/// </summary>
[Authorize]
public class EmsController : Controller
{
    private readonly IDataRepository _repo;

    public EmsController(IDataRepository repo)
    {
        _repo = repo;
    }

    [HttpGet("/EMS")]
    public IActionResult Index()
    {
        if (!PermissionService.IsAdmin(User))
        {
            bool hasAny = PermissionService.EmsRoutes
                .Where(r => !string.Equals(r, "/EMS", StringComparison.OrdinalIgnoreCase))
                .Any(r => PermissionService.CanAccessPage(User, r));
            if (!hasAny)
                return Redirect("/ScadaPage");
        }

        return View();
    }

    /// <summary>取得所有已設定需量 SID 的電表/迴路（供下拉選單）</summary>
    [HttpGet("/EMS/api/demand-circuits")]
    public async Task<IActionResult> GetDemandCircuits()
    {
        var circuits = await _repo.GetCircuitsWithDemandAsync();
        return Ok(circuits.Select(c => new { sid = c.szDemandSID, name = c.szName }));
    }

    /// <summary>取得指定 SID 今日即時需量與今日最高需量</summary>
    [HttpGet("/EMS/api/demand-today")]
    public async Task<IActionResult> GetDemandToday([FromQuery] string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return BadRequest(new { error = "sid 不得為空" });

        var result = await _repo.GetTodayDemandAsync(sid);
        if (result == null)
            return Ok(new { hasData = false });

        return Ok(new
        {
            hasData   = true,
            currentKW = result.dCurrentKW,
            maxKW     = result.dMaxKW,
            maxAt     = result.dtMaxAt.HasValue ? result.dtMaxAt.Value.ToString("HH:mm") : null,
            quality   = result.nQuality
        });
    }

    /// <summary>取得指定 SID 今日需量趨勢資料（折線圖用）</summary>
    [HttpGet("/EMS/api/demand-trend")]
    public async Task<IActionResult> GetDemandTrend([FromQuery] string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return BadRequest(new { error = "sid 不得為空" });

        var points = await _repo.GetTodayDemandTrendAsync(sid);
        return Ok(points.Select(p => new
        {
            t = p.dtTimestamp.ToString("HH:mm"),
            v = p.dDemandKW,
            q = p.nQuality
        }));
    }
}
