namespace ScadaEngine.Web.Features.WaterTariffSetting.Models;

/// <summary>
/// 水費設定整份組態 — 對應 SystemSettings.water_tariff 的 JSON value。
/// 台水預設值來自 Setting/water-tariff-taiwater-defaults.json（唯讀 seed），
/// 使用者修改整份存回 DB；seed 新增方案時載入端自動補齊（by szPlanId）。
/// 水費只做台水「流動水費」分段累進 — 不做基本費、不做 TOU、無季節；1 度 = 1 m³。
/// </summary>
public class WaterTariffConfig
{
    public List<WaterTariffPlan> plans { get; set; } = new();
}

/// <summary>
/// 單一水價方案版本 — 方案含生效日：計算某期水費時依「期別起日」選用當時生效的版本
/// （生效日 &lt;= 期別起日 中最新者；都不合則取生效日最早者），選版邏輯見 WaterTariffService.SelectPlanForDate。
/// </summary>
public class WaterTariffPlan
{
    /// <summary>方案穩定識別碼（如 taiwater-flow-default）</summary>
    public string szPlanId { get; set; } = "";

    /// <summary>方案顯示名稱（使用者輸入，不走 i18n）</summary>
    public string szName { get; set; } = "";

    /// <summary>生效日 yyyy-MM-dd（含當日）</summary>
    public string szEffectiveDate { get; set; } = "";

    /// <summary>分段累進級距（第一級 nFrom=1、級距連續、只有最後一級 nTo=null=「以上」）</summary>
    public List<WaterTariffTier> tiers { get; set; } = new();
}

/// <summary>累進級距一列 — 每期用水 [nFrom, nTo] 度（含），nTo = null 表「以上」；1 度 = 1 m³</summary>
public class WaterTariffTier
{
    public int nFrom { get; set; }
    public int? nTo { get; set; }

    /// <summary>每度單價（元/度）</summary>
    public double dPrice { get; set; }
}

/// <summary>水費設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class WaterTariffSettingViewModel
{
}
