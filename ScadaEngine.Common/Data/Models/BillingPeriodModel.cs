namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 月結週期（期別）自訂 row — 對應 BillingPeriods 表一列。
/// 只存被使用者自訂過的期別；沒有 row 的期別由 BillingPeriodService 推導。
/// </summary>
public class BillingPeriodModel
{
    /// <summary>期別年份（= 起始所在月份的年，台電慣例）</summary>
    public int nPeriodYear { get; set; }

    /// <summary>期別月份 1–12</summary>
    public int nPeriodMonth { get; set; }

    /// <summary>起始日 00:00（含）</summary>
    public DateTime dtStartDate { get; set; }

    /// <summary>結束日 00:00（inclusive 語意 — 該日整天算入本期）</summary>
    public DateTime dtEndDate { get; set; }

    /// <summary>最後更新時間</summary>
    public DateTime dtUpdatedAt { get; set; }
}

/// <summary>
/// 單一期別的解析結果（自訂或推導）— 報表月粒度 bucket 的 [起, 訖) 邊界對。
/// 期別間可能有空窗或重疊（使用者選擇），因此不共用邊界點。
/// </summary>
public class BillingPeriodRange
{
    /// <summary>期別年份</summary>
    public int nYear { get; set; }

    /// <summary>期別月份 1–12</summary>
    public int nMonth { get; set; }

    /// <summary>期界起點 00:00（含）</summary>
    public DateTime dtStart { get; set; }

    /// <summary>期界終點 00:00（不含）= 結束日 + 1 天</summary>
    public DateTime dtEndExclusive { get; set; }

    /// <summary>結束日（含，顯示/編輯用）</summary>
    public DateTime dtEndInclusive => dtEndExclusive.AddDays(-1);

    /// <summary>該期是否有使用者自訂 row（false = 推導預設）</summary>
    public bool isCustomized { get; set; }

    /// <summary>是否等同自然月（1 日～最後一日）— 標籤維持 yyyy-MM</summary>
    public bool isNaturalMonth
    {
        get
        {
            var dtNatural = new DateTime(nYear, nMonth, 1);
            return dtStart == dtNatural && dtEndExclusive == dtNatural.AddMonths(1);
        }
    }

    /// <summary>顯示標籤：自然月 = yyyy-MM；非自然月 = yyyy-MM-dd~MM-dd（跨年右端帶年份）</summary>
    public string szLabel { get; set; } = string.Empty;
}
