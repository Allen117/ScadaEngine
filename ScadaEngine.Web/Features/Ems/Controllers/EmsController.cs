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
    private readonly BillingPeriodService _billingPeriodService;

    public EmsController(
        IDataRepository repo,
        EnergyCircuitService circuitService,
        EnergyReportService reportService,
        BillingPeriodService billingPeriodService)
    {
        _repo                 = repo;
        _circuitService       = circuitService;
        _reportService        = reportService;
        _billingPeriodService = billingPeriodService;
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

    /// <summary>取得主要電表資訊卡資料 — 節點名 + 電壓/電流/功率/功因 綁定點位（unit 取點位本身）；即時值由前端走既有 /api/realtime/by-sids 輪詢</summary>
    [HttpGet("/EMS/api/main-meter-info")]
    public async Task<IActionResult> GetMainMeterInfo()
    {
        var main = await _circuitService.GetMainMeterAsync();
        if (main == null)
            return Ok(new { hasMainMeter = false });

        // SID → (點位名, 單位) 查找表（Modbus + Calculated + DB 全來源）
        var modbus = await _repo.GetAllModbusPointsAsync();
        var calc = await _repo.GetAllCalculatedPointsAsync();
        var dbPts = await _repo.GetAllDbPointsAsync();
        var lookup = new Dictionary<string, (string szName, string szUnit)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in modbus) lookup.TryAdd(p.szSID, (p.szName, p.szUnit ?? string.Empty));
        foreach (var p in calc) lookup.TryAdd(p.szSID, (p.szName, p.szUnit ?? string.Empty));
        foreach (var p in dbPts) lookup.TryAdd(p.szSID, (p.szName, p.szUnit ?? string.Empty));

        object? Resolve(string? szSid)
        {
            if (string.IsNullOrWhiteSpace(szSid)) return null;
            return lookup.TryGetValue(szSid, out var v)
                ? new { sid = szSid, pointName = v.szName, unit = v.szUnit }
                : new { sid = szSid, pointName = szSid, unit = string.Empty };
        }

        return Ok(new
        {
            hasMainMeter = true,
            name        = main.szName,
            voltage     = Resolve(main.szVoltageSID),
            current     = Resolve(main.szCurrentSID),
            power       = Resolve(main.szPowerSID),
            powerFactor = Resolve(main.szPowerFactorSID)
        });
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
            (dtStart, dtEnd) = await ParsePivotAsync(granularity, pivot);
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

    /// <summary>取得主要電表基本資訊（IsMainMeter = 1，全系統唯一）</summary>
    [HttpGet("/EMS/api/main-meter")]
    public async Task<IActionResult> GetMainMeter()
    {
        var meter = await _circuitService.GetMainMeterAsync();
        if (meter == null)
            return Ok(new EmsMainMeterDto { hasMainMeter = false });

        return Ok(new EmsMainMeterDto
        {
            hasMainMeter = true,
            id           = meter.nId,
            name         = meter.szName,
            hasChildren  = await _circuitService.HasChildrenAsync(meter.nId)
        });
    }

    /// <summary>主要電表直接子迴路在區間內的用電量拆解（圓餅圖用）；無子迴路時回主要電表自己一筆</summary>
    /// <param name="granularity">month / day / hour（同 circuit-energy）</param>
    /// <param name="pivot">month=年份(2026)；day=年月(2026-06)；hour=日期(2026-06-29)</param>
    [HttpGet("/EMS/api/main-meter-breakdown")]
    public async Task<IActionResult> GetMainMeterBreakdown(
        [FromQuery] string? granularity,
        [FromQuery] string? pivot)
    {
        if (string.IsNullOrWhiteSpace(granularity) || string.IsNullOrWhiteSpace(pivot))
            return BadRequest(new { error = "granularity, pivot 皆為必填" });

        DateTime dtStart, dtEnd;
        try { (dtStart, dtEnd) = await ParsePivotAsync(granularity, pivot); }
        catch { return BadRequest(new { error = "pivot 格式不正確" }); }

        var meter = await _circuitService.GetMainMeterAsync();
        if (meter == null)
            return Ok(new EmsMainMeterBreakdownDto { hasMainMeter = false });

        var dto = new EmsMainMeterBreakdownDto { hasMainMeter = true, meterName = meter.szName };

        var children = await _circuitService.GetDirectChildrenAsync(meter.nId);
        if (children.Count == 0)
        {
            dto.items.Add(new EmsBreakdownItemDto
            {
                id   = meter.nId,
                name = meter.szName,
                kwh  = await _reportService.GetTotalKwhAsync(meter.nId, granularity, dtStart, dtEnd)
            });
            return Ok(dto);
        }

        foreach (var child in children)
        {
            // 子迴路內部 leaves 的 sign 已由計算核心累乘（相對於 child），child 自己對父的方向在這裡補乘
            var nChildSign = child.nSign == -1 ? -1 : 1;
            var dKwh = await _reportService.GetTotalKwhAsync(child.nId, granularity, dtStart, dtEnd);
            dto.items.Add(new EmsBreakdownItemDto
            {
                id   = child.nId,
                name = child.szName,
                kwh  = Math.Round(dKwh * nChildSign, 3)
            });
        }
        return Ok(dto);
    }

    /// <summary>主要電表 + 各直接子迴路的本期 vs 去年同期用電比較（比較表用）；首列為主要電表</summary>
    /// <param name="granularity">month / day / hour（同 circuit-energy）</param>
    /// <param name="pivot">month=年份(2026)；day=年月(2026-06)；hour=日期(2026-06-29)</param>
    [HttpGet("/EMS/api/main-meter-yoy")]
    public async Task<IActionResult> GetMainMeterYoy(
        [FromQuery] string? granularity,
        [FromQuery] string? pivot)
    {
        if (string.IsNullOrWhiteSpace(granularity) || string.IsNullOrWhiteSpace(pivot))
            return BadRequest(new { error = "granularity, pivot 皆為必填" });

        DateTime dtStart, dtEnd;
        try { (dtStart, dtEnd) = await ParsePivotAsync(granularity, pivot); }
        catch { return BadRequest(new { error = "pivot 格式不正確" }); }

        var meter = await _circuitService.GetMainMeterAsync();
        if (meter == null)
            return Ok(new EmsMainMeterYoyDto { hasMainMeter = false });

        // 去年同期：重建去年 pivot 再走同一解析（月/日粒度會取去年期別設定，2/29 → 2/28）
        DateTime dtLastStart, dtLastEnd;
        try { (dtLastStart, dtLastEnd) = await ParsePivotAsync(granularity, LastYearPivot(granularity, pivot)); }
        catch { return BadRequest(new { error = "pivot 格式不正確" }); }

        var dto = new EmsMainMeterYoyDto { hasMainMeter = true };
        dto.rows.Add(await BuildYoyRowAsync(meter.nId, meter.szName, true, 1,
            granularity, dtStart, dtEnd, dtLastStart, dtLastEnd));

        foreach (var child in await _circuitService.GetDirectChildrenAsync(meter.nId))
        {
            var nChildSign = child.nSign == -1 ? -1 : 1;
            dto.rows.Add(await BuildYoyRowAsync(child.nId, child.szName, false, nChildSign,
                granularity, dtStart, dtEnd, dtLastStart, dtLastEnd));
        }
        return Ok(dto);
    }

    private async Task<EmsYoyRowDto> BuildYoyRowAsync(
        int nCircuitId, string szName, bool isMainMeter, int nSign,
        string granularity, DateTime dtStart, DateTime dtEnd, DateTime dtLastStart, DateTime dtLastEnd)
    {
        var dCurrent  = Math.Round(await _reportService.GetTotalKwhAsync(nCircuitId, granularity, dtStart, dtEnd) * nSign, 3);
        var dLastYear = Math.Round(await _reportService.GetTotalKwhAsync(nCircuitId, granularity, dtLastStart, dtLastEnd) * nSign, 3);
        var dDiff     = Math.Round(dCurrent - dLastYear, 3);
        return new EmsYoyRowDto
        {
            id          = nCircuitId,
            name        = szName,
            isMainMeter = isMainMeter,
            currentKwh  = dCurrent,
            lastYearKwh = dLastYear,
            diffKwh     = dDiff,
            // 去年為 0（含無資料）時無法算增減比，回 null 由前端顯示 --；負底取絕對值保留增減方向語意
            pctChange   = dLastYear == 0 ? null : Math.Round(dDiff / Math.Abs(dLastYear) * 100, 1)
        };
    }

    /// <summary>
    /// pivot → 報表服務的 (dtStart, dtEnd)。
    /// month（年檢視）：pivot=年份 → 期別 1~12 月（報表月粒度語意 = 含頭尾期別）；
    /// day（月檢視）：pivot=YYYY-MM → 該期別的實際起訖日（日粒度 dtEnd 為含訖日）；
    /// hour（日檢視）：pivot=YYYY-MM-DD → 該日（維持自然日，不受期別影響）。
    /// </summary>
    private async Task<(DateTime Start, DateTime End)> ParsePivotAsync(string granularity, string pivot)
    {
        switch (granularity)
        {
            case "month":
                {
                    var nYear = int.Parse(pivot);
                    return (new DateTime(nYear, 1, 1), new DateTime(nYear, 12, 1));
                }
            case "day":
                {
                    var dtYM = DateTime.ParseExact(pivot + "-01", "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture);
                    var period = await _billingPeriodService.GetPeriodAsync(dtYM.Year, dtYM.Month);
                    return (period.dtStart, period.dtEndInclusive);
                }
            case "hour":
                {
                    var dtDay = DateTime.ParseExact(pivot, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture);
                    return (dtDay, dtDay.AddDays(1));
                }
            default:
                throw new ArgumentException("不支援的 granularity");
        }
    }

    /// <summary>去年同期 pivot 字串（與前端 lastYearPivotStr 同邏輯；hour 粒度 2/29 → 2/28）</summary>
    private static string LastYearPivot(string granularity, string pivot)
    {
        switch (granularity)
        {
            case "month":
                return (int.Parse(pivot) - 1).ToString();
            case "day":
                {
                    var aParts = pivot.Split('-');
                    return $"{int.Parse(aParts[0]) - 1}-{aParts[1]}";
                }
            default:
                {
                    var aParts = pivot.Split('-');
                    var szDay = aParts[1] == "02" && aParts[2] == "29" ? "28" : aParts[2];
                    return $"{int.Parse(aParts[0]) - 1}-{aParts[1]}-{szDay}";
                }
        }
    }
}
