using System.Text.Json;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 載入 `OpcUaPoint/*.json` 並 UPSERT 到 OpcUaCoordinator + OpcUaPoints。
/// 一檔 = 一個 OPC UA Server（Coordinator），檔內含多個 Device 分組，點位數不設上限。
/// SID = OPC{CoordinatorId}-S{Seq}，Seq 由 Web 配號並持久化於 JSON（刪除不回收，保 SID 穩定）。
/// </summary>
public class OpcUaConfigLoader
{
    private readonly ILogger<OpcUaConfigLoader> _logger;
    private readonly IDataRepository _repository;
    private readonly string _szConfigFolder;

    public OpcUaConfigLoader(ILogger<OpcUaConfigLoader> logger, IDataRepository repository)
    {
        _logger = logger;
        _repository = repository;
        _szConfigFolder = ResolveConfigFolder();
    }

    /// <summary>
    /// 設定資料夾解析：
    /// 開發環境（dotnet run，CurrentDirectory = 專案根）優先用專案根的 OpcUaPoint —
    /// 這是 Web 動態回寫的目標（source of truth），確保 Web 存檔 → reload 即生效，不需重建。
    /// Windows Service / 部署環境（CurrentDirectory 無 csproj）用 BaseDirectory/OpcUaPoint。
    /// </summary>
    private static string ResolveConfigFolder()
    {
        var szCurrentDir = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(szCurrentDir, "ScadaEngine.Engine.csproj")))
            return Path.Combine(szCurrentDir, "OpcUaPoint");

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpcUaPoint");
    }

    /// <summary>
    /// 掃描 OpcUaPoint/*.json，UPSERT 到 DB；回傳已載入的 Coordinator + 點位清單。
    /// 任一檔案格式錯誤只 log warning + skip，不擋住其他檔案。
    /// </summary>
    public async Task<List<OpcUaCoordinatorLoaded>> LoadAllAsync()
    {
        var result = new List<OpcUaCoordinatorLoaded>();

        if (!Directory.Exists(_szConfigFolder))
        {
            _logger.LogInformation("OPC UA 來源設定資料夾不存在: {Folder}（將跳過 OPC UA 來源載入）", _szConfigFolder);
            return result;
        }

        var jsonFiles = Directory.GetFiles(_szConfigFolder, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0)
        {
            _logger.LogInformation("OpcUaPoint/ 下未找到任何 JSON 檔");
            return result;
        }

        foreach (var szPath in jsonFiles)
        {
            try
            {
                var loaded = await LoadOneAsync(szPath);
                if (loaded != null)
                    result.Add(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "載入 OPC UA 來源 JSON 失敗: {Path}", szPath);
            }
        }

        _logger.LogInformation("OPC UA 來源載入完成: {Count} 個 Coordinator", result.Count);
        return result;
    }

    private async Task<OpcUaCoordinatorLoaded?> LoadOneAsync(string szPath)
    {
        var szJson = await File.ReadAllTextAsync(szPath);
        if (string.IsNullOrWhiteSpace(szJson))
        {
            _logger.LogWarning("JSON 內容為空: {Path}", szPath);
            return null;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        OpcUaCoordinatorJsonDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OpcUaCoordinatorJsonDto>(szJson, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON 格式錯誤: {Path}", szPath);
            return null;
        }

        if (dto == null) return null;
        if (string.IsNullOrWhiteSpace(dto.Name))
            dto.Name = Path.GetFileNameWithoutExtension(szPath);

        var coordinator = new OpcUaCoordinatorModel
        {
            szName = dto.Name!,
            szEndpointUrl = dto.EndpointUrl ?? string.Empty,
            szUsername = dto.Username ?? string.Empty,
            szPassword = dto.Password ?? string.Empty,
            nPollingInterval = dto.PollingInterval > 0 ? dto.PollingInterval : 1000,
            nConnectTimeout = dto.ConnectTimeout > 0 ? dto.ConnectTimeout : 5000,
            isMonitorEnabled = dto.MonitorEnabled
        };

        if (!coordinator.Validate())
        {
            _logger.LogWarning("OPC UA Coordinator 驗證失敗（Name/EndpointUrl 必填），跳過: {Path}", szPath);
            return null;
        }

        // UPSERT Coordinator → 拿到 Id（SID 前綴依賴）
        var nId = await _repository.SaveOpcUaCoordinatorAsync(coordinator);
        coordinator.Id = nId;

        // 攤平 Devices → 點位清單；Seq 採 JSON 持久化值，缺漏/重複者補號（max+1）
        var pointList = new List<OpcUaPointModel>();
        var usedSeq = new HashSet<int>();
        var nMaxSeq = 0;

        if (dto.Devices != null)
        {
            // 第一輪：登記已有的合法 Seq
            foreach (var device in dto.Devices)
            {
                if (device.Tags == null) continue;
                foreach (var tag in device.Tags)
                {
                    if (tag.Seq > 0 && usedSeq.Add(tag.Seq))
                        nMaxSeq = Math.Max(nMaxSeq, tag.Seq);
                }
            }

            foreach (var device in dto.Devices)
            {
                if (device.Tags == null) continue;
                var szDeviceName = device.Name ?? string.Empty;

                foreach (var tag in device.Tags)
                {
                    var nSeq = tag.Seq;
                    if (nSeq <= 0)
                    {
                        // 手寫檔漏 Seq → 補號（Web 產生的檔一律有 Seq）
                        nSeq = ++nMaxSeq;
                        usedSeq.Add(nSeq);
                        _logger.LogWarning("Coordinator {Name} 點位 {Point} 缺 Seq，自動補為 {Seq}",
                            coordinator.szName, tag.Name, nSeq);
                    }

                    var fRatio = ParseRatio(tag.Ratio);
                    if (fRatio == 0f)
                    {
                        _logger.LogWarning("Coordinator {Name} 點位 {Point} Ratio=0 非法，改用 1.0",
                            coordinator.szName, tag.Name);
                        fRatio = 1.0f;
                    }

                    pointList.Add(new OpcUaPointModel
                    {
                        szSID = $"OPC{nId}-S{nSeq}",
                        nCoordinatorId = nId,
                        szDeviceName = szDeviceName,
                        nSequence = nSeq,
                        szName = tag.Name ?? string.Empty,
                        szTagName = tag.TagName ?? string.Empty,
                        szControlType = NormalizeControlType(tag.ControlType),
                        fRatio = fRatio,
                        szUnit = tag.Unit ?? string.Empty,
                        fMin = tag.Min,
                        fMax = tag.Max
                    });
                }
            }
        }

        // 同檔內重複 Seq → 後者跳過（SID 衝突保護）
        var dedupList = new List<OpcUaPointModel>();
        var seenSid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pointList)
        {
            if (!seenSid.Add(p.szSID))
            {
                _logger.LogWarning("Coordinator {Name} 出現重複 SID {SID}（Seq 重複），已跳過後者",
                    coordinator.szName, p.szSID);
                continue;
            }
            dedupList.Add(p);
        }

        await _repository.SaveOpcUaPointsAsync(nId, dedupList);

        _logger.LogInformation("OPC UA 來源載入: {Name} (Id={Id}), Device {DeviceCount} 組, 點位 {Count}",
            coordinator.szName, nId, dto.Devices?.Count ?? 0, dedupList.Count);

        return new OpcUaCoordinatorLoaded
        {
            Coordinator = coordinator,
            Points = dedupList,
            nMaxNodesPerReadFallback = dto.MaxNodesPerRead > 0 ? dto.MaxNodesPerRead : 500
        };
    }

    /// <summary>
    /// Ratio 兼容字串（"0.1"，比照 Modbus 慣例）與數字（0.1）兩種寫法；缺漏 = 1.0
    /// </summary>
    private static float ParseRatio(JsonElement ratio)
    {
        switch (ratio.ValueKind)
        {
            case JsonValueKind.Number:
                return ratio.GetSingle();
            case JsonValueKind.String:
                return float.TryParse(ratio.GetString(), out var f) ? f : 1.0f;
            default:
                return 1.0f;
        }
    }

    private static string NormalizeControlType(string? szControlType)
    {
        var sz = (szControlType ?? string.Empty).Trim().ToUpperInvariant();
        return sz is "AO" or "DO" ? sz : string.Empty;
    }
}

