using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;
using ScadaEngine.Web.Features.DbCoordinator.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// DB 來源點位名稱熱編輯服務 — 讀原 DBPoint/{Name}.json → 只改該點 Name → 原子寫回
/// （Watched + Mirror 雙寫）→ 以 JSON 為準 UPSERT DBPoints，之後由 Controller 發 MQTT reload。
/// JSON 為設定源、DB 為執行期快照（比照 OpcUaCoordinatorService，見 docs/plans 決策 2）。
///
/// 只開放改 Name：SID = DB{Id}-S{陣列索引+1}，增刪或重排會使後續 SID 位移、歷史資料錯接，
/// 故點位順序與數量一律不動。
///
/// 寫檔路徑由 appsettings.json 的 EngineDbPointConfig 明定（比照 EngineModbusConfig / EngineOpcUaConfig）：
/// WatchedFolder = Engine 實際讀取的部署資料夾；MirrorFolder（可選，dev 用）鏡像寫回原始碼資料夾。
/// 原子寫檔（*.json.tmp → File.Replace → 留 *.json.bak）。
/// 現檔可能是 Excel 巨集產的 UTF-16 LE BOM，讀取依 BOM 自動偵測；寫回統一 UTF-8（Engine 兩種都讀得動）。
/// </summary>
public class DbPointConfigFileService
{
    private readonly IDataRepository _repository;
    private readonly ILogger<DbPointConfigFileService> _logger;
    private readonly IStringLocalizer<DbPointConfigFileService> _l;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    /// <summary>JSON 檔寫入互斥（static — Scoped service 跨請求共用）</summary>
    private static readonly SemaphoreSlim _fileGate = new(1, 1);

    private static readonly JsonSerializerOptions _jsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public DbPointConfigFileService(
        IDataRepository repository,
        ILogger<DbPointConfigFileService> logger,
        IStringLocalizer<DbPointConfigFileService> localizer,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _repository = repository;
        _logger = logger;
        _l = localizer;
        _configuration = configuration;
        _env = env;
    }

