namespace ScadaEngine.Web.Features.GlobalSearch.Models;

/// <summary>
/// 全站搜尋索引單筆條目 — 由後端依登入者權限過濾後回傳前端做本地比對。
/// 同時攜帶 zh-TW / en 標題與同義關鍵字，讓任一語系輸入都能命中。
/// </summary>
public class SearchIndexEntry
{
    /// <summary>頁面路由（如 /EnergyReport）</summary>
    public string szRoute { get; set; } = string.Empty;

    /// <summary>zh-TW 標題（取自 _Layout resx）</summary>
    public string szTitleZh { get; set; } = string.Empty;

    /// <summary>en 標題（取自 _Layout .en.resx）</summary>
    public string szTitleEn { get; set; } = string.Empty;

    /// <summary>同義關鍵字（空白分隔、混雜中英，僅供比對不顯示）</summary>
    public string szKeywords { get; set; } = string.Empty;

    /// <summary>Font Awesome 圖示 class（與導覽列選單一致）</summary>
    public string szIcon { get; set; } = string.Empty;

    /// <summary>是否屬 EMS 體系頁面（前端顯示綠葉標記用）</summary>
    public bool isEms { get; set; }
}
