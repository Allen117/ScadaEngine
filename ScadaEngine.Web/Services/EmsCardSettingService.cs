using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.Ems.Models;
using ScadaEngine.Web.Features.EmsCardSetting.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// EMS 首頁卡片顯示設定 — EmsCardRegistry（唯一真相來源）merge DB 覆寫（EmsCardSetting 表）。
/// merge 規則：
///   DB 有列 → 用 DB 的 IsVisible / SortOrder；
///   DB 無列（新卡片）→ IsVisible=true，排在所有 DB 列之後、依註冊表預設順序；
///   DB 有但註冊表沒有（卡片被移除）→ 忽略該列，不渲染、不列在設定頁。
/// 表空 = 全開、預設順序。
/// </summary>
public class EmsCardSettingService
{
    private readonly ILogger<EmsCardSettingService> _logger;
    private readonly DatabaseConfigService _configService;
    private string _szConnectionString = string.Empty;

    public EmsCardSettingService(ILogger<EmsCardSettingService> logger, DatabaseConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// 取得生效卡片清單（含隱藏卡，設定頁要列出全部；/EMS 端自行過濾 isVisible）。
    /// 回傳順序即生效顯示順序。
    /// </summary>
    public async Task<List<EmsEffectiveCard>> GetEffectiveCardsAsync()
    {
        using var conn = await GetConnectionAsync();
        var aRows = (await conn.QueryAsync<EmsCardOverrideRow>(@"
            SELECT CardKey   AS szCardKey,
                   IsVisible AS isVisible,
                   SortOrder AS nSortOrder
            FROM   EmsCardSetting")).ToList();

        var mapOverride = aRows.ToDictionary(r => r.szCardKey, StringComparer.OrdinalIgnoreCase);

        var aWithDb    = new List<EmsEffectiveCard>();
        var aWithoutDb = new List<EmsEffectiveCard>();
        foreach (var card in EmsCardRegistry.Cards)
        {
            if (mapOverride.TryGetValue(card.szCardKey, out var row))
                aWithDb.Add(new EmsEffectiveCard(card, row.isVisible, row.nSortOrder));
            else
                aWithoutDb.Add(new EmsEffectiveCard(card, true, int.MaxValue));
        }

        // DB 覆寫列在前（依 SortOrder），未入 DB 的新卡片排最後（依註冊表預設順序）
        var aResult = aWithDb
            .OrderBy(c => c.nSortOrder)
            .ThenBy(c => c.Definition.nDefaultOrder)
            .Concat(aWithoutDb.OrderBy(c => c.Definition.nDefaultOrder))
            .ToList();

        // SortOrder 正規化為 1..N（顯示用；不回寫 DB）
        for (int i = 0; i < aResult.Count; i++)
            aResult[i] = aResult[i] with { nSortOrder = i + 1 };
        return aResult;
    }

    /// <summary>
    /// 儲存整份設定 — 交易內 DELETE 全表 → 依傳入順序 INSERT（SortOrder=1..N）。
    /// 只收註冊表內合法 CardKey；重複鍵取第一筆。
    /// </summary>
    public async Task SaveAsync(List<EmsCardSaveItemDto> aCards)
    {
        var aValid = new List<EmsCardSaveItemDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in aCards)
        {
            if (string.IsNullOrWhiteSpace(item.cardKey) || !EmsCardRegistry.Contains(item.cardKey))
                continue;
            if (!seen.Add(item.cardKey))
                continue;
            aValid.Add(item);
        }

        using var conn = await GetConnectionAsync();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM EmsCardSetting", transaction: tx);
        for (int i = 0; i < aValid.Count; i++)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO EmsCardSetting (CardKey, IsVisible, SortOrder, UpdatedAt)
                VALUES (@szCardKey, @isVisible, @nSortOrder, GETDATE())",
                new { szCardKey = aValid[i].cardKey.Trim(), isVisible = aValid[i].isVisible, nSortOrder = i + 1 },
                tx);
        }
        tx.Commit();

        _logger.LogInformation("EMS 卡片顯示設定已更新：{Cards}",
            string.Join(", ", aValid.Select((c, i) => $"{i + 1}.{c.cardKey}={(c.isVisible ? "顯示" : "隱藏")}")));
    }
}
