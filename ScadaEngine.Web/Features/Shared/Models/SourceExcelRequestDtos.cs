namespace ScadaEngine.Web.Features.Shared.Models;

/// <summary>POST {base}/ImportCommit 請求 — 帶預覽 token 與使用者勾選結果</summary>
public class SourceExcelCommitRequest
{
    public string Token { get; set; } = string.Empty;

    /// <summary>要匯入（新增 + 覆寫）的工作表名稱</summary>
    public List<string> ImportSheets { get; set; } = new();

    /// <summary>要刪除的既有設定名稱</summary>
    public List<string> DeleteNames { get; set; } = new();

    /// <summary>使用者已勾選「我了解 SID 位移後果」</summary>
    public bool AcknowledgeSidShift { get; set; }
}

/// <summary>POST {base}/DeleteSource 請求</summary>
public class SourceExcelDeleteRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>ImportCommit / DeleteSource 回應</summary>
public class SourceExcelCommitResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>true = 預覽後設定已被更動，需重新預覽（HTTP 409）</summary>
    public bool Conflict { get; set; }

    public List<string> Imported { get; } = new();
    public List<string> Deleted { get; } = new();
}

/// <summary>共用 partial `_SourceExcelImport.cshtml` 的 ViewModel（工具列 + 預覽 Modal）</summary>
public class SourceExcelImportViewModel
{
    /// <summary>Controller 路由前綴，如 "/ModbusCoordinator"</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>"modbus" / "dbpoint" — 前端用來切換說明文字</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>既有設定的來源 Excel 檔名（不重複）— 匯出下拉用</summary>
    public List<string> Workbooks { get; set; } = new();
}
