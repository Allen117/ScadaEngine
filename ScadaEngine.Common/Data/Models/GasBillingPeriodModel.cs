namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 氣費月結週期（期別）自訂 row — 對應 GasBillingPeriods 表一列。
/// 與 <see cref="BillingPeriodModel"/> 的唯一差異是多了 <see cref="isSkipped"/>：
/// 供氣事業抄表週期可能兩月一次，故允許把某一期「刪除」（其日數併入前一期）。
/// </summary>
public class GasBillingPeriodModel
{
    /// <summary>期別年份（= 起始所在月份的年）</summary>
    public int nPeriodYear { get; set; }

    /// <summary>期別月份 1–12</summary>
    public int nPeriodMonth { get; set; }

    /// <summary>起始日 00:00（含）。isSkipped=true 時保留刪除當下的原始推導值，供復原使用</summary>
    public DateTime dtStartDate { get; set; }

    /// <summary>結束日 00:00（inclusive 語意 — 該日整天算入本期）</summary>
    public DateTime dtEndDate { get; set; }

    /// <summary>
    /// true = 該期已被刪除（日數併入前一期），報表 / 設定頁皆不列出。
    /// 電費（BillingPeriods）與水費（WaterBillingPeriods）兩套刻意沒有這個概念。
    /// </summary>
    public bool isSkipped { get; set; }

    /// <summary>最後更新時間</summary>
    public DateTime dtUpdatedAt { get; set; }
}
