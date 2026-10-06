using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.Shared.Models;

namespace ScadaEngine.Web.Services.SourceExcel;

/// <summary>
/// Excel 匯入共用流程（Modbus / DB 來源共用，各來源差異交給 <see cref="ISourceExcelAdapter"/>）：
///
/// 1. <see cref="PreviewAsync"/>：解析 + 驗證 + 與既有 JSON 比對 → 回傳預覽 + previewToken（解析結果暫存 IMemoryCache 10 分鐘、綁定使用者），**不寫檔**
/// 2. <see cref="CommitAsync"/>：帶 token + 使用者勾選的匯入／刪除清單 → 提交前重新比對（既有檔指紋變了就拒絕，避免靜默蓋掉別人的熱編輯）
///    → 原子寫檔 / 移到 _deleted/ → 清 token
/// 3. <see cref="DeleteAsync"/>：逐台刪除（不必上傳 Excel）
/// 4. <see cref="BuildTemplate"/> / <see cref="ExportAsync"/>：空白範本、現行設定匯出（全部或依來源 Excel 檔）
///
/// 匯入時在 JSON 頂層寫入 SourceWorkbook（上傳檔名），作為下次「同檔刪除候選」的判定依據（決策 5）。
/// Scoped 註冊（依賴 IStringLocalizer）；寫檔互斥為 static 跨請求共用。
/// </summary>
public class SourceExcelImportCoordinator
{
    private const string CACHE_PREFIX = "srcxl:";
    private static readonly TimeSpan PREVIEW_TTL = TimeSpan.FromMinutes(10);

    /// <summary>提交 / 刪除互斥（static — Scoped service 跨請求共用；與熱編輯的鎖不同，但彼此靠「提交前指紋重比對」互保）</summary>
    private static readonly SemaphoreSlim _commitGate = new(1, 1);

    private readonly SourceConfigFileIo _io;
    private readonly IMemoryCache _cache;
    private readonly IStringLocalizer<SourceExcelImportCoordinator> _l;
    private readonly ILogger<SourceExcelImportCoordinator> _logger;

    public SourceExcelImportCoordinator(
        SourceConfigFileIo io,
        IMemoryCache cache,
        IStringLocalizer<SourceExcelImportCoordinator> localizer,
        ILogger<SourceExcelImportCoordinator> logger)
    {
        _io = io;
        _cache = cache;
        _l = localizer;
        _logger = logger;
    }

    // ───────────────────────────── 預覽 ─────────────────────────────

    public async Task<SourceExcelPreviewResult> PreviewAsync(ISourceExcelAdapter adapter, Stream xlsxStream, string szWorkbookFileName, string szUsername)
    {
        var result = new SourceExcelPreviewResult { WorkbookName = Path.GetFileName(szWorkbookFileName ?? string.Empty) };

        var szFolder = _io.GetWatchedFolder(adapter.ConfigSection);
        if (szFolder == null)
        {
            result.Message = _l["srcxl.svc.folder_not_configured", adapter.ConfigSection].Value;
            return result;
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsxStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Excel 讀取失敗: {File}", result.WorkbookName);
            result.Message = _l["srcxl.svc.file_unreadable"].Value;
            return result;
        }

        using (workbook)
        {
            if (workbook.Worksheets.Count == 0)
            {
                result.Message = _l["srcxl.svc.no_sheets"].Value;
                return result;
            }

            var session = new SourceExcelPreviewSession
            {
                Kind = adapter.Kind,
                Username = szUsername,
                WorkbookName = result.WorkbookName,
            };

            // 既有設定一覽（名稱 + SourceWorkbook）
            var existingInfos = await ListExistingAsync(adapter);
            var existingParsed = new Dictionary<string, ParsedSourceSheet>(StringComparer.OrdinalIgnoreCase);

            foreach (var ws in workbook.Worksheets)
            {
                var sheet = adapter.ParseSheet(ws);
                var szName = sheet.SheetName;

                if (session.Sheets.ContainsKey(szName))
                    continue; // Excel 不允許同名工作表（大小寫不同視為同一個檔名 → 取第一個）

                ParsedSourceSheet? existing = null;
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, szName);
                if (szPath != null && File.Exists(szPath))
                {
                    var (szJson, _) = await SourceConfigFileIo.ReadAllTextDetectEncodingAsync(szPath);
                    existing = adapter.ParseJson(szName, szJson);
                    if (existing == null)
                        sheet.Warnings.Add(SheetIssue.General("srcxl.svc.existing_unreadable"));
                    session.Fingerprints[szName] = Fingerprint(szPath);
                }
                else
                {
                    session.Fingerprints[szName] = string.Empty;
                }

                var diff = SourceExcelDiffBuilder.Build(sheet, existing, adapter);
                if (existing == null && szPath != null && File.Exists(szPath))
                {
                    // 既有檔存在但解析不了 → 仍是覆寫（整份蓋掉），不能算新增
                    diff.Kind = diff.Kind == SheetDiffKind.Added ? SheetDiffKind.Overwrite : diff.Kind;
                    diff.DefaultChecked = diff.Kind == SheetDiffKind.Overwrite;
                }
                LocalizeIssues(diff.Errors);
                LocalizeIssues(diff.Warnings);

                session.Sheets[szName] = sheet;
                session.Diffs[szName] = diff;
                if (existing != null) existingParsed[szName] = existing;
                result.Sheets.Add(diff);
            }

            // 刪除候選 / 其他既有
            var (candidates, others) = SourceExcelDiffBuilder.ClassifyExisting(existingInfos, session.Sheets.Keys, result.WorkbookName);
            result.DeleteCandidates.AddRange(candidates);
            result.OtherExisting.AddRange(others);
            foreach (var e in candidates.Concat(others))
            {
                session.Deletable.Add(e.Name);
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, e.Name);
                session.Fingerprints[e.Name] = szPath != null && File.Exists(szPath) ? Fingerprint(szPath) : string.Empty;
            }

