using ClosedXML.Excel;
using ScadaEngine.Web.Features.Shared.Models;
using ScadaEngine.Web.Services.SourceExcel;

namespace ScadaEngine.Tests.SourceExcel;

/// <summary>
/// 往返測試：既有 JSON → 匯出 .xlsx → 再匯入 → 與既有比對必須是「無變更」（驗收條件）。
/// 另驗證空白範本可被解析（只缺點位），且 Address 欄為文字格式保留前導 0。
/// </summary>
public class SourceExcelRoundTripTests
{
    private static ParsedSourceSheet ParseFirstSheet(ISourceExcelAdapter adapter, byte[] xlsx)
    {
        using var ms = new MemoryStream(xlsx);
        using var wb = new XLWorkbook(ms);
        return adapter.ParseSheet(wb.Worksheets.First());
    }

    [Fact]
    public void Modbus_ExistingJson_Export_Import_Unchanged()
    {
        var adapter = new ModbusExcelAdapter();
        const string szJson = """
        {
          "IP": "192.168.147.1", "Port": 502, "ModbusId": "1,2,3", "ConnectTimeout": 500,
          "Tags": [
            { "Name": "V", "Address": "30513", "DataType": "SWAPPEDFP", "Ratio": "1", "Unit": "", "Min": "0", "Max": "300" },
            { "Name": "kWh \"總\"", "Address": "30001", "DataType": "UINT32BE", "Ratio": "0.1", "Unit": "kWh", "Min": "", "Max": "" },
            { "Name": "Coil6", "Address": "000001", "DataType": "INTEGER", "Ratio": "1", "Unit": "", "Min": "0", "Max": "1", "Device": "泵A" },
            { "Name": "Bit", "Address": "40010", "DataType": "BIT5", "Ratio": "1", "Unit": "", "Min": "0", "Max": "1", "Device": "" }
          ]
        }
        """;
        var existing = adapter.ParseJson("Modbus", szJson)!;

        var xlsx = SourceExcelTemplateWriter.Build(adapter, new[] { existing });
        var reimported = ParseFirstSheet(adapter, xlsx);

        Assert.True(reimported.IsValid, string.Join("; ", reimported.Errors.Select(e => $"{e.Row}{e.Column}:{e.Key}")));
        Assert.Equal("000001", reimported.Points[2].Get("Address"));   // 文字格式保留前導 0

        var diff = SourceExcelDiffBuilder.Build(reimported, existing, adapter);
        Assert.Equal(SheetDiffKind.Unchanged, diff.Kind);
    }

    [Fact]
    public void DbPoint_ExistingJson_Export_Import_Unchanged()
    {
        var adapter = new DbPointExcelAdapter();
        const string szJson = """
        {
          "Name": "DB1", "PollingInterval": 1000, "ConnectTimeout": 1000, "MonitorEnabled": false,
          "Points": [
            { "Name": "模擬度數", "Unit": "kWh", "Min": 0, "Max": 100 },
            { "Name": "CH1RUN", "Unit": "", "Min": -1.5, "Max": 99.25 }
          ]
        }
        """;
        var existing = adapter.ParseJson("DB1", szJson)!;

        var xlsx = SourceExcelTemplateWriter.Build(adapter, new[] { existing });
        var reimported = ParseFirstSheet(adapter, xlsx);

        Assert.True(reimported.IsValid, string.Join("; ", reimported.Errors.Select(e => $"{e.Row}{e.Column}:{e.Key}")));
        Assert.Equal("FALSE", reimported.Header["MonitorEnabled"]);

        var diff = SourceExcelDiffBuilder.Build(reimported, existing, adapter);
        Assert.Equal(SheetDiffKind.Unchanged, diff.Kind);
    }

    [Fact]
    public void Modbus_BlankTemplate_ParsesWithOnlyMissingIpAndNoPoints()
    {
        var adapter = new ModbusExcelAdapter();
        var xlsx = SourceExcelTemplateWriter.BuildTemplate(adapter);
        var sheet = ParseFirstSheet(adapter, xlsx);

        Assert.Equal(adapter.DefaultSheetName, sheet.SheetName);
        Assert.Equal("502", sheet.Header["Port"]);
        Assert.Equal("1", sheet.Header["ModbusId"]);
        Assert.Equal("1000", sheet.Header["ConnectTimeout"]);
        Assert.Equal(new[] { "srcxl.err.header_ip_required", "srcxl.err.no_points" }, sheet.Errors.Select(e => e.Key).OrderBy(k => k));
    }

    [Fact]
    public void DbPoint_BlankTemplate_ParsesWithOnlyNoPoints()
    {
        var adapter = new DbPointExcelAdapter();
        var xlsx = SourceExcelTemplateWriter.BuildTemplate(adapter);
        var sheet = ParseFirstSheet(adapter, xlsx);

        Assert.Equal("DB1", sheet.SheetName);
        Assert.Equal("TRUE", sheet.Header["MonitorEnabled"]);
        Assert.Equal("srcxl.err.no_points", Assert.Single(sheet.Errors).Key);
    }

    [Fact]
    public void Build_MultipleSheets_OnePerJson_InvalidNameSkipped()
    {
        var adapter = new DbPointExcelAdapter();
        var a = adapter.ParseJson("DB1", """{ "Points": [ { "Name": "P" } ] }""")!;
        var b = adapter.ParseJson("DB2", """{ "Points": [ { "Name": "Q" } ] }""")!;
        var bad = adapter.ParseJson("Bad[Name]", """{ "Points": [ { "Name": "R" } ] }""")!;

        var skipped = new List<string>();
        var xlsx = SourceExcelTemplateWriter.Build(adapter, new[] { a, b, bad }, skipped);

        using var ms = new MemoryStream(xlsx);
        using var wb = new XLWorkbook(ms);
        Assert.Equal(new[] { "DB1", "DB2" }, wb.Worksheets.Select(w => w.Name));
        Assert.Equal("Bad[Name]", Assert.Single(skipped));
    }
}
