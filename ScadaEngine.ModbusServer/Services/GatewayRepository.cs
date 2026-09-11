using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.ModbusServer.Services;

/// <summary>
/// Gateway 專用精簡資料存取（唯讀）。
/// 刻意不引用 Engine 的 IDataRepository — 保持可獨立部署（docs/plans 決策 3）。
/// SQL 一律用別名對映匈牙利屬性名（Dapper 依屬性名對映，專案慣例）。
/// </summary>
public class GatewayRepository
{
    private readonly ILogger<GatewayRepository> _logger;
    private readonly DatabaseConfigService _dbConfigService;
    private string? _szConnectionString;

    public GatewayRepository(ILogger<GatewayRepository> logger, DatabaseConfigService dbConfigService)
    {
        _logger = logger;
        _dbConfigService = dbConfigService;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        _szConnectionString ??= await _dbConfigService.GetConnectionStringAsync();
        var connection = new SqlConnection(_szConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// 讀取四類點位表，合併為單一目錄。
    /// 任一查詢失敗即整批擲出（caller 決定沿用舊 AddressMap）。
    /// </summary>
    public async Task<List<CatalogPointModel>> GetCatalogAsync()
    {
        using var connection = await OpenAsync();
        var catalog = new List<CatalogPointModel>();

        var modbusPoints = await connection.QueryAsync<(string szSID, string szName, string? szUnit, string? szAddress)>(
            @"SELECT SID AS szSID, Name AS szName, Unit AS szUnit, Address AS szAddress
              FROM ModbusPoints ORDER BY SID");
        catalog.AddRange(modbusPoints.Select(p => new CatalogPointModel
        {
            szSID = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit ?? "",
            Source = PointSource.Modbus,
            szRawAddress = p.szAddress ?? "",
        }));

        // 計算點位：停用者不配址（若曾配過位址則保留、runtime 標示已停用）
        var calcPoints = await connection.QueryAsync<(string szSID, string szName, string? szUnit)>(
            @"SELECT SID AS szSID, Name AS szName, Unit AS szUnit
              FROM CalculatedPoints WHERE IsEnabled = 1 ORDER BY SID");
        catalog.AddRange(calcPoints.Select(p => new CatalogPointModel
        {
            szSID = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit ?? "",
            Source = PointSource.Calc,
        }));

        var dbPoints = await connection.QueryAsync<(string szSID, string szName, string? szUnit, int nCoordinatorId, int nSequence)>(
            @"SELECT SID AS szSID, Name AS szName, Unit AS szUnit,
                     CoordinatorId AS nCoordinatorId, Sequence AS nSequence
              FROM DBPoints ORDER BY CoordinatorId, Sequence");
        catalog.AddRange(dbPoints.Select(p => new CatalogPointModel
        {
            szSID = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit ?? "",
            Source = PointSource.Db,
            szRawAddress = $"DB{p.nCoordinatorId}#{p.nSequence}",
        }));

        var opcUaPoints = await connection.QueryAsync<(string szSID, string szName, string? szUnit, string? szTagName)>(
            @"SELECT SID AS szSID, Name AS szName, Unit AS szUnit, TagName AS szTagName
              FROM OpcUaPoints ORDER BY CoordinatorId, Sequence");
        catalog.AddRange(opcUaPoints.Select(p => new CatalogPointModel
        {
            szSID = p.szSID,
            szName = p.szName,
            szUnit = p.szUnit ?? "",
            Source = PointSource.OpcUa,
            szRawAddress = p.szTagName ?? "",
        }));

        _logger.LogInformation("點位目錄掃描完成：Modbus={Modbus}、Calc={Calc}、DB={Db}、OPC UA={OpcUa}",
            catalog.Count(p => p.Source == PointSource.Modbus),
            catalog.Count(p => p.Source == PointSource.Calc),
            catalog.Count(p => p.Source == PointSource.Db),
            catalog.Count(p => p.Source == PointSource.OpcUa));
        return catalog;
    }

    /// <summary>LatestData 全表（啟動預填非 DB 來源點位用）</summary>
    public async Task<Dictionary<string, LatestDataModel>> GetLatestDataMapAsync()
    {
        using var connection = await OpenAsync();
        var rows = await connection.QueryAsync<LatestDataModel>(
            @"SELECT SID AS szSID, Value AS fValue, Quality AS nQuality, Timestamp AS dtTimestamp
              FROM LatestData");
        var map = new Dictionary<string, LatestDataModel>(StringComparer.Ordinal);
        foreach (var row in rows) map[row.szSID] = row;
        return map;
    }

    /// <summary>
    /// DBLatestData（DB 來源點位輪詢用）。
    /// 例外不 swallow — caller 需分辨「讀失敗」vs「無資料」以決定 Bad quality。
    /// </summary>
    public async Task<Dictionary<string, LatestDataModel>> GetDbLatestDataMapAsync()
    {
        using var connection = await OpenAsync();
        var rows = await connection.QueryAsync<LatestDataModel>(new CommandDefinition(
            @"SELECT SID                          AS szSID,
                     ISNULL(Value, 0)             AS fValue,
                     ISNULL(Quality, 0)           AS nQuality,
                     ISNULL(Timestamp, GETDATE()) AS dtTimestamp
              FROM DBLatestData
              WHERE SID LIKE 'DB%'",
            commandTimeout: 2));
        var map = new Dictionary<string, LatestDataModel>(StringComparer.Ordinal);
        foreach (var row in rows) map[row.szSID] = row;
        return map;
    }
}