    /// <summary>
    /// 每次呼叫即時解析監控資料夾（Engine 實際讀取的 DBPoint 位置）— 不在建構子快取，
    /// appsettings.json 熱重載改路徑即生效。相對路徑以 Web ContentRoot 為基準；未設定回傳 null（呼叫端明確報錯）。
    /// 比照 ModbusConfigFileService 的 EngineModbusConfig 慣例，禁止猜測式 fallback。
    /// </summary>
    private string? GetWatchedFolder()
    {
        var szWatched = _configuration["EngineDbPointConfig:WatchedFolder"];
        return string.IsNullOrWhiteSpace(szWatched)
            ? null
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, szWatched));
    }

    /// <summary>每次呼叫即時解析鏡像資料夾（可選，dev 用 — 同步寫回原始碼資料夾避免 rebuild 後設定倒退）</summary>
    private string? GetMirrorFolder()
    {
        var szMirror = _configuration["EngineDbPointConfig:MirrorFolder"];
        return string.IsNullOrWhiteSpace(szMirror)
            ? null
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, szMirror));
    }

    /// <summary>
    /// 更新單一點位（名稱 + 單位）：改 JSON（不動陣列結構）→ 原子雙寫 → 以 JSON 為準 UPSERT DBPoints。
    /// 單位可空白；自動帶入類功能（如電表設定篩 kWh）依 DBPoints.Unit 運作。
    /// </summary>
    public async Task<(bool isSuccess, string szMessage)> UpdatePointAsync(int nCoordinatorId, int nSequence, string szNewName, string szNewUnit)
    {
        szNewName = (szNewName ?? string.Empty).Trim();
        szNewUnit = (szNewUnit ?? string.Empty).Trim();
        if (szNewName.Length == 0)
            return (false, _l["dbpointcfg.svc.name_required"].Value);
        if (szNewName.Length > 100)
            return (false, _l["dbpointcfg.svc.name_too_long"].Value);
        if (szNewUnit.Length > 50)
            return (false, _l["dbpointcfg.svc.unit_too_long"].Value);
        if (nSequence < 1 || nSequence > 100)
            return (false, _l["dbpointcfg.svc.sequence_invalid"].Value);

        var coordinators = (await _repository.GetAllDbCoordinatorsAsync()).ToList();
        var coordinator = coordinators.FirstOrDefault(c => c.Id == nCoordinatorId);
        if (coordinator == null)
            return (false, _l["dbpointcfg.svc.coordinator_not_found"].Value);

        await _fileGate.WaitAsync();
        try
        {
            var szFolder = GetWatchedFolder();
            if (szFolder == null)
                return (false, _l["dbpointcfg.svc.folder_not_configured"].Value);

            var szPath = Path.Combine(szFolder, coordinator.szName + ".json");
            if (!File.Exists(szPath))
                return (false, _l["dbpointcfg.svc.json_not_found", szPath].Value);

            // File.ReadAllTextAsync 依 BOM 自動偵測編碼（Excel 巨集產出為 UTF-16 LE BOM）
            var szJson = await File.ReadAllTextAsync(szPath);
            DbPointJsonFile? file;
            try
            {
                file = JsonSerializer.Deserialize<DbPointJsonFile>(szJson, _jsonReadOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "DBPoint JSON 格式錯誤: {Path}", szPath);
                return (false, _l["dbpointcfg.svc.json_invalid"].Value);
            }

            if (file?.Points == null || nSequence > file.Points.Count)
                return (false, _l["dbpointcfg.svc.point_not_found", nSequence].Value);

            // Sequence = 陣列索引+1（與 Engine DbCoordinatorJsonLoader 對齊），只改 Name/Unit 不動結構
            file.Points[nSequence - 1].Name = szNewName;
            file.Points[nSequence - 1].Unit = szNewUnit;

            await WriteJsonFileAsync(szPath, file);

            // 以剛寫回的 JSON 為準重建 DBPoints（走既有 SaveDbPointsAsync，DELETE+INSERT 冪等；
            // 映射邏輯與 Engine 載入器一致：Sequence = 索引+1、上限 100 點）
            var pointModels = new List<DbPointModel>();
            for (int i = 0; i < file.Points.Count && i < 100; i++)
            {
                var pt = file.Points[i];
                pointModels.Add(new DbPointModel
                {
                    szSID = $"DB{nCoordinatorId}-S{i + 1}",
                    nCoordinatorId = nCoordinatorId,
                    nSequence = i + 1,
                    szName = pt.Name ?? string.Empty,
                    szUnit = pt.Unit ?? string.Empty,
                    fMin = pt.Min,
                    fMax = pt.Max
                });
            }
            await _repository.SaveDbPointsAsync(nCoordinatorId, pointModels);

            _logger.LogInformation("DB 來源點位已更新: {Coordinator} S{Seq} → Name={NewName}, Unit={NewUnit}",
                coordinator.szName, nSequence, szNewName, szNewUnit);
            return (true, _l["dbpointcfg.svc.save_success"].Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 DB 來源點位失敗: CoordinatorId={Id}, Seq={Seq}", nCoordinatorId, nSequence);
            return (false, _l["dbpointcfg.svc.json_write_failed", ex.Message].Value);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// 原子寫檔 + 鏡像寫回（比照 OpcUaCoordinatorService）：
    /// 先寫 *.json.tmp 再 File.Replace 原子替換（留 *.json.bak 備份），
    /// 確保 Engine reload 任何瞬間讀到的都是完整舊檔或完整新檔。
    /// </summary>
    private Task WriteJsonFileAsync(string szPath, DbPointJsonFile file)
    {
        var szJson = JsonSerializer.Serialize(file, _jsonWriteOptions);

        var szTmpPath = szPath + ".tmp";
        File.WriteAllText(szTmpPath, szJson, System.Text.Encoding.UTF8);
        if (File.Exists(szPath))
            File.Replace(szTmpPath, szPath, szPath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(szTmpPath, szPath);

        MirrorWrite(Path.GetFileName(szPath), szJson);
        return Task.CompletedTask;
    }

    /// <summary>dev 環境鏡像寫回原始碼資料夾（失敗僅記 log，不影響主寫入）</summary>
    private void MirrorWrite(string szFileName, string szContent)
    {
        var szMirrorFolder = GetMirrorFolder();
        if (szMirrorFolder == null) return;

        try
        {
            if (!Directory.Exists(szMirrorFolder)) return;
            var szMirrorPath = Path.Combine(szMirrorFolder, szFileName);
            File.WriteAllText(szMirrorPath, szContent, System.Text.Encoding.UTF8);
            _logger.LogInformation("DBPoint 設定已鏡像寫回: {File}", szMirrorPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DBPoint 鏡像寫回失敗（不影響主寫入）: {File}", szFileName);
        }
    }
}
