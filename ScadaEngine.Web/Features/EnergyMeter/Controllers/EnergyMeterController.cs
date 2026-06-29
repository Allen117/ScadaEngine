using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.EnergyMeter.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.EnergyMeter.Controllers;

[Authorize]
[Route("[controller]")]
public class EnergyMeterController : Controller
{
    private readonly EnergyCircuitService _service;
    private readonly IDataRepository _repository;
    private readonly ILogger<EnergyMeterController> _logger;

    public EnergyMeterController(
        EnergyCircuitService service,
        IDataRepository repository,
        ILogger<EnergyMeterController> logger)
    {
        _service = service;
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("/EnergyMeter")]
    public IActionResult Index()
    {
        ViewData["Title"] = "電表/迴路設定";
        return View();
    }

    [HttpGet("api/tree")]
    public async Task<IActionResult> GetTree()
    {
        var nodes = await _service.GetAllAsync();
        return Ok(nodes.Select(n => new EnergyCircuitNodeViewModel
        {
            id = n.nId,
            name = n.szName,
            parentId = n.nParentId,
            sortOrder = n.nSortOrder,
            sid = n.szSID,
            maxKwh = n.dMaxKwh,
            sign = n.nSign,
            isDemandEnabled = n.isIsDemandEnabled,
            description = n.szDescription
        }));
    }

    [HttpGet("api/sids")]
    public async Task<IActionResult> GetSidOptions()
    {
        var modbus = await _repository.GetAllModbusPointsAsync();
        var calc = await _repository.GetAllCalculatedPointsAsync();
        var dbPts = await _repository.GetAllDbPointsAsync();
        var coords = (await _repository.GetAllCoordinatorsAsync()).ToList();
        var dbCoords = (await _repository.GetAllDbCoordinatorsAsync()).ToList();

        string ResolveDeviceName(string szSID)
        {
            // SID 前綴 = coord.Id * 65536 + subModbusId * 256 + pointSeq
            var nHyphen = szSID.IndexOf('-');
            if (nHyphen <= 0) return string.Empty;
            if (!int.TryParse(szSID[..nHyphen], out var nPrefix)) return string.Empty;

            var nCoordId = nPrefix / 65536;
            var nSubModbusId = (nPrefix % 65536) / 256;
            var coord = coords.FirstOrDefault(c => c.Id == nCoordId);
            if (coord == null) return string.Empty;

            var ids = (coord.szModbusID ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var names = (coord.szDeviceName ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            // 單 ID 協調器：直接用協調器名稱
            if (ids.Length <= 1) return coord.szName ?? string.Empty;

            // 多 ID 協調器：找與 subModbusId 相符的子設備
            for (int i = 0; i < ids.Length; i++)
            {
                if (int.TryParse(ids[i], out var nMid) && nMid == nSubModbusId)
                    return (i < names.Length && !string.IsNullOrWhiteSpace(names[i]))
                        ? names[i] : (coord.szName ?? string.Empty);
            }
            return coord.szName ?? string.Empty;
        }

        var list = modbus
            .Where(p => string.Equals(p.szUnit, "kWh", StringComparison.OrdinalIgnoreCase))
            .Select(p => new CircuitSidOptionDto
            {
                sid = p.szSID,
                name = p.szName,
                unit = p.szUnit,
                source = "Modbus",
                deviceName = ResolveDeviceName(p.szSID)
            })
            .Concat(calc
                .Where(p => string.Equals(p.szUnit, "kWh", StringComparison.OrdinalIgnoreCase))
                .Select(p => new CircuitSidOptionDto
                {
                    sid = p.szSID,
                    name = p.szName,
                    unit = p.szUnit,
                    source = "Calculated",
                    deviceName = p.szGroupName ?? string.Empty
                }))
            .Concat(dbPts
                .Where(p => string.Equals(p.szUnit, "kWh", StringComparison.OrdinalIgnoreCase))
                .Select(p => new CircuitSidOptionDto
                {
                    sid = p.szSID,
                    name = p.szName,
                    unit = p.szUnit ?? string.Empty,
                    source = "DB",
                    deviceName = dbCoords.FirstOrDefault(c => c.Id == p.nCoordinatorId)?.szName ?? string.Empty
                }));
        return Ok(list);
    }

    [HttpPost("api/tree")]
    public async Task<IActionResult> Create([FromBody] CreateCircuitDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.name))
            return BadRequest(new { success = false, message = "名稱不可為空" });
        if (dto.sign != 1 && dto.sign != -1)
            return BadRequest(new { success = false, message = "貢獻方向只能為 +1 或 -1" });

        var nId = await _service.CreateAsync(new EnergyCircuitModel
        {
            szName = dto.name,
            nParentId = dto.parentId,
            szSID = string.IsNullOrWhiteSpace(dto.sid) ? null : dto.sid,
            dMaxKwh = dto.maxKwh,
            nSign = dto.sign,
            isIsDemandEnabled = dto.isDemandEnabled,
            szDescription = dto.description
        });
        return Ok(new { success = true, id = nId });
    }

    [HttpPut("api/tree/{nId}")]
    public async Task<IActionResult> Update(int nId, [FromBody] UpdateCircuitDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.name))
            return BadRequest(new { success = false, message = "名稱不可為空" });
        if (dto.sign != 1 && dto.sign != -1)
            return BadRequest(new { success = false, message = "貢獻方向只能為 +1 或 -1" });

        var ok = await _service.UpdateAsync(nId, dto.name,
            string.IsNullOrWhiteSpace(dto.sid) ? null : dto.sid,
            dto.maxKwh, dto.sign,
            dto.isDemandEnabled,
            dto.description);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "節點不存在" });
    }

    [HttpDelete("api/tree/{nId}")]
    public async Task<IActionResult> Delete(int nId, [FromQuery] bool force = false)
    {
        var hasChildren = await _service.HasChildrenAsync(nId);
        if (hasChildren && !force)
            return Conflict(new { success = false, message = "該迴路含子節點，刪除將一併移除所有子孫，請確認後再執行（force=true）" });

        var ok = await _service.DeleteAsync(nId);
        return ok ? Ok(new { success = true }) : NotFound(new { success = false, message = "節點不存在" });
    }

    [HttpPut("api/tree/sort")]
    public async Task<IActionResult> UpdateSort([FromBody] List<CircuitSortDto> dtoList)
    {
        var sortList = dtoList.Select(d => (d.id, d.parentId, d.sortOrder));
        var ok = await _service.UpdateSortOrderAsync(sortList);
        return ok ? Ok(new { success = true }) : StatusCode(500, new { success = false });
    }
}
