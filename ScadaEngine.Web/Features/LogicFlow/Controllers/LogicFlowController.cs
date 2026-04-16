using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Services;
using ScadaEngine.Web.Features.LogicFlow.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.LogicFlow.Controllers;

[Authorize]
[Route("[controller]")]
public class LogicFlowController : Controller
{
    private readonly LogicFlowService _service;
    private readonly MqttRealtimeSubscriberService _mqttService;
    private readonly CSharpAlgorithmService _csharpAlgoService;
    private readonly ILogger<LogicFlowController> _logger;

    public LogicFlowController(
        LogicFlowService service,
        MqttRealtimeSubscriberService mqttService,
        CSharpAlgorithmService csharpAlgoService,
        ILogger<LogicFlowController> logger)
    {
        _service = service;
        _mqttService = mqttService;
        _csharpAlgoService = csharpAlgoService;
        _logger = logger;
    }

    [HttpGet("/LogicFlow")]
    public IActionResult Index()
    {
        ViewData["Title"] = "流程圖控制";
        return View();
    }

    // ============ Tree API ============

    [HttpGet("api/tree")]
    public async Task<IActionResult> GetTree()
    {
        var nodes = await _service.GetAllNodesAsync();
        return Ok(nodes);
    }

    [HttpPost("api/tree")]
    public async Task<IActionResult> CreateNode([FromBody] CreateNodeDto dto)
    {
        var nId = await _service.CreateNodeAsync(dto.ParentId, dto.Name, dto.NodeType, dto.SortOrder);
        return Ok(new { id = nId });
    }

    [HttpPut("api/tree/{nId}/rename")]
    public async Task<IActionResult> RenameNode(int nId, [FromBody] RenameNodeDto dto)
    {
        var ok = await _service.RenameNodeAsync(nId, dto.Name);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("api/tree/{nId}")]
    public async Task<IActionResult> DeleteNode(int nId)
    {
        var ok = await _service.DeleteNodeAsync(nId);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    [HttpPut("api/tree/{nId}/toggle")]
    public async Task<IActionResult> ToggleEnabled(int nId, [FromBody] ToggleEnabledDto dto)
    {
        var ok = await _service.ToggleEnabledAsync(nId, dto.IsEnabled);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    [HttpPut("api/tree/sort")]
    public async Task<IActionResult> UpdateSortOrder([FromBody] List<SortOrderDto> dtoList)
    {
        var sortList = dtoList.Select(d => (d.Id, d.SortOrder));
        var ok = await _service.UpdateSortOrderAsync(sortList);
        return ok ? Ok(new { success = true }) : StatusCode(500);
    }

    // ============ Diagram API ============

    [HttpGet("api/diagram/{nTreeId}")]
    public async Task<IActionResult> GetDiagram(int nTreeId)
    {
        var diagram = await _service.GetDiagramAsync(nTreeId);
        if (diagram == null) return NotFound();
        return Ok(diagram);
    }

    [HttpPut("api/diagram/{nTreeId}")]
    public async Task<IActionResult> SaveDiagram(int nTreeId, [FromBody] SaveDiagramDto dto)
    {
        var ok = await _service.SaveDiagramAsync(nTreeId, dto.DiagramJson, dto.Version);
        if (!ok) return Conflict(new { success = false, message = "版本衝突，請重新載入" });
        return Ok(new { success = true });
    }

    // ============ Timer State API ============

    /// <summary>取得指定邏輯的 TP 計時器狀態（由 Engine 透過 MQTT 同步）</summary>
    [HttpGet("api/timer-state/{nTreeId}")]
    public IActionResult GetTimerState(int nTreeId)
    {
        var states = _mqttService.GetTimerStates(nTreeId);
        return Ok(states);
    }

    // ============ Algorithm Eval API (前端預覽用，代理呼叫 Python FastAPI) ============

    private static readonly HttpClient _algoHttpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8100"),
        Timeout = TimeSpan.FromSeconds(3)
    };

    [HttpPost("api/algo-eval/{szAlgoName}")]
    public async Task<IActionResult> EvalAlgorithm(string szAlgoName, [FromBody] Dictionary<string, double> inputs)
    {
        // ★ 優先嘗試 C# 演算法（同步 in-process，零延遲）
        if (_csharpAlgoService.TryEvaluate(szAlgoName, inputs, out var csResult))
        {
            return Ok(new { result = csResult, quality = "Good" });
        }

        // ★ 退回 Python FastAPI
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { inputs });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await _algoHttpClient.PostAsync($"/algorithms/{szAlgoName}/evaluate", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            return StatusCode((int)response.StatusCode);
        }
        catch
        {
            return StatusCode(503, new { error = "Algorithm service unavailable" });
        }
    }

