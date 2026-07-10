namespace ScadaEngine.Web.Features.DbCoordinator.Models;

/// <summary>
/// 「DB 來源」頁 — 更新單一點位（名稱 + 單位）請求。
/// 只開放這兩欄：Min/Max 與陣列結構仍由 Excel 巨集維護。
/// </summary>
public class UpdatePointRequest
{
    /// <summary>Coordinator Id（DBCoordinator.Id）</summary>
    public int Id { get; set; }

    /// <summary>點位序號（1~100，= JSON 陣列索引+1，不可位移）</summary>
    public int Sequence { get; set; }

    /// <summary>新點位名稱</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>新物理單位（可空白；自動帶入類功能以此欄篩選，如電表設定只列 kWh）</summary>
    public string NewUnit { get; set; } = string.Empty;
}
