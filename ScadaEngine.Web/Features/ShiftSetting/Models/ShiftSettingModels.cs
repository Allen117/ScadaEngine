namespace ScadaEngine.Web.Features.ShiftSetting.Models;

/// <summary>
/// 班別設定整份 JSON — 存 SystemSettings.shift_schedule（仿電費 TOU electricity_tariff 模式）。
/// 單一現行版本，改動即對所有歷史查詢生效（班別是查詢時的檢視切法，非計價依據，不需生效時間軸）。
/// </summary>
public class ShiftScheduleConfig
{
    public List<ShiftDefinition> shifts { get; set; } = new();
}

/// <summary>
/// 單一班別 — 起訖限整點（水/氣報表資料源為 *MeterLeafHourly 逐時預聚合，僅整點粒度）。
/// nEndHour &lt;= nStartHour 視為跨日班（訖 = 隔日該時），用量歸屬起始日。
/// </summary>
public class ShiftDefinition
{
    /// <summary>班別名稱（顯示用，需唯一）</summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>起始整點 0~23</summary>
    public int nStartHour { get; set; }

    /// <summary>結束整點 0~23（exclusive）；&lt;= 起始時視為跨日。0 = 至隔日 00:00</summary>
    public int nEndHour { get; set; }

    /// <summary>適用星期：0=週日 .. 6=週六（對齊 .NET DayOfWeek）</summary>
    public List<int> weekdays { get; set; } = new();
}

/// <summary>
/// 班別粒度的 bucket 計畫 — 由 ShiftScheduleService 展開日期範圍產生，三個用量報表共用。
/// flatRanges 供計算核心（一段 [起, 訖)），displayXxx 供顯示；
/// 「非班別」補桶在同一天內可能是多段不連續時段 → 多個 flatRange 對映同一 display bucket，
/// groupOf[i] = flatRanges[i] 所屬的 display bucket 索引。
/// </summary>
public class ShiftBucketPlan
{
    /// <summary>計算用的扁平 [起, 訖) 區間列表</summary>
    public List<(DateTime dtStart, DateTime dtEnd)> flatRanges { get; } = new();

    /// <summary>flatRanges[i] 對映的 display bucket 索引</summary>
    public List<int> groupOf { get; } = new();

    /// <summary>顯示 bucket 的 [起, 訖)（跨多段時 = min 起 ~ max 訖）</summary>
    public List<(DateTime dtStart, DateTime dtEnd)> displayRanges { get; } = new();

    /// <summary>顯示 bucket 標籤，例如 "09/24 早班"、"非班別"</summary>
    public List<string> displayLabels { get; } = new();
}

/// <summary>班別設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class ShiftSettingViewModel
{
}
