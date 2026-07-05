using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScadaEngine.Web.Features.ModbusCoordinator.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 讀寫 Engine 執行目錄下的 Modbus JSON 設定檔（點位熱編輯）。
///
/// - 只准「原地編輯」點位欄位（Name / Address / DataType / Ratio / Unit / Min / Max），
///   點位數量、順序與設備層欄位（IP / Port / ModbusId / ConnectTimeout）一律鎖死 —
///   SID 由陣列索引產生、控制指令用 TagIndex 定位，結構一變就是歷史資料錯位 + 控制寫錯暫存器。
///   DataType 限 Engine 支援的型態白名單（影響暫存器讀取長度與控制轉換）。
/// - 原子寫檔：先寫 *.json.tmp（不符 Engine watcher 的 *.json filter）再 File.Replace 替換，
///   確保控制路徑任何瞬間讀到的都是完整舊檔或完整新檔；並保留 *.json.bak 備份。
/// - 保留原檔編碼（現場檔案為 UTF-16 LE with BOM，工具產生）。
/// - MirrorFolder（可選，dev 用）：同步寫回原始碼資料夾，避免 rebuild 後設定倒退。
/// </summary>
public class ModbusConfigFileService
{
    private readonly ILogger<ModbusConfigFileService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    /// <summary>寫檔序列化鎖 — 防止兩個 Admin 同時存檔互相覆蓋</summary>
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Engine 支援的資料型態 — 對應 ModbusTagModel.ParseRatioAndRegisterCount / CalculatePhysicalValue 的 switch case
    /// （Engine 端比對時 ToUpper，故此處以大寫正規形比對；UI 下拉同此清單）
    /// </summary>
    public static readonly string[] SupportedDataTypes =
    {
        "INTEGER", "UINTEGER", "FLOATINGPT", "SWAPPEDFP", "DOUBLE", "SWAPPEDDOUBLE", "UINT32BE",
    };

