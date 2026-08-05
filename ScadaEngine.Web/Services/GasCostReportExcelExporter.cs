using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.GasCostReport.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 氣費報表 Excel 匯出 — 使用 ClosedXML，格式比照 WaterCostReportExcelExporter。
/// 表格為 期別 / 用氣量 (m³) / 套用方案 / 氣費（元）；資料不完整期別於期別欄加「*」註記。
/// 透過 IStringLocalizer 取得當前 culture 字串。
/// </summary>
public class GasCostReportExcelExporter
{
    private const string CostFormat = "#,##0.0";
    private const string M3Format = "#,##0.00";

    private readonly IStringLocalizer<GasCostReportExcelExporter> _l;

    public GasCostReportExcelExporter(IStringLocalizer<GasCostReportExcelExporter> localizer)
    {
        _l = localizer;
    }

    /// <summary>產出 .xlsx 二進位內容</summary>
    public byte[] Export(string szCircuitName, string szFromYm, string szToYm,
        List<GasCostPeriodRow> rows, string szOperator)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["excel.sheet_name"]);

        const int nLastCol = 4;
        var isAnyStale = rows.Any(r => r.isStale);

        // 標題區
        ws.Cell(1, 1).Value = _l["excel.title"].Value;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, nLastCol).Merge();

        // 查詢條件
        ws.Cell(3, 1).Value = _l["excel.label.circuit"].Value;
        ws.Cell(3, 2).Value = szCircuitName;
        ws.Cell(4, 1).Value = _l["excel.label.range"].Value;
        ws.Cell(4, 2).Value = $"{szFromYm} ~ {szToYm}";
        ws.Cell(5, 1).Value = _l["excel.label.query_time"].Value;
        ws.Cell(5, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(6, 1).Value = _l["excel.label.operator"].Value;
        ws.Cell(6, 2).Value = szOperator;
        ws.Cell(7, 1).Value = _l["excel.label.total_cost"].Value;
        ws.Cell(7, 2).Value = rows.Sum(r => r.totalCost);
        ws.Cell(7, 2).Style.NumberFormat.Format = CostFormat;

        for (var r = 3; r <= 7; r++)
        {
            ws.Range(r, 2, r, nLastCol).Merge();
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // 註記列：資料不完整期別說明
        var nDataStartRow = 9;
        if (isAnyStale)
        {
            ws.Cell(9, 1).Value = _l["excel.label.stale_note"].Value;
            ws.Cell(9, 1).Style.Font.FontColor = XLColor.DarkOrange;
            ws.Range(9, 1, 9, nLastCol).Merge();
            nDataStartRow = 11;
        }

        // 表頭
        ws.Cell(nDataStartRow, 1).Value = _l["excel.col.period"].Value;
        ws.Cell(nDataStartRow, 2).Value = _l["excel.col.m3"].Value;
        ws.Cell(nDataStartRow, 3).Value = _l["excel.col.plan"].Value;
        ws.Cell(nDataStartRow, 4).Value = _l["excel.col.cost"].Value;
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Font.Bold = true;
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        // 資料列
        for (var i = 0; i < rows.Count; i++)
        {
            var row = nDataStartRow + 1 + i;
            var data = rows[i];
            ws.Cell(row, 1).Value = data.isStale ? data.periodLabel + " *" : data.periodLabel;
            ws.Cell(row, 2).Value = data.totalM3;
            ws.Cell(row, 2).Style.NumberFormat.Format = M3Format;
            ws.Cell(row, 3).Value = data.planName;
            ws.Cell(row, 4).Value = data.totalCost;
            ws.Cell(row, 4).Style.NumberFormat.Format = CostFormat;
        }

        // 合計列
        var sumRow = nDataStartRow + 1 + rows.Count;
        ws.Cell(sumRow, 1).Value = _l["excel.row.total"].Value;
        ws.Cell(sumRow, 2).Value = rows.Sum(r => r.totalM3);
        ws.Cell(sumRow, 2).Style.NumberFormat.Format = M3Format;
        ws.Cell(sumRow, 4).Value = rows.Sum(r => r.totalCost);
        ws.Cell(sumRow, 4).Style.NumberFormat.Format = CostFormat;
        ws.Range(sumRow, 1, sumRow, nLastCol).Style.Font.Bold = true;
        ws.Range(sumRow, 1, sumRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightYellow;

        // 邊框
        var dataRange = ws.Range(nDataStartRow, 1, sumRow, nLastCol);
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();
        for (var c = 1; c <= nLastCol; c++)
        {
            if (ws.Column(c).Width < 18) ws.Column(c).Width = 18;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
