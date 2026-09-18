using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;
using ScadaEngine.Web.Features.Designer.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.Designer.Controllers;

/// <summary>
/// 畫面設計 — 設計頁與寫入 API 為工程師模式專屬（action-level Roles="Engineer"）；
/// Points / Devices / Load 三個唯讀 API 為執行期共用（ScadaPage 載入設計、EventLog/CalcPoint/LogicFlow 點位選擇器），維持一般 [Authorize]
/// </summary>
[Authorize]
public class DesignerController : Controller
{
    private readonly IDataRepository _repository;
    private readonly ILogger<DesignerController> _logger;
    private readonly IStringLocalizer<DesignerController> _l;
    private readonly DesignerTemplateService _templateService;
    private readonly EnergyCircuitService _circuitService;
    private readonly IWebHostEnvironment _env;

    // 自訂圖片上傳限制（image widget）：單檔上限 + 允許型別（決策 4 / 已知風險）
    private const long AssetMaxBytes = 2 * 1024 * 1024;   // 2MB
    private static readonly HashSet<string> AssetAllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/gif", "image/png", "image/jpeg", "image/webp", "image/svg+xml" };

    // 內建圖庫根目錄（相對 wwwroot），成對命名約定：{名稱}_anim.* / {名稱}_still.*
    private const string GalleryRelDir = "img/designer-gifs";

    public DesignerController(
        IDataRepository repository,
        ILogger<DesignerController> logger,
        IStringLocalizer<DesignerController> localizer,
        DesignerTemplateService templateService,
        EnergyCircuitService circuitService,
        IWebHostEnvironment env)
    {
        _repository      = repository;
        _logger          = logger;
        _l               = localizer;
        _templateService = templateService;
        _circuitService  = circuitService;
        _env             = env;
    }

