using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// Email 群組 / 收件人 / 群組-規則對應 資料存取 — Engine 端唯讀
/// 內含 60 秒快取，避免每筆警報都查 DB
/// </summary>
public class EmailGroupRepository
{
    private readonly ILogger<EmailGroupRepository> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    private List<EmailGroupCache> _cache = new();
    private DateTime _dtLastRefreshed = DateTime.MinValue;
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public EmailGroupRepository(ILogger<EmailGroupRepository> logger, DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 取得所有啟用中的群組（含對應的啟用收件人 + 對應的 AlarmRuleId 集合）。含 60 秒快取。
    /// </summary>
    public async Task<IReadOnlyList<EmailGroupCache>> GetEnabledGroupsAsync()
    {
        if (DateTime.UtcNow - _dtLastRefreshed < _refreshInterval)
            return _cache;

        await _refreshLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _dtLastRefreshed < _refreshInterval)
                return _cache;

            await EnsureConnectionStringAsync();
            using var connection = new SqlConnection(_szConnectionString);
            await connection.OpenAsync();

            const string szGroupSql = @"
                SELECT Id          AS nId,
                       Name        AS szName,
                       Label       AS szLabel,
                       MaxSeverity AS nMaxSeverity,
                       Language    AS szLanguage,
                       IsEnabled   AS isEnabled,
                       Remarks     AS szRemarks,
                       CreatedAt   AS dtCreatedAt,
                       UpdatedAt   AS dtUpdatedAt
                FROM EmailGroups
                WHERE IsEnabled = 1";
            var groups = (await connection.QueryAsync<EmailGroupModel>(szGroupSql)).ToList();

            const string szRecipientSql = @"
                SELECT Id           AS nId,
                       GroupId      AS nGroupId,
                       EmailAddress AS szEmailAddress,
                       DisplayName  AS szDisplayName,
                       IsEnabled    AS isEnabled,
                       CreatedAt    AS dtCreatedAt,
                       UpdatedAt    AS dtUpdatedAt
                FROM EmailRecipients
                WHERE IsEnabled = 1";
            var recipients = (await connection.QueryAsync<EmailRecipientModel>(szRecipientSql)).ToList();

            const string szMapSql = "SELECT GroupId, AlarmRuleId FROM EmailGroupRuleMap";
            var maps = (await connection.QueryAsync<(int GroupId, int AlarmRuleId)>(szMapSql)).ToList();

            var newCache = groups.Select(g => new EmailGroupCache
            {
                group = g,
                recipients = recipients.Where(r => r.nGroupId == g.nId).ToList(),
                ruleIds = maps.Where(m => m.GroupId == g.nId).Select(m => m.AlarmRuleId).ToHashSet()
            }).ToList();

            _cache = newCache;
            _dtLastRefreshed = DateTime.UtcNow;
            _logger.LogDebug("已重載 Email 群組: {Groups} 群組 / {Recipients} 收件人 / {Maps} 對應",
                newCache.Count, recipients.Count, maps.Count);
            return _cache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "讀取 Email 群組資料失敗，使用上次快取值");
            return _cache;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>強制清除快取，下次查詢會重新讀 DB</summary>
    public void InvalidateCache() => _dtLastRefreshed = DateTime.MinValue;

    private async Task EnsureConnectionStringAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
    }
}

public class EmailGroupCache
{
    public EmailGroupModel group { get; set; } = new();
    public List<EmailRecipientModel> recipients { get; set; } = new();
    /// <summary>該群組接收的 AlarmRules.Id 集合。空集合視為「全收」</summary>
    public HashSet<int> ruleIds { get; set; } = new();
}
