namespace ScadaEngine.Web.Features.EnergyBaseline.Models;

/// <summary>基線模型新增/更新請求（id 為 null = 新增）</summary>
public class BaselineSaveDto
{
    public int? id { get; set; }
    public string name { get; set; } = string.Empty;

    /// <summary>circuit / point</summary>
    public string targetType { get; set; } = "circuit";
    public string? targetSid { get; set; }
    public int? targetCircuitId { get; set; }

    /// <summary>cumulative / average（circuit 一律 cumulative，後端強制）</summary>
    public string targetMode { get; set; } = "cumulative";
    public string targetLabel { get; set; } = string.Empty;
    public string? targetUnit { get; set; }

    /// <summary>day / month</summary>
    public string granularity { get; set; } = "day";
    public DateTime baselineStart { get; set; }
    public DateTime baselineEnd { get; set; }
    public string? description { get; set; }

    public List<BaselineVariableDto> variables { get; set; } = new();
}

/// <summary>基線相關變數 X 定義</summary>
public class BaselineVariableDto
{
    /// <summary>point / circuit</summary>
    public string varType { get; set; } = "point";
    public string? sourceSid { get; set; }
    public int? sourceCircuitId { get; set; }
    public string label { get; set; } = string.Empty;
    public string? unit { get; set; }
}

/// <summary>EnPI / 節能量報告查詢（匯出共用）</summary>
public class EnPIQueryDto
{
    public int baselineId { get; set; }
    public DateTime start { get; set; }
    public DateTime end { get; set; }
}

/// <summary>SEU 重大能源使用鑑別查詢</summary>
public class SeuQueryDto
{
    public DateTime start { get; set; }
    public DateTime end { get; set; }

    /// <summary>帕累托累計占比門檻（%），預設 80</summary>
    public double threshold { get; set; } = 80;
}
