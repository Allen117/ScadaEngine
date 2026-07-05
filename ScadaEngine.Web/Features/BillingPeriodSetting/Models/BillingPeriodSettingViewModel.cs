namespace ScadaEngine.Web.Features.BillingPeriodSetting.Models;

/// <summary>月結週期設定頁 ViewModel</summary>
public class BillingPeriodSettingViewModel
{
    /// <summary>頁面載入時預設顯示的年份（= 今年）</summary>
    public int nCurrentYear { get; set; } = DateTime.Today.Year;
}
