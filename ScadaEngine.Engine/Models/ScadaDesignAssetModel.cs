namespace ScadaEngine.Engine.Models;

/// <summary>
/// Designer 自訂圖片/GIF 去重資產模型，對應 ScadaDesignAsset 資料表。
/// Hash = SHA-256(內容) 為主鍵，同內容只存一份；image widget 僅存
/// /Designer/asset/{Hash} URL 引用，避免頁 WidgetStateJson 內嵌 base64 膨脹。
/// </summary>
public class ScadaDesignAssetModel
{
    /// <summary>SHA-256 hex（小寫）</summary>
    public string szHash        { get; set; } = string.Empty;

    /// <summary>MIME，例如 image/gif、image/png</summary>
    public string szContentType { get; set; } = string.Empty;

    /// <summary>圖片內容 base64（不含 data: 前綴）</summary>
    public string szDataBase64  { get; set; } = string.Empty;

    /// <summary>內容位元組大小</summary>
    public int    nByteSize     { get; set; }
}
