namespace ScadaEngine.Web.Features.HolidaySetting.Models;

/// <summary>國定假日設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class HolidaySettingViewModel
{
}

/// <summary>POST api/holidays — 整年批次覆蓋儲存</summary>
public class SaveHolidaysRequest
{
    public int year { get; set; }

    /// <summary>該年度所有標註日期（yyyy-MM-dd）；未列入者視為取消標註</summary>
    public List<string> dates { get; set; } = [];
}
