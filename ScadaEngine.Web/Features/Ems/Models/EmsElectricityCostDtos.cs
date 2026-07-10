namespace ScadaEngine.Web.Features.Ems.Models;

/// <summary>
/// GET /EMS/api/electricity-cost 回應 — 電費狀態卡（依採用方案型態自適應）。
/// 金額一律只含流動電費（不含基本電費）；progressive / surcharge 金額在子迴路為 kWh 占比分攤估算（isEstimated=true）。
/// </summary>
public class EmsElectricityCostDto
{
    /// <summary>是否已選採用方案（false 時前端顯示「尚未選擇電費方案」提示）</summary>
    public bool hasPlan { get; set; }

    /// <summary>是否有可查詢的迴路（未建任何 EnergyCircuit 時 false）</summary>
    public bool hasCircuit { get; set; }

    /// <summary>採用方案 Id（前端 i18n key：tariff.plan.{planId}）</summary>
    public string planId { get; set; } = string.Empty;

    /// <summary>tou / flat / progressive</summary>
    public string planType { get; set; } = string.Empty;

    /// <summary>方案類別（前端 i18n key：tariff.category.{planCategory}）</summary>
    public string planCategory { get; set; } = string.Empty;

    public int circuitId { get; set; }
    public string circuitName { get; set; } = string.Empty;

    /// <summary>查詢迴路是否為根迴路（主要電表）— false 且方案含級距/加價時金額為估算</summary>
    public bool isRootCircuit { get; set; }

    /// <summary>級距 / surcharge 金額是否為占比分攤估算（子迴路）</summary>
    public bool isEstimated { get; set; }

    /// <summary>本期期別標籤（BillingPeriodService.BuildLabel）</summary>
    public string periodLabel { get; set; } = string.Empty;

    /// <summary>資料已計算至的小時（yyyy-MM-dd HH:00）；本期無資料時 null</summary>
    public string? lastHour { get; set; }

    /// <summary>本期累計 kWh（已套 EffectiveSign）</summary>
    public double totalKwh { get; set; }

    /// <summary>本期累計流動電費（元）；無資料時 0</summary>
    public double? totalCost { get; set; }

    /// <summary>今日小計 kWh</summary>
    public double todayKwh { get; set; }

    /// <summary>今日小計電費（元）；progressive 無法歸屬單日 → null</summary>
    public double? todayCost { get; set; }

    /// <summary>tou 方案：各時段（尖峰/半尖峰/離峰）明細，依方案定義排序</summary>
    public List<EmsCostPeriodItemDto> periods { get; set; } = new();

    /// <summary>progressive 方案：級距落點資訊</summary>
    public EmsCostProgressiveDto? progressive { get; set; }

    /// <summary>flat 方案：當季單價</summary>
    public EmsCostFlatDto? flat { get; set; }

    /// <summary>簡易型 tou 的月總度數超額加價（超過門檻才出現）</summary>
    public EmsCostSurchargeDto? surcharge { get; set; }
}

/// <summary>tou 時段明細一列</summary>
public class EmsCostPeriodItemDto
{
    /// <summary>peak / semipeak / offpeak（前端 i18n key：tariff.period.{period}）</summary>
    public string period { get; set; } = string.Empty;
    public double kwh { get; set; }
    public double cost { get; set; }
}

/// <summary>progressive 級距落點</summary>
public class EmsCostProgressiveDto
{
    /// <summary>目前落點級距（0-based；總量 0 時 0）</summary>
    public int tierIndex { get; set; }
    public int tierFrom { get; set; }
    /// <summary>null = 最後一級（以上）</summary>
    public int? tierTo { get; set; }
}

/// <summary>flat 當季單價</summary>
public class EmsCostFlatDto
{
    /// <summary>summer / nonsummer（依今日判定）</summary>
    public string season { get; set; } = string.Empty;
    public double unitPrice { get; set; }
}

/// <summary>月總度數超額加價</summary>
public class EmsCostSurchargeDto
{
    public int overKwh { get; set; }
    public double amount { get; set; }
}
