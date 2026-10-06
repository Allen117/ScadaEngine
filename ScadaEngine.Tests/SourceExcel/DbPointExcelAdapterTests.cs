using ClosedXML.Excel;
using ScadaEngine.Web.Services.SourceExcel;

namespace ScadaEngine.Tests.SourceExcel;

/// <summary>
/// DbPointExcelAdapter 解析 / 驗證 / JSON 序列化測試（plan 2026-10-06 決策 3、8）。
/// 版面：第 1 列 A~D 標題 + F1/G1=ConnectTimeout、H1/I1=PollingInterval、J1/K1=MonitorEnabled；第 2 列起 A~D = Name/Unit/Min/Max。
/// </summary>
public class DbPointExcelAdapterTests
{
    private readonly DbPointExcelAdapter _adapter = new();

    /// <summary>傳入此哨兵表示「該儲存格留白」（null = 用預設值）</summary>
    private static readonly object Blank = new();

    private static void SetCell(IXLWorksheet ws, string szAddress, object? value, object? szDefault)
    {
        if (ReferenceEquals(value, Blank)) return;
        var v = value ?? szDefault;
        if (v != null) ws.Cell(szAddress).Value = XLCellValue.FromObject(v);
    }

    private static (XLWorkbook wb, IXLWorksheet ws) NewSheet(string szName = "DB1", object? timeout = null, object? polling = null, object? monitor = null)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(szName);
        ws.Cell("A1").Value = "名稱"; ws.Cell("B1").Value = "單位"; ws.Cell("C1").Value = "最小值"; ws.Cell("D1").Value = "最大值";
        ws.Cell("F1").Value = "ConnectTimeout"; SetCell(ws, "G1", timeout, 1000);
        ws.Cell("H1").Value = "PollingInterval"; SetCell(ws, "I1", polling, 1000);
        ws.Cell("J1").Value = "MonitorEnabled"; SetCell(ws, "K1", monitor, null);
        return (wb, ws);
    }

    private static void Row(IXLWorksheet ws, int r, object? name, object? unit = null, object? min = null, object? max = null)
    {
        void Set(int c, object? v) { if (v != null) ws.Cell(r, c).Value = XLCellValue.FromObject(v); }
        Set(1, name); Set(2, unit); Set(3, min); Set(4, max);
    }

    [Fact]
    public void Parse_StandardLayout()
    {
        var (wb, ws) = NewSheet(timeout: 2000, polling: 5000, monitor: false);
        using (wb)
        {
            Row(ws, 2, "模擬度數", "kWh", 0, 100);
            Row(ws, 3, "CH1RUN", null, null, null);

            var sheet = _adapter.ParseSheet(ws);
            Assert.True(sheet.IsValid, string.Join("; ", sheet.Errors.Select(e => e.Key)));
            Assert.Equal("2000", sheet.Header["ConnectTimeout"]);
            Assert.Equal("5000", sheet.Header["PollingInterval"]);
            Assert.Equal("FALSE", sheet.Header["MonitorEnabled"]);
            Assert.Equal(2, sheet.Points.Count);
            Assert.Equal("kWh", sheet.Points[0].Get("Unit"));
            // 空白預設 0 / 100（與巨集相同）
            Assert.Equal("", sheet.Points[1].Get("Unit"));
            Assert.Equal("0", sheet.Points[1].Get("Min"));
            Assert.Equal("100", sheet.Points[1].Get("Max"));
        }
    }

    [Fact]
    public void Parse_BlankHeaderCells_Defaults_1000_1000_TRUE()
    {
        var (wb, ws) = NewSheet(timeout: Blank, polling: Blank, monitor: Blank);
        using (wb)
        {
            Row(ws, 2, "P1");
            var sheet = _adapter.ParseSheet(ws);
            Assert.True(sheet.IsValid);
            Assert.Equal("1000", sheet.Header["ConnectTimeout"]);
            Assert.Equal("1000", sheet.Header["PollingInterval"]);
            Assert.Equal("TRUE", sheet.Header["MonitorEnabled"]);
        }
    }

    [Fact]
    public void Parse_OldSheetWithLabelAtE1_FoundByScanning()
    {
        // 舊 xlsm 範例工作表：E1="ConnectTimeout" F1=1000（巨集讀 G1，但標籤掃描更穩）
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DB1");
        ws.Cell("E1").Value = "ConnectTimeout"; ws.Cell("F1").Value = 3000;
        Row(ws, 2, "P1");
        var sheet = _adapter.ParseSheet(ws);
        Assert.True(sheet.IsValid);
        Assert.Equal("3000", sheet.Header["ConnectTimeout"]);
    }

    [Theory]
    [InlineData("是", "TRUE")]
    [InlineData("1", "TRUE")]
    [InlineData("yes", "TRUE")]
    [InlineData("否", "FALSE")]
    [InlineData("0", "FALSE")]
    [InlineData("false", "FALSE")]
    public void Parse_MonitorEnabledVariants(string szInput, string szExpected)
    {
        var (wb, ws) = NewSheet(monitor: szInput);
        using (wb)
        {
            Row(ws, 2, "P1");
            var sheet = _adapter.ParseSheet(ws);
            Assert.True(sheet.IsValid);
            Assert.Equal(szExpected, sheet.Header["MonitorEnabled"]);
        }
    }

    [Fact]
    public void Parse_MonitorEnabledGarbage_Error()
    {
        var (wb, ws) = NewSheet(monitor: "maybe");
        using (wb)
        {
            Row(ws, 2, "P1");
            var sheet = _adapter.ParseSheet(ws);
            var err = Assert.Single(sheet.Errors);
            Assert.Equal("srcxl.err.header_monitor_invalid", err.Key);
            Assert.Equal("K", err.Column);
        }
    }

    [Fact]
    public void Parse_Over100Points_ErrorAtRow102()
    {
        var (wb, ws) = NewSheet();
        using (wb)
        {
            for (var i = 0; i < 101; i++) Row(ws, 2 + i, $"P{i + 1}");
            var sheet = _adapter.ParseSheet(ws);
            var err = Assert.Single(sheet.Errors);
            Assert.Equal("srcxl.err.too_many_points", err.Key);
            Assert.Equal(102, err.Row);
            Assert.Equal(100, sheet.Points.Count);
        }
    }

    [Fact]
    public void Parse_NonNumericMax_ErrorAtColumnD()
    {
        var (wb, ws) = NewSheet();
        using (wb)
        {
            Row(ws, 2, "P1", "", 0, "x");
            var sheet = _adapter.ParseSheet(ws);
            var err = Assert.Single(sheet.Errors);
            Assert.Equal("srcxl.err.numeric_required", err.Key);
            Assert.Equal("D", err.Column);
        }
    }

    [Fact]
    public void Parse_NameTooLong_Error()
    {
        var (wb, ws) = NewSheet();
        using (wb)
        {
            Row(ws, 2, new string('A', 101));
            var sheet = _adapter.ParseSheet(ws);
            Assert.Contains(sheet.Errors, e => e.Key == "srcxl.err.name_too_long" && e.Column == "A");
        }
    }

    [Fact]
    public void Parse_BlankNameRow_SkippedWithWarning()
    {
        var (wb, ws) = NewSheet();
        using (wb)
        {
            Row(ws, 2, "P1");
            Row(ws, 3, null, "kWh");
            Row(ws, 4, "P3");
            var sheet = _adapter.ParseSheet(ws);
            Assert.True(sheet.IsValid);
            Assert.Equal(2, sheet.Points.Count);
            Assert.Equal(3, Assert.Single(sheet.Warnings).Row);
        }
    }

    [Fact]
    public void ToJson_NumericMinMax_And_NameFromSheet()
    {
        var (wb, ws) = NewSheet(timeout: 1500, polling: 2000, monitor: true);
        using (wb)
        {
            Row(ws, 2, "P\"1\"", "kWh", 0, 100.5);
            var sheet = _adapter.ParseSheet(ws);
            var szJson = _adapter.ToJson(sheet, "DB來源.xlsx");

            using var doc = System.Text.Json.JsonDocument.Parse(szJson);
            var root = doc.RootElement;
            Assert.Equal("DB1", root.GetProperty("Name").GetString());
            Assert.Equal(2000, root.GetProperty("PollingInterval").GetInt32());
            Assert.Equal(1500, root.GetProperty("ConnectTimeout").GetInt32());
            Assert.True(root.GetProperty("MonitorEnabled").GetBoolean());
            Assert.Equal("DB來源.xlsx", root.GetProperty("SourceWorkbook").GetString());
            var pt = root.GetProperty("Points")[0];
            Assert.Equal("P\"1\"", pt.GetProperty("Name").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Number, pt.GetProperty("Min").ValueKind);
            Assert.Equal(0, pt.GetProperty("Min").GetDouble());
            Assert.Equal(100.5, pt.GetProperty("Max").GetDouble());
        }
    }

    [Fact]
    public void ParseJson_ExistingFile_Normalizes()
    {
        const string szJson = """
        {
          "Name": "DB1",
          "PollingInterval": 1000,
          "ConnectTimeout": 1000,
          "MonitorEnabled": true,
          "Points": [
            { "Name": "模擬度數", "Unit": "kWh", "Min": 0, "Max": 100 },
            { "Name": "CH1RUN", "Unit": "", "Min": 0.0, "Max": 100.0 }
          ]
        }
        """;
        var sheet = _adapter.ParseJson("DB1", szJson);
        Assert.NotNull(sheet);
        Assert.Null(sheet!.SourceWorkbook);
        Assert.Equal("TRUE", sheet.Header["MonitorEnabled"]);
        Assert.Equal("0", sheet.Points[1].Get("Min"));
        Assert.Equal("100", sheet.Points[1].Get("Max"));
    }

    [Fact]
    public void ToJson_ThenParseJson_RoundTrips()
    {
        var (wb, ws) = NewSheet(monitor: "否");
        using (wb)
        {
            Row(ws, 2, "P1", "kWh", 0, 100);
            Row(ws, 3, "P2", "", -5.5, 99.9);
            var parsed = _adapter.ParseSheet(ws);
            var back = _adapter.ParseJson("DB1", _adapter.ToJson(parsed, null));
            Assert.NotNull(back);
            Assert.Equal(parsed.Header, back!.Header);
            for (var i = 0; i < parsed.Points.Count; i++)
                Assert.Equal(parsed.Points[i].Fields, back.Points[i].Fields);
        }
    }
}
