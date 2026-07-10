using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using ScadaEngine.Web.Features.ElectricityCostReport.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 電費報表 Excel 匯出 — 使用 ClosedXML，格式比照 EnergyReportExcelExporter。
/// 表格為 期間 / kWh / 電費（元），子迴路多欄展開時每子迴路一欄電費。
/// 透過 IStringLocalizer 取得當前 culture 字串。
/// </summary>
public class ElectricityCostReportExcelExporter
{
    private const string CostFormat = "#,##0.0";
    private const string KwhFormat = "#,##0.00";

    private readonly IStringLocalizer<ElectricityCostReportExcelExporter> _l;

    public ElectricityCostReportExcelExporter(IStringLocalizer<ElectricityCostReportExcelExporter> localizer)
    {
        _l = localizer;
    }

    /// <summary>產出 .xlsx 二進位內容</summary>
    public byte[] Export(CostReportResult result, string szOperator)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["excel.sheet_name"]);

        var bHasChildren = result.children.Count > 0;
        // 欄配置：1=期間、2=kWh、3=父迴路電費、4..=子迴路電費
        var nLastCol = 3 + result.children.Count;

        // 標題區
        ws.Cell(1, 1).Value = _l["excel.title"].Value;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, nLastCol).Merge();

        // 查詢條件
        ws.Cell(3, 1).Value = _l["excel.label.circuit"].Value;
        ws.Cell(3, 2).Value = result.circuitName;
        ws.Cell(4, 1).Value = _l["excel.label.granularity"].Value;
        ws.Cell(4, 2).Value = LocalizeGranularity(result.granularity);
        ws.Cell(5, 1).Value = _l["excel.label.range"].Value;
        ws.Cell(5, 2).Value = $"{result.start:yyyy-MM-dd HH:mm} ~ {result.end:yyyy-MM-dd HH:mm}";
        ws.Cell(6, 1).Value = _l["excel.label.query_time"].Value;
        ws.Cell(6, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(7, 1).Value = _l["excel.label.operator"].Value;
        ws.Cell(7, 2).Value = szOperator;
        ws.Cell(8, 1).Value = _l["excel.label.total_cost"].Value;
        ws.Cell(8, 2).Value = result.totalCost;
        ws.Cell(8, 2).Style.NumberFormat.Format = CostFormat;

        for (var r = 3; r <= 8; r++)
        {
            ws.Range(r, 2, r, nLastCol).Merge();
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // 註記列：不含基本電費（+ 級距分攤估算註記）
        var nDataStartRow = 10;
        ws.Cell(9, 1).Value = result.isEstimated
            ? $"{_l["excel.label.exclude_base"].Value}；{_l["excel.label.estimated"].Value}"
            : _l["excel.label.exclude_base"].Value;
        ws.Cell(9, 1).Style.Font.FontColor = XLColor.DarkOrange;
        ws.Range(9, 1, 9, nLastCol).Merge();
        nDataStartRow = 11;

        // 表頭
        ws.Cell(nDataStartRow, 1).Value = _l["excel.col.period"].Value;
        ws.Cell(nDataStartRow, 2).Value = _l["excel.col.kwh"].Value;
        if (!bHasChildren)
        {
            ws.Cell(nDataStartRow, 3).Value = _l["excel.col.cost"].Value;
        }
        else
        {
            ws.Cell(nDataStartRow, 3).Value = _l["excel.col.with_circuit_cost", result.circuitName].Value;
            for (var i = 0; i < result.children.Count; i++)
            {
                ws.Cell(nDataStartRow, 4 + i).Value = _l["excel.col.with_circuit_cost", result.children[i].name].Value;
            }
        }
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Font.Bold = true;
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        // 資料列
        for (var i = 0; i < result.buckets.Count; i++)
        {
            var row = nDataStartRow + 1 + i;
            ws.Cell(row, 1).Value = result.buckets[i].label;
            ws.Cell(row, 2).Value = result.buckets[i].kwh;
            ws.Cell(row, 2).Style.NumberFormat.Format = KwhFormat;
            ws.Cell(row, 3).Value = result.buckets[i].cost;
            ws.Cell(row, 3).Style.NumberFormat.Format = CostFormat;
            for (var c = 0; c < result.children.Count; c++)
            {
                var dVal = i < result.children[c].costPerBucket.Count ? result.children[c].costPerBucket[i] : 0;
                ws.Cell(row, 4 + c).Value = dVal;
                ws.Cell(row, 4 + c).Style.NumberFormat.Format = CostFormat;
            }
        }

        // 合計列
        var sumRow = nDataStartRow + 1 + result.buckets.Count;
        ws.Cell(sumRow, 1).Value = _l["excel.row.total"].Value;
        ws.Cell(sumRow, 2).Value = result.totalKwh;
        ws.Cell(sumRow, 2).Style.NumberFormat.Format = KwhFormat;
        ws.Cell(sumRow, 3).Value = result.totalCost;
        ws.Cell(sumRow, 3).Style.NumberFormat.Format = CostFormat;
        for (var c = 0; c < result.children.Count; c++)
        {
            ws.Cell(sumRow, 4 + c).Value = result.children[c].totalCost;
            ws.Cell(sumRow, 4 + c).Style.NumberFormat.Format = CostFormat;
        }
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

    private string LocalizeGranularity(string szGranularity) => szGranularity switch
    {
        "hour" => _l["excel.granularity.hour"].Value,
        "day" => _l["excel.granularity.day"].Value,
        "month" => _l["excel.granularity.month"].Value,
        "year" => _l["excel.granularity.year"].Value,
        _ => szGranularity
    };
}
