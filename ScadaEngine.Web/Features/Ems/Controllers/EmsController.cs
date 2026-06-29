using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.EnergyMeter.Models;
using ScadaEngine.Web.Features.Ems.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Ems.Controllers;

/// <summary>
/// 能源管理 Hub — /EMS 進入點
/// </summary>
[Authorize]
public class EmsController : Controller
{
    private readonly IDataRepository _repo;
    private readonly EnergyCircuitService _circuitService;
    private readonly EnergyReportService _reportService;

    public EmsController(IDataRepository repo, EnergyCircuitService circuitService, EnergyReportService reportService)
    {
        _repo           = repo;
        _circuitService = circuitService;
        _reportService  = reportService;
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

    /// <summary>取得所有可作為 EMS 需量選單的迴路（葉子＋含有啟用後裔的虛擬迴路）</summary>
    [HttpGet("/EMS/api/demand-circuits")]
    public async Task<IActionResult> GetDemandCircuits()
    {
        var circuits = await _repo.GetCircuitsForDemandAsync();
        return Ok(circuits.Select(c => new { id = c.nId, name = c.szName }));
    }

    /// <summary>取得指定迴路今日即時需量與今日最高需量</summary>
    [HttpGet("/EMS/api/demand-today")]
    public async Task<IActionResult> GetDemandToday([FromQuery] int? circuitId)
    {
        if (circuitId == null)
            return BadRequest(new { error = "circuitId 不得為空" });

        var result = await _repo.GetTodayDemandByCircuitIdAsync(circuitId.Value);
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

    /// <summary>取得指定迴路今日需量趨勢資料（折線圖用）</summary>
    [HttpGet("/EMS/api/demand-trend")]
    public async Task<IActionResult> GetDemandTrend([FromQuery] int? circuitId)
    {
        if (circuitId == null)
            return BadRequest(new { error = "circuitId 不得為空" });

        var points = await _repo.GetTodayDemandTrendByCircuitIdAsync(circuitId.Value);
        return Ok(points.Select(p => new
        {
            t = p.dtTimestamp.ToString("HH:mm"),
            v = p.dDemandKW,
            q = p.nQuality
        }));
    }

    /// <summary>取得完整迴路階層（flat 清單，前端組樹）</summary>
    [HttpGet("/EMS/api/circuit-tree")]
    public async Task<IActionResult> GetCircuitTree()
    {
        var nodes = await _circuitService.GetAllAsync();
        return Ok(nodes.Select(n => new EnergyCircuitNodeViewModel
        {
            id              = n.nId,
            name            = n.szName,
            parentId        = n.nParentId,
            sortOrder       = n.nSortOrder,
            sid             = n.szSID,
            maxKwh          = n.dMaxKwh,
            sign            = n.nSign,
            isDemandEnabled = n.isIsDemandEnabled,
            description     = n.szDescription
        }));
    }

    /// <summary>取得指定迴路的累計用電資料（長條圖用）</summary>
    /// <param name="circuitId">迴路 ID</param>
    /// <param name="granularity">month / day / hour</param>
    /// <param name="pivot">month=年份(2026)；day=年月(2026-06)；hour=日期(2026-06-29)</param>
    [HttpGet("/EMS/api/circuit-energy")]
    public async Task<IActionResult> GetCircuitEnergy(
        [FromQuery] int? circuitId,
        [FromQuery] string? granularity,
        [FromQuery] string? pivot)
    {
        if (circuitId == null || string.IsNullOrWhiteSpace(granularity) || string.IsNullOrWhiteSpace(pivot))
            return BadRequest(new { error = "circuitId, granularity, pivot 皆為必填" });

        DateTime dtStart, dtEnd;
        try
        {
            (dtStart, dtEnd) = ParsePivot(granularity, pivot);
        }
        catch
        {
            return BadRequest(new { error = "pivot 格式不正確" });
        }

        var result = await _reportService.GetReportAsync(circuitId.Value, granularity, dtStart, dtEnd);
        return Ok(new EmsCircuitEnergyDto
        {
            labels = result.buckets.Select(b => b.szLabel).ToList(),
            values = result.buckets.Select(b => b.dKwh).ToList()
        });
    }

    private static (DateTime Start, DateTime End) ParsePivot(string granularity, string pivot)
    {
        return granularity switch
        {
            "month" => (
                new DateTime(int.Parse(pivot), 1, 1),
                new DateTime(int.Parse(pivot) + 1, 1, 1)
            ),
            "day" => (
                DateTime.ParseExact(pivot + "-01", "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                DateTime.ParseExact(pivot + "-01", "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture).AddMonths(1)
            ),
            "hour" => (
                DateTime.ParseExact(pivot, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                DateTime.ParseExact(pivot, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture).AddDays(1)
            ),
            _ => throw new ArgumentException("不支援的 granularity")
        };
    }
}
