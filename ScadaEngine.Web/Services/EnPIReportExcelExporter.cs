using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// EnPI / 節能量報告 Excel 匯出（ClosedXML）— 標題/查詢條件區 + 逐 bucket 明細 + 合計列。
/// </summary>
public class EnPIReportExcelExporter
{
    private readonly IStringLocalizer<EnPIReportExcelExporter> _l;

    public EnPIReportExcelExporter(IStringLocalizer<EnPIReportExcelExporter> localizer)
    {
        _l = localizer;
    }

    public byte[] Export(EnPIReportResult result, string szOperator)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["excel.sheet_name"]);

        var nVarCount = result.variableLabels.Count;
        var nLastCol = 6 + nVarCount;   // 期間/實際/預測/節能量/累計節能量/EnPI + X 欄

        // 標題區
        ws.Cell(1, 1).Value = _l["excel.title"].Value;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, nLastCol).Merge();

        // 查詢條件
        ws.Cell(3, 1).Value = _l["excel.label.baseline"].Value;
        ws.Cell(3, 2).Value = result.szBaselineName;
        ws.Cell(4, 1).Value = _l["excel.label.target"].Value;
        ws.Cell(4, 2).Value = string.IsNullOrEmpty(result.szTargetUnit)
            ? result.szTargetLabel : $"{result.szTargetLabel} ({result.szTargetUnit})";
        ws.Cell(5, 1).Value = _l["excel.label.granularity"].Value;
        ws.Cell(5, 2).Value = result.szGranularity == "month"
            ? _l["excel.granularity.month"].Value : _l["excel.granularity.day"].Value;
        ws.Cell(6, 1).Value = _l["excel.label.range"].Value;
        ws.Cell(6, 2).Value = $"{result.dtStart:yyyy-MM-dd} ~ {result.dtEnd.AddDays(-1):yyyy-MM-dd}";
        ws.Cell(7, 1).Value = _l["excel.label.query_time"].Value;
        ws.Cell(7, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(8, 1).Value = _l["excel.label.operator"].Value;
        ws.Cell(8, 2).Value = szOperator;

        // 摘要
        ws.Cell(9, 1).Value = _l["excel.label.total_actual"].Value;
        ws.Cell(9, 2).Value = result.dTotalActual;
        ws.Cell(9, 2).Style.NumberFormat.Format = "#,##0.000";
        ws.Cell(10, 1).Value = _l["excel.label.total_predicted"].Value;
        ws.Cell(10, 2).Value = result.dTotalPredicted;
        ws.Cell(10, 2).Style.NumberFormat.Format = "#,##0.000";
        ws.Cell(11, 1).Value = _l["excel.label.total_savings"].Value;
        ws.Cell(11, 2).Value = result.dTotalSavings;
        ws.Cell(11, 2).Style.NumberFormat.Format = "#,##0.000";
        ws.Cell(12, 1).Value = _l["excel.label.overall_enpi"].Value;
        if (result.dOverallEnpi != null)
        {
            ws.Cell(12, 2).Value = result.dOverallEnpi.Value;
            ws.Cell(12, 2).Style.NumberFormat.Format = "0.0000";
        }
        if (result.nMissingCount > 0)
        {
            ws.Cell(13, 1).Value = _l["excel.warn.missing", result.nMissingCount].Value;
            ws.Cell(13, 1).Style.Font.FontColor = XLColor.OrangeRed;
            ws.Range(13, 1, 13, nLastCol).Merge();
        }

        // 表頭
        var nHeaderRow = 15;
        var headers = new List<string>
        {
            _l["excel.col.period"].Value,
            _l["excel.col.actual"].Value,
            _l["excel.col.predicted"].Value,
            _l["excel.col.savings"].Value,
            _l["excel.col.cum_savings"].Value,
            _l["excel.col.enpi"].Value,
        };
        headers.AddRange(result.variableLabels);
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(nHeaderRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
        }

        // 資料列
        var nRow = nHeaderRow + 1;
        foreach (var b in result.buckets)
        {
            ws.Cell(nRow, 1).Value = b.szLabel;
            if (b.isMissing)
            {
                ws.Cell(nRow, 2).Value = _l["excel.cell.missing"].Value;
                ws.Cell(nRow, 2).Style.Font.FontColor = XLColor.Gray;
            }
            else
            {
                SetNum(ws.Cell(nRow, 2), b.dActual);
                SetNum(ws.Cell(nRow, 3), b.dPredicted);
                SetNum(ws.Cell(nRow, 4), b.dSavings);
                SetNum(ws.Cell(nRow, 5), b.dCumulativeSavings);
                if (b.dEnpi != null)
                {
                    ws.Cell(nRow, 6).Value = b.dEnpi.Value;
                    ws.Cell(nRow, 6).Style.NumberFormat.Format = "0.0000";
                }
            }
            for (var j = 0; j < nVarCount && j < b.xValues.Count; j++)
                SetNum(ws.Cell(nRow, 7 + j), b.xValues[j]);
            nRow++;
        }

        // 合計列
        ws.Cell(nRow, 1).Value = _l["excel.row.total"].Value;
        ws.Cell(nRow, 1).Style.Font.Bold = true;
        SetNum(ws.Cell(nRow, 2), result.dTotalActual);
        SetNum(ws.Cell(nRow, 3), result.dTotalPredicted);
        SetNum(ws.Cell(nRow, 4), result.dTotalSavings);
        if (result.dOverallEnpi != null)
        {
            ws.Cell(nRow, 6).Value = result.dOverallEnpi.Value;
            ws.Cell(nRow, 6).Style.NumberFormat.Format = "0.0000";
        }
        ws.Range(nRow, 1, nRow, nLastCol).Style.Font.Bold = true;

        // 邊框 + 欄寬
        var table = ws.Range(nHeaderRow, 1, nRow, nLastCol);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Columns(1, nLastCol).AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void SetNum(IXLCell cell, double? dValue)
    {
        if (dValue == null) return;
        cell.Value = dValue.Value;
        cell.Style.NumberFormat.Format = "#,##0.000";
    }
}
