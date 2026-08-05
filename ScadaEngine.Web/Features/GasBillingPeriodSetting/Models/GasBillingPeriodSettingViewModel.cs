namespace ScadaEngine.Web.Features.GasBillingPeriodSetting.Models;

/// <summary>氣費月結週期設定頁 ViewModel</summary>
public class GasBillingPeriodSettingViewModel
{
    /// <summary>頁面載入時預設顯示的年份（= 今年）</summary>
    public int nCurrentYear { get; set; } = DateTime.Today.Year;
}
