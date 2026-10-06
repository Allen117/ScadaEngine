using System.Text;
using ClosedXML.Excel;
using ScadaEngine.Web.Features.Shared.Models;

namespace ScadaEngine.Web.Services.SourceExcel;

/// <summary>
/// 各來源（Modbus / DB）要實作的 Excel ⇄ JSON 轉換介面。
/// 實作必須是**純函數、無 I/O、無 DI 依賴**（Singleton 註冊、可直接單元測試）：
/// 預覽、token、確認、提交前重新比對、寫檔、刪檔、Modal UI 都在共用的 SourceExcelImportCoordinator，
/// adapter 只負責「版面解析 + 驗證規則 + JSON 序列化 + 範本版面」。
/// 日後 OPC UA 要支援只要再加一個 adapter。
/// </summary>
public interface ISourceExcelAdapter
{
    SourceExcelKind Kind { get; }

    /// <summary>appsettings 區段（SourceConfigFileIo.MODBUS_SECTION / DBPOINT_SECTION）</summary>
    string ConfigSection { get; }

    /// <summary>下載範本的檔名（含 .xlsx）</summary>
    string TemplateFileName { get; }

    /// <summary>匯出檔名前綴（含 .xlsx 由呼叫端補）</summary>
    string ExportFileNamePrefix { get; }

    /// <summary>空白範本的工作表名稱</summary>
    string DefaultSheetName { get; }

    /// <summary>設備層欄位（比對差異的顯示順序）</summary>
    IReadOnlyList<string> HeaderFields { get; }

    /// <summary>點位欄位（比對差異的顯示順序；必含 "Name"）</summary>
    IReadOnlyList<string> PointFields { get; }

    /// <summary>新建 JSON 檔的編碼（既有檔沿用原編碼）</summary>
    Encoding NewFileEncoding { get; }

    /// <summary>解析一張工作表（含驗證；錯誤/提示寫進 sheet.Errors / Warnings，不丟例外）</summary>
    ParsedSourceSheet ParseSheet(IXLWorksheet ws);

    /// <summary>把既有 JSON 解析成同樣的中介表示（供比對與匯出）；格式壞掉回 null</summary>
    ParsedSourceSheet? ParseJson(string szName, string szJson);

    /// <summary>序列化為 Engine 可載入的 JSON 文字；szSourceWorkbook 非空時寫入頂層 SourceWorkbook</summary>
    string ToJson(ParsedSourceSheet sheet, string? szSourceWorkbook);

    /// <summary>把工作表版面寫進 ws（sheet=null → 空白範本，含預設值與下拉驗證）</summary>
    void WriteSheet(IXLWorksheet ws, ParsedSourceSheet? sheet);
}
