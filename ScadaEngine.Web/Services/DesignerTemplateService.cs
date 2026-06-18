using System.Text.Json;
using ScadaEngine.Web.Features.Designer.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// Designer 列範本 JSON 檔案讀寫服務 — 單台機器、單一全域範本。
/// 首次啟動 / 檔案缺失 / 內容毀損 → 回退預設 V/A/KW/PF/KWH。
/// </summary>
public class DesignerTemplateService
{
    private readonly ILogger<DesignerTemplateService> _logger;
    private readonly SemaphoreSlim                    _fileLock = new(1, 1);
    private readonly string                           _szFilePath;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DesignerTemplateService(ILogger<DesignerTemplateService> logger)
    {
        _logger     = logger;
        _szFilePath = Path.Combine(AppContext.BaseDirectory, "Setting", "DesignerTemplates.json");
    }

    /// <summary>讀取範本（檔案不存在則建立預設並回傳）</summary>
    public async Task<DesignerTemplateFileDto> ReadAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_szFilePath))
            {
                var defaults = CreateDefaults();
                await WriteInternalAsync(defaults);
                return defaults;
            }

            try
            {
                var szJson = await File.ReadAllTextAsync(_szFilePath);
                var dto    = JsonSerializer.Deserialize<DesignerTemplateFileDto>(szJson, s_jsonOptions);
                if (dto == null || dto.arrRoles == null || dto.arrRoles.Count == 0)
                {
                    _logger.LogWarning("DesignerTemplates.json 內容無效，回退預設值");
                    return CreateDefaults();
                }
                if (string.IsNullOrEmpty(dto.szSeparator))
                {
                    dto.szSeparator = "-";
                }
                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 DesignerTemplates.json 失敗，回退預設值");
                return CreateDefaults();
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>整批覆寫範本</summary>
    public async Task<bool> WriteAsync(DesignerTemplateFileDto dto)
    {
        if (dto == null || dto.arrRoles == null || dto.arrRoles.Count == 0)
        {
            return false;
        }
        if (string.IsNullOrEmpty(dto.szSeparator))
        {
            dto.szSeparator = "-";
        }

        await _fileLock.WaitAsync();
        try
        {
            await WriteInternalAsync(dto);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入 DesignerTemplates.json 失敗");
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task WriteInternalAsync(DesignerTemplateFileDto dto)
    {
        var szDir = Path.GetDirectoryName(_szFilePath);
        if (!string.IsNullOrEmpty(szDir) && !Directory.Exists(szDir))
        {
            Directory.CreateDirectory(szDir);
        }
        var szJson = JsonSerializer.Serialize(dto, s_jsonOptions);
        await File.WriteAllTextAsync(_szFilePath, szJson);
    }

    private static DesignerTemplateFileDto CreateDefaults() => new()
    {
        szSeparator = "-",
        arrRoles    = new List<string> { "V", "A", "KW", "PF", "KWH" }
    };
}