    public ModbusConfigFileService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<ModbusConfigFileService> logger)
    {
        _logger = logger;
        _configuration = configuration;
        _env = env;
    }

    /// <summary>
    /// 每次呼叫即時解析監控資料夾 — 不在建構子快取，appsettings.json 熱重載（reloadOnChange）改路徑即生效，
    /// 免重啟也不會殘留舊路徑。相對路徑以 Web ContentRoot 為基準；找不到設定回傳 null。
    /// </summary>
    private string? GetWatchedFolder()
    {
        var szWatched = _configuration["EngineModbusConfig:WatchedFolder"];
        return string.IsNullOrWhiteSpace(szWatched)
            ? null
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, szWatched));
    }

    /// <summary>每次呼叫即時解析鏡像資料夾（可選，dev 用）</summary>
    private string? GetMirrorFolder()
    {
        var szMirror = _configuration["EngineModbusConfig:MirrorFolder"];
        return string.IsNullOrWhiteSpace(szMirror)
            ? null
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, szMirror));
    }

    /// <summary>
    /// 讀取指定 Coordinator（= JSON 檔名，不含副檔名）的點位清單，檔案不存在回傳 null
    /// </summary>
    public async Task<ModbusPointsFileModel?> GetPointsAsync(string szCoordinatorName)
    {
        var szFilePath = ResolveConfigFilePath(szCoordinatorName);
        if (szFilePath == null || !File.Exists(szFilePath))
            return null;

        var (szJson, _) = await ReadAllTextDetectEncodingAsync(szFilePath);
        var root = JsonNode.Parse(szJson);
        if (root == null) return null;

        var model = new ModbusPointsFileModel
        {
            CoordinatorName = szCoordinatorName,
            IP = root["IP"]?.ToString() ?? string.Empty,
            Port = int.TryParse(root["Port"]?.ToString(), out var nPort) ? nPort : 502,
            ModbusId = root["ModbusId"]?.ToString() ?? string.Empty,
            ConnectTimeout = int.TryParse(root["ConnectTimeout"]?.ToString(), out var nTimeout) ? nTimeout : 1000,
        };

        if (root["Tags"] is JsonArray tags)
        {
            foreach (var tag in tags)
            {
                if (tag == null) continue;
                model.Points.Add(new ModbusPointDto
                {
                    Name = tag["Name"]?.ToString() ?? string.Empty,
                    Address = tag["Address"]?.ToString() ?? string.Empty,
                    DataType = tag["DataType"]?.ToString() ?? string.Empty,
                    Ratio = tag["Ratio"]?.ToString() ?? "1",
                    Unit = tag["Unit"]?.ToString() ?? string.Empty,
                    Min = tag["Min"]?.ToString() ?? string.Empty,
                    Max = tag["Max"]?.ToString() ?? string.Empty,
                });
            }
        }

        return model;
    }

    /// <summary>
    /// 原地更新點位欄位並原子寫回。存檔前重讀原檔驗證結構（數量、DataType）未變，不合即拒絕。
    /// 無任何欄位變更時不寫檔（不觸發 Engine 重載）。
    /// </summary>
    public async Task<ModbusPointsUpdateResult> UpdatePointsAsync(string szCoordinatorName, List<ModbusPointDto> newPoints)
    {
        var result = new ModbusPointsUpdateResult();

        var szFilePath = ResolveConfigFilePath(szCoordinatorName);
        if (szFilePath == null || !File.Exists(szFilePath))
        {
            result.nError = ModbusPointsUpdateError.FileNotFound;
            return result;
        }

        await _writeLock.WaitAsync();
        try
        {
            // 重讀原檔 — 以檔案現況為準驗證結構
            var (szJson, encoding) = await ReadAllTextDetectEncodingAsync(szFilePath);
            var root = JsonNode.Parse(szJson);
            if (root == null || root["Tags"] is not JsonArray tags)
            {
                result.nError = ModbusPointsUpdateError.FileNotFound;
                return result;
            }

            // 結構鎖：點位數量必須一致（禁止增刪與排序）
            if (newPoints.Count != tags.Count)
            {
                result.nError = ModbusPointsUpdateError.StructureChanged;
                return result;
            }

            // 逐點驗證 + 計算變更
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i]!;
                var p = newPoints[i];

                var szError = ValidatePointFields(p);
                if (szError != null)
                {
                    result.nError = ModbusPointsUpdateError.InvalidPoint;
                    result.nInvalidRow = i + 1;
                    result.szInvalidReason = szError;
                    return result;
                }

                var szSummary = BuildChangeSummary(tag, p);
                if (szSummary.Length > 0)
                {
                    result.changes.Add(new ModbusPointChange
                    {
                        nTagIndex = i,
                        szPointName = p.Name.Trim(),
                        szSummary = szSummary,
                    });
                }
            }

            // 無變更 → 不寫檔、不觸發重載
            if (result.changes.Count == 0)
            {
                result.isSuccess = true;
                return result;
            }

            // 套用變更（只動 Tags[i] 的可編輯欄位，其他內容原樣保留）
            foreach (var change in result.changes)
            {
                var tag = tags[change.nTagIndex]!;
                var p = newPoints[change.nTagIndex];
                tag["Name"] = p.Name.Trim();
                tag["Address"] = p.Address.Trim();
                tag["DataType"] = (p.DataType ?? string.Empty).Trim();
                tag["Ratio"] = (p.Ratio ?? "1").Trim();
                tag["Unit"] = (p.Unit ?? string.Empty).Trim();
                tag["Min"] = (p.Min ?? string.Empty).Trim();
                tag["Max"] = (p.Max ?? string.Empty).Trim();
            }

            var szNewJson = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            AtomicWrite(szFilePath, szNewJson, encoding);
            MirrorWrite(szCoordinatorName, szNewJson, encoding);

            result.isSuccess = true;
            _logger.LogInformation("Modbus 點位設定已更新: {File}, 變更 {Count} 點", szFilePath, result.changes.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 Modbus 點位設定失敗: {Name}", szCoordinatorName);
            result.nError = ModbusPointsUpdateError.WriteFailed;
            result.changes.Clear();
            return result;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 驗證 Modbus 位址格式 — 與 Engine ModbusTagModel.ParseAddress 相同：
    /// 5 位數慣例（0xxxx Coil 1-9999、1xxxx Discrete、3xxxx Input、4xxxx Holding）
    /// 或 6 位數擴充慣例（000001-065536 / 1xxxxx / 3xxxxx / 4xxxxx，offset 上限 65535）。
    /// 兩慣例數值範圍重疊但意義不同，靠字串長度（含前導 0）區分。
    /// </summary>
    public static bool IsValidAddress(string? szAddress)
    {
        if (string.IsNullOrWhiteSpace(szAddress))
            return false;

        var sz = szAddress.Trim();
        if (sz.Length > 6 || !sz.All(char.IsDigit) || !int.TryParse(sz, out var n))
            return false;

        if (sz.Length == 6)
        {
            return (n >= 1 && n <= 65536)
                || (n >= 100001 && n <= 165536)
                || (n >= 300001 && n <= 365536)
                || (n >= 400001 && n <= 465536);
        }

        return (n >= 1 && n <= 9999)
            || (n >= 10000 && n <= 19999)
            || (n >= 30000 && n <= 39999)
            || (n >= 40000 && n <= 49999);
    }

    /// <summary>驗證單點可編輯欄位，回傳 null 表示合法，否則回傳原因（技術描述）</summary>
    private static string? ValidatePointFields(ModbusPointDto p)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
            return "Name is required";

        if (!IsValidAddress(p.Address))
            return "invalid Address";

        if (!SupportedDataTypes.Contains((p.DataType ?? string.Empty).Trim().ToUpperInvariant()))
            return "unsupported DataType";

        if (!float.TryParse((p.Ratio ?? "1").Trim(), out _))
            return "Ratio must be numeric";

        if (!string.IsNullOrWhiteSpace(p.Min) && !float.TryParse(p.Min.Trim(), out _))
            return "Min must be numeric";

        if (!string.IsNullOrWhiteSpace(p.Max) && !float.TryParse(p.Max.Trim(), out _))
            return "Max must be numeric";

        return null;
    }

    /// <summary>比對舊(檔案) / 新(請求) 欄位，產出「欄位: 舊 → 新」摘要；無變更回傳空字串</summary>
    private static string BuildChangeSummary(JsonNode tag, ModbusPointDto p)
    {
        var aDiffs = new List<string>();

        void Compare(string szField, string szNewValue)
        {
            var szOld = tag[szField]?.ToString() ?? string.Empty;
            if (!string.Equals(szOld, szNewValue, StringComparison.Ordinal))
                aDiffs.Add($"{szField}: {szOld} → {szNewValue}");
        }

        Compare("Name", p.Name.Trim());
        Compare("Address", p.Address.Trim());
        Compare("DataType", (p.DataType ?? string.Empty).Trim());
        Compare("Ratio", (p.Ratio ?? "1").Trim());
        Compare("Unit", (p.Unit ?? string.Empty).Trim());
        Compare("Min", (p.Min ?? string.Empty).Trim());
        Compare("Max", (p.Max ?? string.Empty).Trim());

        return string.Join(", ", aDiffs);
    }

    /// <summary>
    /// 解析 Coordinator 名稱為監控資料夾內的檔案路徑；名稱含路徑字元或逸出資料夾一律回 null
    /// </summary>
    private string? ResolveConfigFilePath(string szCoordinatorName)
    {
        var szWatchedFolder = GetWatchedFolder();
        if (szWatchedFolder == null || string.IsNullOrWhiteSpace(szCoordinatorName))
            return null;

        if (szCoordinatorName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || szCoordinatorName.Contains(".."))
            return null;

        var szFullPath = Path.GetFullPath(Path.Combine(szWatchedFolder, szCoordinatorName + ".json"));

        // 防路徑逸出
        if (!szFullPath.StartsWith(szWatchedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        return szFullPath;
    }

    /// <summary>讀檔並偵測 BOM 編碼（無 BOM 視為 UTF-8）</summary>
    private static async Task<(string szText, Encoding encoding)> ReadAllTextDetectEncodingAsync(string szFilePath)
    {
        using var reader = new StreamReader(szFilePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var szText = await reader.ReadToEndAsync();
        return (szText, reader.CurrentEncoding);
    }

    /// <summary>
    /// 原子寫檔：寫 *.json.tmp（不觸發 Engine watcher）→ File.Replace 原子替換 → 留 *.json.bak 備份。
    /// 控制路徑每筆指令都直接讀 JSON 且失敗不重試，半份 JSON 會讓控制無聲失敗 — 原子替換杜絕此窗口。
    /// </summary>
    private static void AtomicWrite(string szFilePath, string szContent, Encoding encoding)
    {
        var szTmpPath = szFilePath + ".tmp";
        var szBakPath = szFilePath + ".bak";

        File.WriteAllText(szTmpPath, szContent, encoding);
        File.Replace(szTmpPath, szFilePath, szBakPath, ignoreMetadataErrors: true);
    }

    /// <summary>dev 環境鏡像寫回原始碼資料夾（失敗僅記 log，不影響主寫入）</summary>
    private void MirrorWrite(string szCoordinatorName, string szContent, Encoding encoding)
    {
        var szMirrorFolder = GetMirrorFolder();
        if (szMirrorFolder == null) return;

        try
        {
            var szMirrorPath = Path.Combine(szMirrorFolder, szCoordinatorName + ".json");
            if (!Directory.Exists(szMirrorFolder)) return;

            File.WriteAllText(szMirrorPath, szContent, encoding);
            _logger.LogInformation("Modbus 點位設定已鏡像寫回: {File}", szMirrorPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "鏡像寫回失敗（不影響主寫入）: {Name}", szCoordinatorName);
        }
    }
}
