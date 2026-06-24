using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 冷凍噸報表 Excel 匯出 — 使用 ClosedXML（無 Office 依賴）。
/// 對標 <see cref="EnergyReportExcelExporter"/>，差異：欄位 kWh → RT·h、無溢位警告語意。
/// </summary>
public class RefrigerationTonReportExcelExporter
{
    private readonly IStringLocalizer<RefrigerationTonReportExcelExporter> _l;

    public RefrigerationTonReportExcelExporter(IStringLocalizer<RefrigerationTonReportExcelExporter> localizer)
    {
        _l = localizer;
    }

    public byte[] Export(RefrigerationTonReportResult result, string szOperator)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["excel.sheet_name"]);

        var bHasChildren = result.children.Count > 0;
        var nLastCol = bHasChildren ? 2 + result.children.Count : 2;

        // 標題區
        ws.Cell(1, 1).Value = _l["excel.title"].Value;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, nLastCol).Merge();

        // 查詢條件
        ws.Cell(3, 1).Value = _l["excel.label.circuit"].Value;
        ws.Cell(3, 2).Value = result.szCircuitName;
        ws.Cell(4, 1).Value = _l["excel.label.granularity"].Value;
        ws.Cell(4, 2).Value = LocalizeGranularity(result.szGranularity);
        ws.Cell(5, 1).Value = _l["excel.label.range"].Value;
        ws.Cell(5, 2).Value = $"{result.dtStart:yyyy-MM-dd HH:mm} ~ {result.dtEnd:yyyy-MM-dd HH:mm}";
        ws.Cell(6, 1).Value = _l["excel.label.query_time"].Value;
        ws.Cell(6, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(7, 1).Value = _l["excel.label.operator"].Value;
        ws.Cell(7, 2).Value = szOperator;
        ws.Cell(8, 1).Value = _l["excel.label.total_rt_hour"].Value;
        ws.Cell(8, 2).Value = result.dTotalRtHour;
        ws.Cell(8, 2).Style.NumberFormat.Format = "#,##0.000";

        if (bHasChildren)
        {
            for (var r = 3; r <= 8; r++) ws.Range(r, 2, r, nLastCol).Merge();
        }

        for (var r = 3; r <= 8; r++)
        {
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // 警告列
        var nDataStartRow = 10;
        if (result.isHasWarning)
        {
            ws.Cell(9, 1).Value = _l["excel.label.warning"].Value;
            ws.Cell(9, 1).Style.Font.FontColor = XLColor.Red;
            ws.Range(9, 1, 9, nLastCol).Merge();
            nDataStartRow = 11;
        }

        // 表頭
        ws.Cell(nDataStartRow, 1).Value = _l["excel.col.period"].Value;
        if (!bHasChildren)
        {
            ws.Cell(nDataStartRow, 2).Value = _l["excel.col.rt_hour"].Value;
        }
        else
        {
            ws.Cell(nDataStartRow, 2).Value = _l["excel.col.with_circuit_rt_hour", result.szCircuitName].Value;
            for (var i = 0; i < result.children.Count; i++)
            {
                ws.Cell(nDataStartRow, 3 + i).Value = _l["excel.col.with_circuit_rt_hour", result.children[i].szName].Value;
            }
        }
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Font.Bold = true;
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        // 資料列
        for (var i = 0; i < result.buckets.Count; i++)
        {
            var row = nDataStartRow + 1 + i;
            ws.Cell(row, 1).Value = result.buckets[i].szLabel;
            ws.Cell(row, 2).Value = result.buckets[i].dRtHour;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.000";
            if (bHasChildren)
            {
                for (var c = 0; c < result.children.Count; c++)
                {
                    var dVal = i < result.children[c].dRtHourPerBucket.Count ? result.children[c].dRtHourPerBucket[i] : 0;
                    ws.Cell(row, 3 + c).Value = dVal;
                    ws.Cell(row, 3 + c).Style.NumberFormat.Format = "#,##0.000";
                }
            }
        }

        // 合計列
        var sumRow = nDataStartRow + 1 + result.buckets.Count;
        ws.Cell(sumRow, 1).Value = _l["excel.row.total"].Value;
        ws.Cell(sumRow, 2).Value = result.dTotalRtHour;
        ws.Cell(sumRow, 2).Style.NumberFormat.Format = "#,##0.000";
        if (bHasChildren)
        {
            for (var c = 0; c < result.children.Count; c++)
            {
                ws.Cell(sumRow, 3 + c).Value = result.children[c].dTotalRtHour;
                ws.Cell(sumRow, 3 + c).Style.NumberFormat.Format = "#,##0.000";
            }
        }
        ws.Range(sumRow, 1, sumRow, nLastCol).Style.Font.Bold = true;
        ws.Range(sumRow, 1, sumRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightYellow;

        // 邊框
        var dataRange = ws.Range(nDataStartRow, 1, sumRow, nLastCol);
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();
        if (ws.Column(1).Width < 18) ws.Column(1).Width = 18;
        for (var c = 2; c <= nLastCol; c++)
        {
            if (ws.Column(c).Width < 18) ws.Column(c).Width = 18;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private string LocalizeGranularity(string szGranularity) => szGranularity switch
    {
        "hour" => _l["excel.granularity.hour"].Value,
        "day" => _l["excel.granularity.day"].Value,
        "month" => _l["excel.granularity.month"].Value,
        "year" => _l["excel.granularity.year"].Value,
        _ => szGranularity
    };
}
