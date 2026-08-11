using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Web.Features.DailyReport.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Web.Features.DailyReport.Controllers;

[Authorize]
[Route("[controller]")]
public class DailyReportController : Controller
{
    private readonly DailyReportService _reportService;
    private readonly DailyReportMailService _mailService;
    private readonly ILogger<DailyReportController> _logger;

    public DailyReportController(
        DailyReportService reportService,
        DailyReportMailService mailService,
        ILogger<DailyReportController> logger)
    {
        _reportService = reportService;
        _mailService = mailService;
        _logger = logger;
    }

    [HttpGet("/DailyReport")]
    public IActionResult Index()
    {
        return View(new DailyReportViewModel());
    }

    /// <summary>日報設定頁 — 獨立頂層路由（PageAccessFilter 精確路徑比對 + Admin 可個別授權）</summary>
    [HttpGet("/DailyReportSetting")]
    public IActionResult Setting()
    {
        return View("Setting");
    }

    /// <summary>
    /// 取指定日期的日報 — 有快照讀快照（歷史日報不受資料回補影響）；
    /// 無快照即時計算（不落 DB，isSnapshot=false）。僅接受昨日（含）以前的日期。
    /// </summary>
    [HttpGet("api/report")]
    public async Task<IActionResult> GetReport([FromQuery] string date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtDate))
            return BadRequest(new { message = "日期格式須為 yyyy-MM-dd" });
        if (dtDate.Date >= DateTime.Today)
            return BadRequest(new { message = "僅提供昨日（含）以前的日報" });

        try
        {
            var meta = await _reportService.GetSnapshotMetaAsync(dtDate);
            var data = meta != null ? await _reportService.GetSnapshotDataAsync(dtDate) : null;
            if (data != null)
            {
                return Ok(new { isSnapshot = true, nMailStatus = (int)meta!.nMailStatus, szMailDetail = meta.szMailDetail, data });
            }

            // 無快照（或快照解析失敗）→ 即時計算，不落 DB
            var adhoc = await _reportService.BuildAsync(dtDate);
            return Ok(new { isSnapshot = false, nMailStatus = -1, szMailDetail = (string?)null, data = adhoc });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日報查詢失敗 date={Date}", date);
            return BadRequest(new { message = ex.Message });
        }
    }

    // ────────────────────────── 設定 ──────────────────────────

    [HttpGet("api/setting")]
    public async Task<IActionResult> GetSetting()
    {
        return Ok(await _reportService.GetSettingAsync());
    }

    [HttpPost("api/setting")]
    public async Task<IActionResult> SaveSetting([FromBody] DailyReportSettingModel setting)
    {
        try
        {
            await _reportService.SaveSettingAsync(setting);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日報設定儲存失敗");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ────────────────────────── 收件人 ──────────────────────────

    [HttpGet("api/recipients")]
    public async Task<IActionResult> GetRecipients()
    {
        return Ok(await _reportService.GetRecipientsAsync());
    }

    [HttpPost("api/recipients")]
    public async Task<IActionResult> SaveRecipient([FromBody] DailyReportRecipientModel recipient)
    {
        try
        {
            await _reportService.SaveRecipientAsync(recipient);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日報收件人儲存失敗");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("api/recipients/{id:int}")]
    public async Task<IActionResult> DeleteRecipient(int id)
    {
        await _reportService.DeleteRecipientAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("api/recipients/{id:int}/toggle")]
    public async Task<IActionResult> ToggleRecipient(int id)
    {
        await _reportService.ToggleRecipientAsync(id);
        return Ok(new { success = true });
    }

    // ────────────────────────── 測試寄送 ──────────────────────────

    /// <summary>
    /// 測試寄送 — 立即以「昨日」內容（快照優先，無快照即時計算）寄給所有啟用的收件人，
    /// 主旨帶【測試】標記；不受 IsMailEnabled 限制、不改快照 MailStatus。
    /// </summary>
    [HttpPost("api/test-send")]
    public async Task<IActionResult> TestSend()
    {
        var nRetryAfter = _mailService.CheckTestThrottle();
        if (nRetryAfter > 0)
        {
            Response.Headers["Retry-After"] = nRetryAfter.ToString();
            return StatusCode(429, new { success = false, message = $"測試寄送過於頻繁，請 {nRetryAfter} 秒後再試" });
        }

        try
        {
            var dtReportDate = DateTime.Today.AddDays(-1);
            var setting = await _reportService.GetSettingAsync();
            var recipients = await _reportService.GetRecipientsAsync();
            if (recipients.Count(r => r.isEnabled) == 0)
                return BadRequest(new { success = false, message = "無啟用的收件人，請先新增" });

            var data = await _reportService.GetSnapshotDataAsync(dtReportDate)
                       ?? await _reportService.BuildAsync(dtReportDate);
            var result = await _mailService.SendAsync(data, setting, recipients, isTest: true);
            if (result.nSuccess == 0)
                return BadRequest(new { success = false, message = result.szDetail });
            return Ok(new { success = true, message = result.szDetail });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日報測試寄送失敗");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