    [HttpGet("/Designer")]
    [Authorize(Roles = "Engineer")]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// 取得所有可綁定的點位清單（Modbus + 計算點位 + DB 來源點位，供儀錶板選擇）
    /// </summary>
    [HttpGet("/Designer/Points")]
    public async Task<IActionResult> GetPoints()
    {
        var modbusPoints   = await _repository.GetAllModbusPointsAsync();
        var calcPoints     = await _repository.GetAllCalculatedPointsAsync();
        var dbPoints       = await _repository.GetAllDbPointsAsync();
        var dbCoordinators = await _repository.GetAllDbCoordinatorsAsync();
        var dbCoordNameMap = dbCoordinators.ToDictionary(c => c.Id, c => c.szName);
        var opcUaPoints    = await _repository.GetAllOpcUaPointsAsync();
        var opcUaCoordinators = await _repository.GetAllOpcUaCoordinatorsAsync();
        var opcUaCoordNameMap = opcUaCoordinators.ToDictionary(c => c.Id, c => c.szName);

        // szDeviceGroup：Modbus 站號內子設備分群（Tag.Device 投影）；其餘來源本就有自己的群組欄（szGroupName），此欄留 null
        var allPoints = modbusPoints.Select(p => new
        {
            szSid         = p.szSID,
            szName        = p.szName,
            szUnit        = p.szUnit,
            fMin          = p.fMin ?? 0f,
            fMax          = p.fMax ?? 100f,
            szGroupName   = "",
            szDeviceGroup = p.szDeviceGroup
        }).Concat(calcPoints.Where(c => c.isEnabled).Select(c => new
        {
            szSid         = c.szSID,
            szName        = c.szName,
            szUnit        = c.szUnit,
            fMin          = 0f,
            fMax          = 100f,
            szGroupName   = c.szGroupName,
            szDeviceGroup = (string?)null
        })).Concat(dbPoints.Select(p => new
        {
            szSid         = p.szSID,
            szName        = p.szName,
            szUnit        = p.szUnit ?? string.Empty,
            fMin          = p.fMin,
            fMax          = p.fMax,
            szGroupName   = dbCoordNameMap.TryGetValue(p.nCoordinatorId, out var szName) ? szName : "DB",
            szDeviceGroup = (string?)null
        })).Concat(opcUaPoints.Select(p => new
        {
            szSid         = p.szSID,
            szName        = p.szName,
            szUnit        = p.szUnit ?? string.Empty,
            fMin          = p.fMin ?? 0f,
            fMax          = p.fMax ?? 100f,
            szGroupName   = opcUaCoordNameMap.TryGetValue(p.nCoordinatorId, out var szCoordName)
                ? (string.IsNullOrEmpty(p.szDeviceName) ? szCoordName : $"{szCoordName}/{p.szDeviceName}")
                : "OPCUA",
            szDeviceGroup = (string?)null
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
    /// 取得所有能源迴路（含虛擬節點），供 picker「迴路」分頁組樹 + SID 反查迴路對照表共用。
    /// 五個 SID 欄（kWh/V/A/kW/PF）全部帶回，前端據此做表頭驅動整列自動帶入。
    /// </summary>
    [HttpGet("/Designer/api/circuits")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> GetCircuits()
    {
        var circuits = await _circuitService.GetAllAsync();
        return Json(circuits.Select(c => new
        {
            id             = c.nId,
            name           = c.szName,
            parentId       = c.nParentId,
            sortOrder      = c.nSortOrder,
            sid            = c.szSID,
            maxKwh         = c.dMaxKwh,
            voltageSid     = c.szVoltageSID,
            currentSid     = c.szCurrentSID,
            powerSid       = c.szPowerSID,
            powerFactorSid = c.szPowerFactorSID
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
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> Save([FromBody] SaveDesignDto dto)
    {
        try
        {
            var szName = string.IsNullOrWhiteSpace(dto.szName)
                ? _l["designer.untitled_design"].Value
                : dto.szName;
            var isOk = await _repository.SaveDesignAsync(szName, dto.pages);
            return Json(new { success = isOk });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, _l["designer.save_error"].Value);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 取得列範本（分隔符 + 角色順序）
    /// </summary>
    [HttpGet("/Designer/Templates")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> GetTemplates()
    {
        var dto = await _templateService.ReadAsync();
        return Json(new { szSeparator = dto.szSeparator, arrRoles = dto.arrRoles });
    }

    /// <summary>
    /// 整批覆寫列範本（用於「套用並存為預設」）
    /// </summary>
    [HttpPost("/Designer/Templates")]
    [Authorize(Roles = "Engineer")]
    public async Task<IActionResult> SaveTemplates([FromBody] DesignerTemplateFileDto dto)
    {
        if (dto == null || dto.arrRoles == null || dto.arrRoles.Count == 0)
        {
            return Json(new { success = false, error = _l["designer.row_template.invalid_payload"].Value });
        }
        var isOk = await _templateService.WriteAsync(dto);
        return Json(new { success = isOk });
    }

    // ============================================================
    // image widget — 自訂圖片上傳 / 提供 / 內建圖庫（plan 2026-09-18 決策 1 / 4）
    // ============================================================

    /// <summary>
    /// 上傳自訂圖片（GIF / 靜止圖）。以內容 SHA-256 去重，回傳可直接當 img src 的 URL。
    /// </summary>
    [HttpPost("/Designer/asset")]
    [Authorize(Roles = "Engineer")]
    [RequestSizeLimit(AssetMaxBytes + 4096)]
    public async Task<IActionResult> UploadAsset(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, error = _l["designer.image.upload_empty"].Value });
        if (file.Length > AssetMaxBytes)
            return Json(new { success = false, error = _l["designer.image.upload_too_large"].Value });
        if (!AssetAllowedTypes.Contains(file.ContentType))
            return Json(new { success = false, error = _l["designer.image.upload_bad_type"].Value });

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var szHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var isOk = await _repository.UpsertDesignAssetAsync(new ScadaDesignAssetModel
            {
                szHash        = szHash,
                szContentType = file.ContentType,
                szDataBase64  = Convert.ToBase64String(bytes),
                nByteSize     = bytes.Length
            });
            if (!isOk)
                return Json(new { success = false, error = _l["designer.image.upload_failed"].Value });

            return Json(new { success = true, url = $"/Designer/asset/{szHash}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadAsset 失敗");
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 依 Hash 提供圖片內容。內容定址（Hash=內容）故可長快取；ScadaPage 執行期共用（一般 [Authorize]）。
    /// </summary>
    [HttpGet("/Designer/asset/{hash}")]
    public async Task<IActionResult> GetAsset(string hash)
    {
        var asset = await _repository.GetDesignAssetAsync(hash);
        if (asset == null) return NotFound();

        byte[] bytes;
        try { bytes = Convert.FromBase64String(asset.szDataBase64); }
        catch { return NotFound(); }

        // 內容不會變（Hash=內容），可放心長快取
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(bytes, string.IsNullOrEmpty(asset.szContentType) ? "application/octet-stream" : asset.szContentType);
    }

    /// <summary>
    /// 列出內建圖庫（wwwroot/img/designer-gifs/{分類}/{名稱}_anim|_still.*）。
    /// 掃目錄動態產生，免手維護 manifest。
    /// </summary>
    [HttpGet("/Designer/gallery")]
    [Authorize(Roles = "Engineer")]
    public IActionResult GetGallery()
    {
        var result = new List<object>();
        try
        {
            var szRoot = Path.Combine(_env.WebRootPath, GalleryRelDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(szRoot)) return Json(result);

            foreach (var szCatDir in Directory.GetDirectories(szRoot).OrderBy(d => d))
            {
                var szCat = Path.GetFileName(szCatDir);
                // 依 _anim / _still 前的基底名配對
                var groups = new Dictionary<string, (string? anim, string? still)>(StringComparer.OrdinalIgnoreCase);
                foreach (var szFile in Directory.GetFiles(szCatDir).OrderBy(f => f))
                {
                    var szBare = Path.GetFileNameWithoutExtension(szFile);
                    string szBase; bool bAnim;
                    if (szBare.EndsWith("_anim", StringComparison.OrdinalIgnoreCase))      { szBase = szBare[..^5]; bAnim = true;  }
                    else if (szBare.EndsWith("_still", StringComparison.OrdinalIgnoreCase)) { szBase = szBare[..^6]; bAnim = false; }
                    else continue;

                    var szUrl = $"/{GalleryRelDir}/{szCat}/{Path.GetFileName(szFile)}";
                    groups.TryGetValue(szBase, out var pair);
                    groups[szBase] = bAnim ? (szUrl, pair.still) : (pair.anim, szUrl);
                }

                foreach (var kv in groups)
                {
                    if (kv.Value.anim == null && kv.Value.still == null) continue;
                    result.Add(new
                    {
                        szCategory = szCat,
                        szName     = kv.Key,
                        szAnimSrc  = kv.Value.anim  ?? kv.Value.still,   // 缺動畫圖時退靜止圖
                        szStillSrc = kv.Value.still ?? kv.Value.anim
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetGallery 掃描失敗");
        }
        return Json(result);
    }
}