    // ============ Algorithm API ============

    /// <summary>掃描 Engine/Algorithms/ 資料夾及子資料夾，回傳可用的演算法清單（.py + .cs）</summary>
    [HttpGet("api/algorithms")]
    public IActionResult GetAlgorithms()
    {
        var szAlgoDir = ResolveAlgorithmsDir();
        if (szAlgoDir == null)
            return Ok(Array.Empty<object>());

        var algorithms = new List<object>();

        // 掃描 .py（排除 main.py / __*.py）
        foreach (var szFilePath in Directory.GetFiles(szAlgoDir, "*.py", SearchOption.AllDirectories))
        {
            var szFileName = Path.GetFileName(szFilePath);
            if (szFileName == "main.py" || szFileName.StartsWith("__")) continue;
            algorithms.Add(ParseAlgorithmMetadata(szFilePath, szAlgoDir, "#", "python"));
        }

        // 掃描 .cs
        foreach (var szFilePath in Directory.GetFiles(szAlgoDir, "*.cs", SearchOption.AllDirectories))
        {
            algorithms.Add(ParseAlgorithmMetadata(szFilePath, szAlgoDir, "//", "csharp"));
        }

        return Ok(algorithms);
    }

    /// <summary>通用 metadata 解析：支援 # (Python) 和 // (C#) 前綴</summary>
    private static object ParseAlgorithmMetadata(string szFilePath, string szAlgoDir, string szPrefix, string szLanguage)
    {
        var szName = Path.GetFileNameWithoutExtension(szFilePath);
        var szLabel = szName;
        var inputs = new List<string> { "in" };
        var outputs = new List<string> { "out" };
        var szDescription = "";

        var szRelDir = Path.GetDirectoryName(szFilePath)!;
        var szGroup = szRelDir == szAlgoDir ? "" : Path.GetFileName(szRelDir);

        var lines = System.IO.File.ReadLines(szFilePath).Take(15);
        var szAlgo = $"{szPrefix} @algorithm:";
        var szIn = $"{szPrefix} @inputs:";
        var szOut = $"{szPrefix} @outputs:";
        var szDesc = $"{szPrefix} @description:";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(szAlgo))
                szLabel = trimmed[szAlgo.Length..].Trim();
            else if (trimmed.StartsWith(szIn))
                inputs = trimmed[szIn.Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (trimmed.StartsWith(szOut))
                outputs = trimmed[szOut.Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (trimmed.StartsWith(szDesc))
                szDescription = trimmed[szDesc.Length..].Trim();
        }

        return new { name = szName, label = szLabel, group = szGroup, inputs, outputs, description = szDescription, language = szLanguage };
    }

    /// <summary>依序探測多個候選路徑，回傳第一個存在的 Algorithms 資料夾（開發 + 部署皆適用）</summary>
    private static string? ResolveAlgorithmsDir()
    {
        var candidates = new[]
        {
            // 開發環境：Web 與 Engine 平行放在 solution 根目錄下
            Path.Combine(Directory.GetCurrentDirectory(), "..", "ScadaEngine.Engine", "Algorithms"),
            // 部署環境：Web 在 C:\SCADA\Web\App，Engine 在 C:\SCADA\Engine\App
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Engine", "App", "Algorithms"),
            // 部署環境（同層）：Algorithms 直接放在 Web\App\Algorithms
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Algorithms"),
        };

        foreach (var szPath in candidates)
        {
            var szFull = Path.GetFullPath(szPath);
            if (Directory.Exists(szFull))
                return szFull;
        }
        return null;
    }
}
