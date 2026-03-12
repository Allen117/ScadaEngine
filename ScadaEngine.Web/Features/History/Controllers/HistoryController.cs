using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaEngine.Engine.Data.Interfaces;
using ScadaEngine.Web.Features.History.Models;

namespace ScadaEngine.Web.Features.History.Controllers;

[Authorize]
public class HistoryController : Controller
{
    private readonly IDataRepository _dataRepository;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(IDataRepository dataRepository, ILogger<HistoryController> logger)
    {
        _dataRepository = dataRepository;
        _logger = logger;
    }

    /// <summary>
    /// GET /History/Trend — 歷史趨勢頁面
    /// </summary>
    [HttpGet("/History/Trend")]
    public async Task<IActionResult> Trend()
    {
        var coordinatorList = (await _dataRepository.GetAllCoordinatorsAsync()).ToList();
        var pointList = (await _dataRepository.GetAllModbusPointsAsync()).ToList();

        var viewModel = new HistoryTrendViewModel
        {
            CoordinatorList = coordinatorList,
            PointList = pointList,
            dtStartTime = DateTime.Now.AddHours(-24),
            dtEndTime = DateTime.Now
        };

        return View(viewModel);
    }

    /// <summary>
    /// GET /api/history/data?szSID=xxx&szStart=...&szEnd=...
    /// 查詢 HistoryData 資料表並回傳 JSON，供前端 Chart.js 使用。
    /// </summary>
    [HttpGet("~/api/history/data")]
    public async Task<IActionResult> GetHistoryData(
        [FromQuery] string szSID,
        [FromQuery] string szStart,
        [FromQuery] string szEnd)
    {
        if (string.IsNullOrWhiteSpace(szSID))
            return BadRequest(new { success = false, message = "SID 不可為空" });

        if (!DateTime.TryParse(szStart, out var dtStart))
            dtStart = DateTime.Now.AddHours(-24);

        if (!DateTime.TryParse(szEnd, out var dtEnd))
            dtEnd = DateTime.Now;

        if (dtStart >= dtEnd)
            return BadRequest(new { success = false, message = "開始時間必須早於結束時間" });

        try
        {
            const int nMaxRecords = 5000;
            var historyData = (await _dataRepository.GetHistoryTableDataAsync(
                szSID, dtStart, dtEnd, nMaxRecords)).ToList();

            // 查詢點位名稱與單位
            var allPoints = await _dataRepository.GetAllModbusPointsAsync();
            var point = allPoints.FirstOrDefault(p => p.szSID == szSID);

            // 計算標準差
            double? dStdDev = null;
            if (historyData.Count > 1)
            {
                var dAvgVal = historyData.Average(d => d.fValue);
                dStdDev = Math.Sqrt(historyData.Average(d => Math.Pow(d.fValue - dAvgVal, 2)));
            }

            return Ok(new
            {
                success    = true,
                szSID      = szSID,
                szName     = point?.szName ?? szSID,
                szUnit     = point?.szUnit ?? "",
                nCount     = historyData.Count,
                isLimited  = historyData.Count >= nMaxRecords,
                dMin       = historyData.Count > 0 ? historyData.Min(d => d.fValue) : (double?)null,
                dMax       = historyData.Count > 0 ? historyData.Max(d => d.fValue) : (double?)null,
                dAvg       = historyData.Count > 0 ? historyData.Average(d => d.fValue) : (double?)null,
                dStdDev    = dStdDev,
                data       = historyData.Select(d => new
                {
                    t = d.dtTimestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
                    v = d.fValue,
                    q = d.nQuality == 1 ? "Good" : "Bad"
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢歷史資料失敗: SID={SID}", szSID);
            return StatusCode(500, new { success = false, message = "查詢失敗，請稍後再試" });
        }
    }
}
