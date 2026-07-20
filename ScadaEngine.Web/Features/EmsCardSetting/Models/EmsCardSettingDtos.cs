using ScadaEngine.Web.Features.Ems.Models;

namespace ScadaEngine.Web.Features.EmsCardSetting.Models;

/// <summary>DB EmsCardSetting 覆寫列（Dapper 查詢對應）</summary>
public class EmsCardOverrideRow
{
    public string szCardKey { get; set; } = string.Empty;
    public bool isVisible { get; set; }
    public int nSortOrder { get; set; }
}

/// <summary>生效卡片（註冊表定義 + merge 後的顯示狀態/順序）</summary>
public record EmsEffectiveCard(EmsCardDefinition Definition, bool isVisible, int nSortOrder);

/// <summary>設定頁儲存請求的單張卡片（陣列順序 = 顯示順序）</summary>
public class EmsCardSaveItemDto
{
    public string cardKey { get; set; } = string.Empty;
    public bool isVisible { get; set; }
}

/// <summary>POST api/cards 請求</summary>
public class SaveEmsCardsRequest
{
    public List<EmsCardSaveItemDto> cards { get; set; } = [];
}