/// <summary>
/// 一個 Coordinator 連同其所有點位（給 OpcUaCommunicationService 啟動 polling 用）
/// </summary>
public class OpcUaCoordinatorLoaded
{
    public OpcUaCoordinatorModel Coordinator { get; set; } = new();
    public List<OpcUaPointModel> Points { get; set; } = new();

    /// <summary>
    /// 讀不到 Server OperationLimits.MaxNodesPerRead 時的分塊 fallback（JSON 可覆寫，預設 500）
    /// </summary>
    public int nMaxNodesPerReadFallback { get; set; } = 500;
}

/// <summary>
/// OpcUaPoint/*.json 反序列化 DTO — 一檔一 Server，Devices 分組
/// </summary>
internal class OpcUaCoordinatorJsonDto
{
    public string? Name { get; set; }
    public string? EndpointUrl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int PollingInterval { get; set; } = 1000;
    public int ConnectTimeout { get; set; } = 5000;
    public bool MonitorEnabled { get; set; } = true;

    /// <summary>選填：讀不到 Server OperationLimits 時的分塊 fallback</summary>
    public int MaxNodesPerRead { get; set; } = 0;

    public List<OpcUaDeviceJsonDto>? Devices { get; set; }
}

internal class OpcUaDeviceJsonDto
{
    public string? Name { get; set; }
    public List<OpcUaTagJsonDto>? Tags { get; set; }
}

internal class OpcUaTagJsonDto
{
    /// <summary>由 Web 配號並持久化（同 Coordinator 遞增、刪除不回收）</summary>
    public int Seq { get; set; }
    public string? Name { get; set; }

    /// <summary>OPC UA NodeId 字串，例 ns=2;s=D1.T</summary>
    public string? TagName { get; set; }

    /// <summary>''=唯讀 / 'AO' / 'DO'</summary>
    public string? ControlType { get; set; }

    /// <summary>兼容字串 "0.1"（比照 Modbus）與數字 0.1</summary>
    public System.Text.Json.JsonElement Ratio { get; set; }

    public string? Unit { get; set; }
    public float? Min { get; set; }
    public float? Max { get; set; }
}
