using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 用電報表 Excel 匯出 — 使用 ClosedXML（無 Office 依賴）。
/// 獨立檔案，便於日後抽換套件。
/// 透過 IStringLocalizer 取得當前 culture 的標題、表頭、警告等字串。
/// </summary>
public class EnergyReportExcelExporter
{
    /// <summary>kWh 儲存格格式 — 顯示小數一位（0 → 0.0），與頁面/日報/能源申報一致。儲存格內的值仍是全精度。</summary>
    private const string KwhFormat = "#,##0.0";

    /// <summary>增減% 格式 — 小數一位帶正負號與 % 符號（正;負;零 三段）。</summary>
    private const string PctFormat = "+0.0\"%\";-0.0\"%\";0.0\"%\"";

    /// <summary>去年同期/差異儲存格：有值填數字（KwhFormat），null 填 "--"。</summary>
    private static void SetKwhOrDash(IXLCell cell, double? dValue)
    {
        if (dValue.HasValue)
        {
            cell.Value = dValue.Value;
            cell.Style.NumberFormat.Format = KwhFormat;
        }
        else cell.Value = "--";
    }

    /// <summary>增減% 儲存格：有值填百分比數字，null 填 "--"。</summary>
    private static void SetPctOrDash(IXLCell cell, double? dValue)
    {
        if (dValue.HasValue)
        {
            cell.Value = dValue.Value;
            cell.Style.NumberFormat.Format = PctFormat;
        }
        else cell.Value = "--";
    }

    private readonly IStringLocalizer<EnergyReportExcelExporter> _l;

    public EnergyReportExcelExporter(IStringLocalizer<EnergyReportExcelExporter> localizer)
    {
        _l = localizer;
    }

    /// <summary>產出 .xlsx 二進位內容</summary>
    public byte[] Export(EnergyReportResult result, string szOperator)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["excel.sheet_name"]);

        var bHasChildren = result.children.Count > 0;
        var bYoy = result.isYoy;
        // 資料主欄（期別 + 本期 kWh + 各子迴路），YOY 三欄接在最後（去年同期 / 差異 / 增減%）
        var nBaseCols = bHasChildren ? 2 + result.children.Count : 2;
        var nYoyStartCol = nBaseCols + 1;
        var nLastCol = bYoy ? nBaseCols + 3 : nBaseCols;
        var bWide = bHasChildren || bYoy;

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
        ws.Cell(8, 1).Value = _l["excel.label.total_kwh"].Value;
        ws.Cell(8, 2).Value = result.dTotalKwh;
        ws.Cell(8, 2).Style.NumberFormat.Format = KwhFormat;

        if (bWide)
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
            // 向後相容：實體葉子或無子迴路時，欄名維持「用電量 (kWh)」
            ws.Cell(nDataStartRow, 2).Value = _l["excel.col.kwh"].Value;
        }
        else
        {
            // 父欄改用迴路名稱明確標示，避免與子欄混淆
            ws.Cell(nDataStartRow, 2).Value = _l["excel.col.with_circuit_kwh", result.szCircuitName].Value;
            for (var i = 0; i < result.children.Count; i++)
            {
                ws.Cell(nDataStartRow, 3 + i).Value = _l["excel.col.with_circuit_kwh", result.children[i].szName].Value;
            }
        }
        if (bYoy)
        {
            ws.Cell(nDataStartRow, nYoyStartCol).Value = _l["excel.col.lastyear_kwh"].Value;
            ws.Cell(nDataStartRow, nYoyStartCol + 1).Value = _l["excel.col.diff_kwh"].Value;
            ws.Cell(nDataStartRow, nYoyStartCol + 2).Value = _l["excel.col.pct_change"].Value;
        }
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Font.Bold = true;
        ws.Range(nDataStartRow, 1, nDataStartRow, nLastCol).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        // 資料列
        for (var i = 0; i < result.buckets.Count; i++)
        {
            var row = nDataStartRow + 1 + i;
            ws.Cell(row, 1).Value = result.buckets[i].szLabel;
            ws.Cell(row, 2).Value = result.buckets[i].dKwh;
            ws.Cell(row, 2).Style.NumberFormat.Format = KwhFormat;
            if (bHasChildren)
            {
                for (var c = 0; c < result.children.Count; c++)
                {
                    var dVal = i < result.children[c].dKwhPerBucket.Count ? result.children[c].dKwhPerBucket[i] : 0;
                    ws.Cell(row, 3 + c).Value = dVal;
                    ws.Cell(row, 3 + c).Style.NumberFormat.Format = KwhFormat;
                }
            }
            if (bYoy)
            {
                var b = result.buckets[i];
                SetKwhOrDash(ws.Cell(row, nYoyStartCol), b.dLastYearKwh);
                SetKwhOrDash(ws.Cell(row, nYoyStartCol + 1), b.dDiffKwh);
                SetPctOrDash(ws.Cell(row, nYoyStartCol + 2), b.dPctChange);
            }
        }

        // 合計列
        var sumRow = nDataStartRow + 1 + result.buckets.Count;
        ws.Cell(sumRow, 1).Value = _l["excel.row.total"].Value;
        ws.Cell(sumRow, 2).Value = result.dTotalKwh;
        ws.Cell(sumRow, 2).Style.NumberFormat.Format = KwhFormat;
        if (bHasChildren)
        {
            for (var c = 0; c < result.children.Count; c++)
            {
                ws.Cell(sumRow, 3 + c).Value = result.children[c].dTotalKwh;
                ws.Cell(sumRow, 3 + c).Style.NumberFormat.Format = KwhFormat;
            }
        }
        if (bYoy)
        {
            // 合計列 YOY 只比「有去年同期資料」的可比 bucket，避免把缺去年的期別灌進差異
            var matched = result.buckets.Where(b => b.dLastYearKwh.HasValue).ToList();
            if (matched.Count > 0)
            {
                var dCurMatched = Math.Round(matched.Sum(b => b.dKwh), 3);
                var dLastTotal = Math.Round(matched.Sum(b => b.dLastYearKwh!.Value), 3);
                var dDiffTotal = Math.Round(dCurMatched - dLastTotal, 3);
                SetKwhOrDash(ws.Cell(sumRow, nYoyStartCol), dLastTotal);
                SetKwhOrDash(ws.Cell(sumRow, nYoyStartCol + 1), dDiffTotal);
                SetPctOrDash(ws.Cell(sumRow, nYoyStartCol + 2),
                    dLastTotal == 0 ? (double?)null : Math.Round(dDiffTotal / Math.Abs(dLastTotal) * 100, 1));
            }
            else
            {
                SetKwhOrDash(ws.Cell(sumRow, nYoyStartCol), null);
                SetKwhOrDash(ws.Cell(sumRow, nYoyStartCol + 1), null);
                SetPctOrDash(ws.Cell(sumRow, nYoyStartCol + 2), null);
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
        "shift" => _l["excel.granularity.shift"].Value,
        _ => szGranularity
    };
}
