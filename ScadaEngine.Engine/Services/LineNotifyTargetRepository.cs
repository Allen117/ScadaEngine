using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Line 通知收件群組資料存取 — Engine 端唯讀（CRUD 由 Web 處理）
/// 內含 60 秒快取，避免每筆警報都查 DB
/// </summary>
public class LineNotifyTargetRepository
{
    private readonly ILogger<LineNotifyTargetRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    private List<LineNotifyTargetModel> _cachedTargets = new();
    private DateTime _dtLastRefreshed = DateTime.MinValue;
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public LineNotifyTargetRepository(
        ILogger<LineNotifyTargetRepository> logger,
        DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 取得所有啟用中的收件群組（含 60 秒快取）
    /// </summary>
    public async Task<IReadOnlyList<LineNotifyTargetModel>> GetEnabledTargetsAsync()
    {
        if (DateTime.UtcNow - _dtLastRefreshed < _refreshInterval)
            return _cachedTargets;

        await _refreshLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _dtLastRefreshed < _refreshInterval)
                return _cachedTargets;

            await EnsureConnectionStringAsync();
            const string szSql = @"
                SELECT Id          AS nId,
                       GroupId     AS szGroupId,
                       Label       AS szLabel,
                       MaxSeverity AS nMaxSeverity,
                       Language    AS szLanguage,
                       IsEnabled   AS isEnabled,
                       CreatedAt   AS dtCreatedAt,
                       UpdatedAt   AS dtUpdatedAt
                FROM LineNotifyTargets
                WHERE IsEnabled = 1";

            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();
            var rows = await connection.QueryAsync<LineNotifyTargetModel>(szSql);
            _cachedTargets = rows.ToList();
            _dtLastRefreshed = DateTime.UtcNow;
            _logger.LogDebug("已重載 Line 收件群組: {Count} 筆", _cachedTargets.Count);
            return _cachedTargets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "讀取 Line 收件群組失敗，使用上次快取值");
            return _cachedTargets;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task EnsureConnectionStringAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
    }
}
