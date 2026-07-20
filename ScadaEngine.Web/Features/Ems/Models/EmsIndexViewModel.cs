namespace ScadaEngine.Web.Features.Ems.Models;

/// <summary>
/// /EMS 首頁 ViewModel — 依 /EmsCardSetting 生效順序過濾後的可見卡片清單。
/// </summary>
public class EmsIndexViewModel
{
    /// <summary>可見卡片（順序即渲染順序）</summary>
    public List<EmsCardDefinition> aVisibleCards { get; set; } = [];

    /// <summary>指定卡片是否可見（View 端決定各驅動 JS 是否載入）</summary>
    public bool HasCard(string szCardKey) =>
        aVisibleCards.Any(c => string.Equals(c.szCardKey, szCardKey, StringComparison.OrdinalIgnoreCase));
}
