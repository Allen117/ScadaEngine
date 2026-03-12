namespace ScadaEngine.Engine.Models;

/// <summary>
/// 畫面設計頁面模型，對應 ScadaDesignPage 資料表
/// </summary>
public class ScadaDesignPageModel
{
    /// <summary>前端 szId，例如 p1、p2-1</summary>
    public string  szPageSid        { get; set; } = string.Empty;

    /// <summary>父頁面 szId；null 代表根節點</summary>
    public string? szParentPageSid  { get; set; }

    /// <summary>同層顯示順序（0 起算）</summary>
    public int     nSortOrder       { get; set; } = 0;

    /// <summary>頁面顯示名稱</summary>
    public string  szPageName       { get; set; } = string.Empty;

    /// <summary>FontAwesome class，例如 fa-home</summary>
    public string? szPageIcon       { get; set; }

    /// <summary>畫布寬度（px）</summary>
    public int     nCanvasW         { get; set; } = 1200;

    /// <summary>畫布高度（px）</summary>
    public int     nCanvasH         { get; set; } = 800;

    /// <summary>背景圖檔名</summary>
    public string? szBgFileName     { get; set; }

    /// <summary>背景圖 base64 DataURL（nvarchar MAX）</summary>
    public string? szBgDataUrl      { get; set; }

    /// <summary>
    /// 該頁 widget 陣列 JSON
    /// 格式: [{szType, nX, nY, nW, nH, props:{...}}, ...]
    /// </summary>
    public string? szWidgetStateJson { get; set; }
}
