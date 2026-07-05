namespace ScadaEngine.Web.Features.BillingPeriodSetting.Models;

/// <summary>儲存單一期別自訂起訖（日期格式 yyyy-MM-dd，時分固定 00:00）</summary>
public class BillingPeriodSaveRequest
{
    public int year { get; set; }
    public int month { get; set; }
    public DateTime start { get; set; }
    public DateTime end { get; set; }
}

/// <summary>還原單一期別為推導預設（刪除自訂 row）</summary>
public class BillingPeriodResetRequest
{
    public int year { get; set; }
    public int month { get; set; }
}

/// <summary>期別清單/區間查詢回傳項目（設定頁與報表期別提示共用）</summary>
public class BillingPeriodItemDto
{
    /// <summary>期別年份</summary>
    public int year { get; set; }

    /// <summary>期別月份 1–12</summary>
    public int month { get; set; }

    /// <summary>起始日 yyyy-MM-dd</summary>
    public string start { get; set; } = string.Empty;

    /// <summary>結束日 yyyy-MM-dd（含）</summary>
    public string end { get; set; } = string.Empty;

    /// <summary>期間天數（含頭尾）</summary>
    public int days { get; set; }

    /// <summary>是否使用者自訂（false = 推導預設）</summary>
    public bool isCustomized { get; set; }

    /// <summary>是否等同自然月</summary>
    public bool isNatural { get; set; }

    /// <summary>報表顯示標籤（自然月 yyyy-MM / 非自然月完整期間）</summary>
    public string label { get; set; } = string.Empty;

    /// <summary>與上期的空窗（+N）/ 重疊（−N）天數，0 = 無縫接續（僅設定頁清單有值）</summary>
    public int gapDays { get; set; }
}
