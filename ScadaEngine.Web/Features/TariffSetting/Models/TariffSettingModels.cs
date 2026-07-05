namespace ScadaEngine.Web.Features.TariffSetting.Models;

/// <summary>
/// 電費設定整份組態 — 對應 SystemSettings.electricity_tariff 的 JSON value。
/// 台電預設值來自 Setting/tariff-taipower-defaults.json（唯讀 seed），
/// 使用者修改整份存回 DB；seed 新增方案時載入端自動補齊（by szPlanId）。
/// </summary>
public class TariffConfig
{
    /// <summary>目前採用方案 Id（空字串 = 尚未選擇）</summary>
    public string szActivePlanId { get; set; } = string.Empty;

    public List<TariffPlan> plans { get; set; } = [];
}

/// <summary>
/// 單一電價方案 — 統一模型容納台電三種型態：
/// progressive（累進級距）/ flat（單一費率+基本費）/ tou（時間電價時段+基本費）。
/// 方案與類別名稱走 i18n key（tariff.plan.{szPlanId} / tariff.category.{szCategory}），不存於 JSON。
/// </summary>
public class TariffPlan
{
    /// <summary>方案穩定識別碼（如 hv_tou2），同時是 i18n key 後綴</summary>
    public string szPlanId { get; set; } = string.Empty;

    /// <summary>類別：lighting（表燈住商）| lv（低壓電力）| hv（高壓電力）| ehv（特高壓電力）</summary>
    public string szCategory { get; set; } = string.Empty;

    /// <summary>型態：progressive | flat | tou</summary>
    public string szType { get; set; } = string.Empty;

    /// <summary>夏月起（月-日，如 06-01；含當日）</summary>
    public string szSummerStart { get; set; } = string.Empty;

    /// <summary>夏月訖（月-日，如 09-30；含當日）</summary>
    public string szSummerEnd { get; set; } = string.Empty;

    /// <summary>方案備註 i18n key（可變動尖峰/批次生產/EV 適用條件等），null = 無</summary>
    public string? szNoteKey { get; set; }

    /// <summary>累進級距（progressive 用，其餘型態為空陣列）</summary>
    public List<TariffTier> tiers { get; set; } = [];

    /// <summary>單一費率（flat 用，其餘 null）</summary>
    public TariffFlatRate? flatRate { get; set; }

    /// <summary>基本電費項目（tou / flat 用）</summary>
    public List<TariffBaseFee> baseFees { get; set; } = [];

    /// <summary>流動電費時段列（tou 用）</summary>
    public List<TariffFlowRate> flowRates { get; set; } = [];

    /// <summary>月總度數超額加價（簡易型限定），null = 無</summary>
    public TariffSurcharge? surcharge { get; set; }
}

/// <summary>累進級距一列 — 每月用電 [nFrom, nTo] 度（含），nTo = null 表「以上」</summary>
public class TariffTier
{
    public int nFrom { get; set; }
    public int? nTo { get; set; }
    /// <summary>夏月每度單價（元）</summary>
    public double dSummer { get; set; }
    /// <summary>非夏月每度單價（元）</summary>
    public double dNonSummer { get; set; }
}

/// <summary>單一費率（低壓電力非時間電價）— 流動電費每度單價</summary>
public class TariffFlatRate
{
    public double dSummer { get; set; }
    public double dNonSummer { get; set; }
}

/// <summary>
/// 基本電費項目 — szKey 為固定詞彙（i18n key = tariff.basefee.{szKey}）：
/// per_household / per_household_1p / per_household_3p / device_household / device_kw /
/// demand_household / regular_kw / nonsummer_kw / semipeak_kw / sat_semipeak_kw / offpeak_kw
/// </summary>
public class TariffBaseFee
{
    public string szKey { get; set; } = string.Empty;
    /// <summary>計價單位：household（每戶每月）| kw（每瓩每月）</summary>
    public string szUnit { get; set; } = string.Empty;
    /// <summary>夏月單價（元）；null = 該季不適用（顯示 —）</summary>
    public double? dSummer { get; set; }
    /// <summary>非夏月單價（元）；null = 該季不適用</summary>
    public double? dNonSummer { get; set; }
}

/// <summary>
/// 流動電費時段列 — 一列 = 日別 × 季節 × 時段別 的時間區間組與單價。
/// 驗證單位為（日別 × 季節）：組內各列 ranges 聯集須覆蓋 24h 且互不重疊（允許跨午夜）。
/// </summary>
public class TariffFlowRate
{
    /// <summary>日別：weekday（週一至週五）| sat（週六）| sun_offday（週日及離峰日）</summary>
    public string szDayType { get; set; } = string.Empty;

    /// <summary>季節：summer | nonsummer</summary>
    public string szSeason { get; set; } = string.Empty;

    /// <summary>時段別：peak | semipeak | offpeak（預設顯示名走 i18n tariff.period.{szPeriod}）</summary>
    public string szPeriod { get; set; } = string.Empty;

    /// <summary>使用者自訂區間名稱；null/空 = 用 szPeriod 的 i18n 預設名</summary>
    public string? szName { get; set; }

    /// <summary>時間區間（"HH:mm-HH:mm"，可多段；"24:00" 允許作為訖點；起 &gt; 訖 = 跨午夜）</summary>
    public List<string> ranges { get; set; } = [];

    /// <summary>每度單價（元）</summary>
    public double dPrice { get; set; }
}

/// <summary>月總度數超額加價（簡易型時間電價限定）</summary>
public class TariffSurcharge
{
    /// <summary>門檻度數（超過部分加價）</summary>
    public int nOverKwh { get; set; }
    /// <summary>每度加價（元）</summary>
    public double dPrice { get; set; }
}

// ---------- Request DTOs ----------

/// <summary>設為採用方案</summary>
public class SetActivePlanRequest
{
    public string planId { get; set; } = string.Empty;
}

/// <summary>還原單一方案為台電預設</summary>
public class ResetPlanRequest
{
    public string planId { get; set; } = string.Empty;
}

/// <summary>電費設定頁 ViewModel（目前無需傳值，保留擴充）</summary>
public class TariffSettingViewModel
{
}
