namespace ScadaEngine.Web.Features.GasTariffSetting.Models;

/// <summary>
/// 氣費設定整份組態 — 對應 SystemSettings.gas_tariff 的 JSON value。
/// 預設值來自 Setting/gas-tariff-defaults.json（唯讀 seed，**空白單一級距範本、單價 0**）—
/// 天然氣無全國統一費率（依供氣事業合約而定），使用者需照帳單自行輸入級距與單價。
/// 只做「流動氣費」分段累進 — 不做基本費、不做 TOU、無季節、不做熱值換算；1 度 = 1 m³。
/// </summary>
public class GasTariffConfig
{
    public List<GasTariffPlan> plans { get; set; } = new();
}

/// <summary>
/// 單一氣價方案版本 — 方案含生效日：計算某期氣費時依「期別起日」選用當時生效的版本
/// （生效日 &lt;= 期別起日 中最新者；都不合則取生效日最早者），選版邏輯見 GasTariffService.SelectPlanForDate。
/// </summary>
public class GasTariffPlan
{
    /// <summary>方案穩定識別碼（如 gas-flow-default）</summary>
    public string szPlanId { get; set; } = "";

    /// <summary>方案顯示名稱（使用者輸入，不走 i18n）</summary>
    public string szName { get; set; } = "";

    /// <summary>生效日 yyyy-MM-dd（含當日）</summary>
    public string szEffectiveDate { get; set; } = "";

    /// <summary>
    /// 分段累進級距（第一級 nFrom=1、級距連續、只有最後一級 nTo=null=「以上」）。
    /// ⚠️ 級距與期別長度**完全解耦** — 兩月一期時請直接照帳單填「一期」的級距數字，
    ///    系統不做任何依期別月數的倍率換算（決策 7）。
    /// </summary>
    public List<GasTariffTier> tiers { get; set; } = new();
}

/// <summary>累進級距一列 — 每期用氣 [nFrom, nTo] 度（含），nTo = null 表「以上」；1 度 = 1 m³</summary>
public class GasTariffTier
{
    public int nFrom { get; set; }
    public int? nTo { get; set; }

    /// <summary>每度單價（元/度）</summary>
    public double dPrice { get; set; }
}

/// <summary>氣費設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class GasTariffSettingViewModel
{
}