            // 工作表改名提示：新增的工作表點位名稱與某刪除候選完全相同
            if (candidates.Count > 0)
            {
                var candidateSheets = new List<ParsedSourceSheet>();
                foreach (var c in candidates)
                {
                    var szPath = SourceConfigFileIo.ResolveWithin(szFolder, c.Name);
                    if (szPath == null || !File.Exists(szPath)) continue;
                    var (szJson, _) = await SourceConfigFileIo.ReadAllTextDetectEncodingAsync(szPath);
                    var parsed = adapter.ParseJson(c.Name, szJson);
                    if (parsed != null) candidateSheets.Add(parsed);
                }
                foreach (var diff in result.Sheets.Where(d => d.Kind == SheetDiffKind.Added))
                    diff.RenameHintFrom = SourceExcelDiffBuilder.FindRenameHint(session.Sheets[diff.SheetName], candidateSheets);
            }

            result.Token = Guid.NewGuid().ToString("N");
            _cache.Set(CACHE_PREFIX + result.Token, session, new MemoryCacheEntryOptions { SlidingExpiration = PREVIEW_TTL });
            result.Success = true;

            _logger.LogInformation("Excel 匯入預覽: {Kind} {File} 工作表 {Count} 張（新增 {Added}／覆寫 {Over}／無變更 {Same}／錯誤 {Err}），刪除候選 {Del}",
                adapter.Kind, result.WorkbookName, result.Sheets.Count,
                result.Sheets.Count(s => s.Kind == SheetDiffKind.Added),
                result.Sheets.Count(s => s.Kind == SheetDiffKind.Overwrite),
                result.Sheets.Count(s => s.Kind == SheetDiffKind.Unchanged),
                result.Sheets.Count(s => s.Kind == SheetDiffKind.Error),
                result.DeleteCandidates.Count);
            return result;
        }
    }

    // ───────────────────────────── 提交 ─────────────────────────────

    public async Task<SourceExcelCommitResult> CommitAsync(ISourceExcelAdapter adapter, SourceExcelCommitRequest request, string szUsername)
    {
        var result = new SourceExcelCommitResult();

        if (string.IsNullOrWhiteSpace(request.Token)
            || !_cache.TryGetValue(CACHE_PREFIX + request.Token, out SourceExcelPreviewSession? session)
            || session == null
            || session.Kind != adapter.Kind)
        {
            result.Message = _l["srcxl.svc.preview_expired"].Value;
            return result;
        }

        if (!string.Equals(session.Username, szUsername, StringComparison.Ordinal))
        {
            result.Message = _l["srcxl.svc.preview_user_mismatch"].Value;
            return result;
        }

        var szFolder = _io.GetWatchedFolder(adapter.ConfigSection);
        if (szFolder == null)
        {
            result.Message = _l["srcxl.svc.folder_not_configured", adapter.ConfigSection].Value;
            return result;
        }

        var importNames = (request.ImportSheets ?? new List<string>()).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var deleteNames = (request.DeleteNames ?? new List<string>()).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (importNames.Count == 0 && deleteNames.Count == 0)
        {
            result.Message = _l["srcxl.svc.nothing_selected"].Value;
            return result;
        }

        // 勾選項目必須都在預覽內
        var aUnknown = importNames.Where(n => !session.Diffs.ContainsKey(n))
            .Concat(deleteNames.Where(n => !session.Deletable.Contains(n)))
            .ToList();
        if (aUnknown.Count > 0)
        {
            result.Message = _l["srcxl.svc.invalid_selection", string.Join(", ", aUnknown)].Value;
            return result;
        }

        // 有錯誤的工作表不可匯入；無變更的直接略過
        foreach (var szName in importNames)
        {
            if (session.Diffs[szName].Kind == SheetDiffKind.Error)
            {
                result.Message = _l["srcxl.svc.sheet_has_errors", szName].Value;
                return result;
            }
        }
        importNames = importNames.Where(n => session.Diffs[n].Kind != SheetDiffKind.Unchanged).ToList();

        // SID 位移需另外確認
        var aShift = importNames.Where(n => session.Diffs[n].HasSidShift).ToList();
        if (aShift.Count > 0 && !request.AcknowledgeSidShift)
        {
            result.Message = _l["srcxl.svc.sid_shift_unacknowledged", string.Join(", ", aShift)].Value;
            return result;
        }

        await _commitGate.WaitAsync();
        try
        {
            // 提交前重新比對：既有檔內容若與預覽當下不同 → 拒絕
            var aChanged = new List<string>();
            foreach (var szName in importNames.Concat(deleteNames))
            {
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, szName);
                var szNow = szPath != null && File.Exists(szPath) ? Fingerprint(szPath) : string.Empty;
                if (!string.Equals(session.Fingerprints.GetValueOrDefault(szName, string.Empty), szNow, StringComparison.Ordinal))
                    aChanged.Add(szName);
            }
            if (aChanged.Count > 0)
            {
                _cache.Remove(CACHE_PREFIX + request.Token);
                result.Conflict = true;
                result.Message = _l["srcxl.svc.changed_since_preview", string.Join(", ", aChanged)].Value;
                return result;
            }

            // 寫檔
            foreach (var szName in importNames)
            {
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, szName);
                if (szPath == null) continue;

                var sheet = session.Sheets[szName];
                var szJson = adapter.ToJson(sheet, session.WorkbookName);

                var encoding = adapter.NewFileEncoding;
                if (File.Exists(szPath))
                    (_, encoding) = await SourceConfigFileIo.ReadAllTextDetectEncodingAsync(szPath);

                SourceConfigFileIo.AtomicWrite(szPath, szJson, encoding);
                _io.MirrorWrite(adapter.ConfigSection, szName, szJson, encoding);
                result.Imported.Add(szName);
            }

            // 刪檔（移到 _deleted/）
            foreach (var szName in deleteNames)
            {
                if (_io.MoveToDeleted(adapter.ConfigSection, szName) != null)
                    result.Deleted.Add(szName);
            }

            _cache.Remove(CACHE_PREFIX + request.Token);
            result.Success = true;
            result.Message = _l["srcxl.svc.commit_success", result.Imported.Count, result.Deleted.Count].Value;

            _logger.LogInformation("Excel 匯入提交: {Kind} {File} by {User} — 匯入 [{Imported}] 刪除 [{Deleted}]",
                adapter.Kind, session.WorkbookName, szUsername,
                string.Join(", ", result.Imported), string.Join(", ", result.Deleted));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel 匯入提交失敗: {Kind} {File}", adapter.Kind, session.WorkbookName);
            result.Message = _l["srcxl.svc.write_failed", ex.Message].Value;
            return result;
        }
        finally
        {
            _commitGate.Release();
        }
    }

    // ───────────────────────────── 逐台刪除 ─────────────────────────────

    public async Task<SourceExcelCommitResult> DeleteAsync(ISourceExcelAdapter adapter, string szName, string szUsername)
    {
        var result = new SourceExcelCommitResult();
        szName = (szName ?? string.Empty).Trim();

        if (!SourceConfigFileIo.IsSafeName(szName))
        {
            result.Message = _l["srcxl.svc.delete_not_found", szName].Value;
            return result;
        }

        if (_io.GetWatchedFolder(adapter.ConfigSection) == null)
        {
            result.Message = _l["srcxl.svc.folder_not_configured", adapter.ConfigSection].Value;
            return result;
        }

        await _commitGate.WaitAsync();
        try
        {
            var szBackup = _io.MoveToDeleted(adapter.ConfigSection, szName);
            if (szBackup == null)
            {
                result.Message = _l["srcxl.svc.delete_not_found", szName].Value;
                return result;
            }

            result.Deleted.Add(szName);
            result.Success = true;
            result.Message = _l["srcxl.svc.delete_success", szName].Value;
            _logger.LogInformation("來源設定刪除: {Kind} {Name} by {User} → {Backup}", adapter.Kind, szName, szUsername, szBackup);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "來源設定刪除失敗: {Kind} {Name}", adapter.Kind, szName);
            result.Message = _l["srcxl.svc.write_failed", ex.Message].Value;
            return result;
        }
        finally
        {
            _commitGate.Release();
        }
    }

    // ───────────────────────────── 範本 / 匯出 / 一覽 ─────────────────────────────

    public byte[] BuildTemplate(ISourceExcelAdapter adapter) => SourceExcelTemplateWriter.BuildTemplate(adapter);

    /// <summary>匯出現行設定；szWorkbookFilter 非空時只匯出 SourceWorkbook 相同（忽略大小寫與副檔名）的設定</summary>
    public async Task<byte[]> ExportAsync(ISourceExcelAdapter adapter, string? szWorkbookFilter)
    {
        var szFolder = _io.GetWatchedFolder(adapter.ConfigSection);
        var sheets = new List<ParsedSourceSheet>();
        if (szFolder != null)
        {
            var szFilterKey = SourceExcelDiffBuilder.NormalizeWorkbookName(szWorkbookFilter);
            foreach (var szName in _io.ListConfigNames(adapter.ConfigSection))
            {
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, szName);
                if (szPath == null || !File.Exists(szPath)) continue;

                var (szJson, _) = await SourceConfigFileIo.ReadAllTextDetectEncodingAsync(szPath);
                var parsed = adapter.ParseJson(szName, szJson);
                if (parsed == null) continue;

                if (szFilterKey.Length > 0
                    && !string.Equals(SourceExcelDiffBuilder.NormalizeWorkbookName(parsed.SourceWorkbook), szFilterKey, StringComparison.Ordinal))
                    continue;

                sheets.Add(parsed);
            }
        }

        return SourceExcelTemplateWriter.Build(adapter, sheets);
    }

    /// <summary>既有設定一覽：名稱 + JSON 內 SourceWorkbook（讀不到 → null）</summary>
    public async Task<List<ExistingSourceInfo>> ListExistingAsync(ISourceExcelAdapter adapter)
    {
        var list = new List<ExistingSourceInfo>();
        var szFolder = _io.GetWatchedFolder(adapter.ConfigSection);
        if (szFolder == null) return list;

        foreach (var szName in _io.ListConfigNames(adapter.ConfigSection))
        {
            var info = new ExistingSourceInfo { Name = szName };
            try
            {
                var szPath = SourceConfigFileIo.ResolveWithin(szFolder, szName);
                if (szPath != null && File.Exists(szPath))
                {
                    var (szJson, _) = await SourceConfigFileIo.ReadAllTextDetectEncodingAsync(szPath);
                    if (JsonNode.Parse(szJson) is JsonObject root)
                        info.SourceWorkbook = root["SourceWorkbook"]?.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "讀取 SourceWorkbook 失敗: {Name}", szName);
            }
            list.Add(info);
        }
        return list;
    }

    /// <summary>既有設定名稱集合（頁面標示「設定檔已移除」用）</summary>
    public HashSet<string> GetExistingNames(ISourceExcelAdapter adapter)
        => new(_io.ListConfigNames(adapter.ConfigSection), StringComparer.OrdinalIgnoreCase);

    // ───────────────────────────── helpers ─────────────────────────────

    private void LocalizeIssues(List<SheetIssue> issues)
    {
        foreach (var issue in issues)
        {
            var szText = _l[issue.Key, issue.Args].Value;
            issue.Message = issue.Row > 0
                ? _l["srcxl.issue.format", issue.Row, issue.Column, szText].Value
                : szText;
        }
    }

    /// <summary>檔案內容 SHA-256（hex）</summary>
    private static string Fingerprint(string szPath)
    {
        using var stream = File.OpenRead(szPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
