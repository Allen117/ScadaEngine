using System.Text.Json;
using ScadaEngine.ModbusServer.Core;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Services;

/// <summary>
/// 點位目錄與 AddressMap 維護：
/// - 啟動即載入持久化的 AddressMap.json（DB 連不上也能先照舊表服務）
/// - ScanAsync()：讀四類點位表 → append-only 配址（AddressAllocator）→ 有新點才回寫 JSON
/// - 對外提供不可變 snapshot（Modbus 掃寫迴圈 / Web API 共用）
/// </summary>
public class PointCatalogService
{
    private const string ADDRESS_MAP_PATH = "./AddressMap.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogger<PointCatalogService> _logger;
    private readonly GatewayRepository _repository;
    private readonly ModbusServerSettingModel _setting;
    private readonly object _scanLock = new();

    private AddressMapModel _map = new();

    // 掃寫迴圈每秒讀取 — 以整個陣列/集合替換的方式發布，避免鎖競爭
    private volatile AddressMapEntryModel[] _snapshot = Array.Empty<AddressMapEntryModel>();
    private volatile HashSet<string> _activeSids = new(StringComparer.Ordinal);

    /// <summary>是否已成功完成過至少一次點位表掃描（未成功前 monitor 會定期重試）</summary>
    public bool HasScannedOk { get; private set; }

    public DateTime dtLastScanAt { get; private set; } = DateTime.MinValue;

    public PointCatalogService(ILogger<PointCatalogService> logger, GatewayRepository repository,
        ModbusServerSettingModel setting)
    {
        _logger = logger;
        _repository = repository;
        _setting = setting;
        LoadPersistedMap();
    }

    /// <summary>目前對照表 snapshot（依位址排序）</summary>
    public AddressMapEntryModel[] GetSnapshot() => _snapshot;

    /// <summary>目前點位表中仍存在的 SID（不在其中 = 已刪除/停用，網頁標示、值回報 NaN）</summary>
    public HashSet<string> GetActiveSids() => _activeSids;

    private void LoadPersistedMap()
    {
        try
        {
            if (!File.Exists(ADDRESS_MAP_PATH)) return;
            var szJson = File.ReadAllText(ADDRESS_MAP_PATH);
            var map = JsonSerializer.Deserialize<AddressMapModel>(szJson);
            if (map?.Entries is { Count: > 0 })
            {
                map.Entries.Sort((a, b) => a.nAddress.CompareTo(b.nAddress));
                _map = map;
                _snapshot = map.Entries.ToArray();
                _logger.LogInformation("已載入 AddressMap.json：{Count} 筆既有配址", map.Entries.Count);
            }
        }
        catch (Exception ex)
        {
            // 對照檔壞掉不能拿空表重配 — 那會讓所有位址重排。保留檔案、停在空 snapshot 等人工處理
            _logger.LogError(ex, "AddressMap.json 載入失敗，為避免位址重排不會自動重建，請人工檢查該檔");
            throw;
        }
    }

    /// <summary>
    /// 掃描點位表並同步 AddressMap。回傳 (新增筆數, 總筆數)；失敗擲出（caller 決定重試策略）。
    /// </summary>
    public async Task<(int nAdded, int nTotal)> ScanAsync()
    {
        var catalog = await _repository.GetCatalogAsync();

        int nAdded;
        lock (_scanLock)
        {
            nAdded = AddressAllocator.Sync(_map, catalog, _setting.Segments);
            if (nAdded > 0)
            {
                _map.dtUpdatedAt = DateTime.Now;
                PersistMap();
            }
            _snapshot = _map.Entries.ToArray();
            _activeSids = new HashSet<string>(catalog.Select(p => p.szSID), StringComparer.Ordinal);
            HasScannedOk = true;
            dtLastScanAt = DateTime.Now;
        }

        if (nAdded > 0)
            _logger.LogInformation("AddressMap 同步：新增 {Added} 點（append-only），總計 {Total} 點", nAdded, _map.Entries.Count);
        return (nAdded, _map.Entries.Count);
    }

    private void PersistMap()
    {
        // 先寫暫存檔再原子替換，避免寫到一半斷電產生壞檔
        var szTempPath = ADDRESS_MAP_PATH + ".tmp";
        File.WriteAllText(szTempPath, JsonSerializer.Serialize(_map, _jsonOptions));
        File.Move(szTempPath, ADDRESS_MAP_PATH, overwrite: true);
    }
}
