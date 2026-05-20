using System.Text.Json;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// 載入 `DBPoint/*.json` 並 UPSERT 到 DBCoordinator + DBPoints。
/// SID 由 Coordinator.Id + 點位在 JSON 陣列中的索引+1 動態組合：DB{Id}-S{N}
/// </summary>
public class DbCoordinatorJsonLoader
{
    private readonly ILogger<DbCoordinatorJsonLoader> _logger;
    private readonly IDataRepository _repository;
    private readonly string _szConfigFolder;

    public DbCoordinatorJsonLoader(ILogger<DbCoordinatorJsonLoader> logger, IDataRepository repository)
    {
        _logger = logger;
        _repository = repository;
        _szConfigFolder = ResolveConfigFolder();
    }

    /// <summary>
    /// 一律使用 AppDomain.BaseDirectory/DBPoint。
    /// Windows Service 環境下 CurrentDirectory 預設為 C:\Windows\System32，不可作為 fallback。
    /// 開發環境因 csproj 設 CopyToOutputDirectory=PreserveNewest，BaseDirectory 也會有 DBPoint。
    /// </summary>
    private static string ResolveConfigFolder()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DBPoint");
    }

    /// <summary>
    /// 掃描 DBPoint/*.json，UPSERT 到 DB；回傳已載入的 Coordinator + 點位清單。
    /// 任一檔案格式錯誤只 log warning + skip，不擋住其他檔案。
    /// </summary>
    public async Task<List<DbCoordinatorLoaded>> LoadAllAsync()
    {
        var result = new List<DbCoordinatorLoaded>();

        if (!Directory.Exists(_szConfigFolder))
        {
            _logger.LogWarning("DB 來源設定資料夾不存在: {Folder}（將跳過 DB 來源載入）", _szConfigFolder);
            return result;
        }

        var jsonFiles = Directory.GetFiles(_szConfigFolder, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0)
        {
            _logger.LogInformation("DBPoint/ 下未找到任何 JSON 檔");
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
                _logger.LogError(ex, "載入 DB 來源 JSON 失敗: {Path}", szPath);
            }
        }

        _logger.LogInformation("DB 來源載入完成: {Count} 個 Coordinator", result.Count);
        return result;
    }

    private async Task<DbCoordinatorLoaded?> LoadOneAsync(string szPath)
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

        DbCoordinatorJsonDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DbCoordinatorJsonDto>(szJson, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON 格式錯誤: {Path}", szPath);
            return null;
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
        {
            // 檔名作為 fallback 名稱
            var szFallbackName = Path.GetFileNameWithoutExtension(szPath);
            if (dto == null) return null;
            dto.Name = szFallbackName;
        }

        // UPSERT Coordinator → 拿到 Id
        var coordinator = new DbCoordinatorModel
        {
            szName = dto.Name!,
            nPollingInterval = dto.PollingInterval > 0 ? dto.PollingInterval : 1000,
            nConnectTimeout = dto.ConnectTimeout > 0 ? dto.ConnectTimeout : 1000,
            isMonitorEnabled = dto.MonitorEnabled
        };

        if (!coordinator.Validate())
        {
            _logger.LogWarning("Coordinator 驗證失敗，跳過: {Path}", szPath);
            return null;
        }

        var (nId, nInterval, nTimeout, isMonitor) = await _repository.SaveDbCoordinatorAsync(coordinator);
        coordinator.Id = nId;
        coordinator.nPollingInterval = nInterval;
        coordinator.nConnectTimeout = nTimeout;
        coordinator.isMonitorEnabled = isMonitor;

        // 處理點位 — Sequence 由陣列索引+1 自動產生（與 Modbus 對齊），最多 100 個
        var pointList = new List<DbPointModel>();
        if (dto.Points != null)
        {
            for (int i = 0; i < dto.Points.Count; i++)
            {
                var pt = dto.Points[i];
                var nSeq = i + 1;
                if (nSeq > 100)
                {
                    _logger.LogWarning("Coordinator {Name} 點位數超過 100，第 {N} 筆以後已忽略",
                        coordinator.szName, nSeq);
                    break;
                }

                pointList.Add(new DbPointModel
                {
                    szSID = $"DB{nId}-S{nSeq}",
                    nCoordinatorId = nId,
                    nSequence = nSeq,
                    szName = pt.Name ?? string.Empty,
                    szUnit = pt.Unit ?? string.Empty,
                    fMin = pt.Min,
                    fMax = pt.Max
                });
            }
        }

        await _repository.SaveDbPointsAsync(nId, pointList);

        _logger.LogInformation("DB 來源載入: {Name} (Id={Id}), 點位 {Count}",
            coordinator.szName, nId, pointList.Count);

        return new DbCoordinatorLoaded
        {
            Coordinator = coordinator,
            Points = pointList
        };
    }
}

/// <summary>
/// 一個 Coordinator 連同其所有點位（給 DbCommunicationService 啟動 polling 用）
/// </summary>
public class DbCoordinatorLoaded
{
    public DbCoordinatorModel Coordinator { get; set; } = new();
    public List<DbPointModel> Points { get; set; } = new();
}

/// <summary>
/// DBPoint/*.json 反序列化 DTO
/// </summary>
internal class DbCoordinatorJsonDto
{
    public string? Name { get; set; }
    public int PollingInterval { get; set; } = 1000;
    public int ConnectTimeout { get; set; } = 1000;
    public bool MonitorEnabled { get; set; } = true;
    public List<DbPointJsonDto>? Points { get; set; }
}

internal class DbPointJsonDto
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public float Min { get; set; } = 0.0f;
    public float Max { get; set; } = 100.0f;
}
