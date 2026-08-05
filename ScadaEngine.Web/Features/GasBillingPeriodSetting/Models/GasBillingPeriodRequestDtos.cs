namespace ScadaEngine.Web.Features.GasBillingPeriodSetting.Models;

/// <summary>儲存單一氣費期別自訂起訖（日期格式 yyyy-MM-dd，時分固定 00:00）</summary>
public class GasBillingPeriodSaveRequest
{
    public int year { get; set; }
    public int month { get; set; }
    public DateTime start { get; set; }
    public DateTime end { get; set; }
}

/// <summary>指定單一氣費期別（還原預設 / 刪除此期 / 復原此期 共用）</summary>
public class GasBillingPeriodTargetRequest
{
    public int year { get; set; }
    public int month { get; set; }
}

/// <summary>氣費期別清單/區間查詢回傳項目（設定頁與用氣報表期別提示共用）</summary>
public class GasBillingPeriodItemDto
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

    /// <summary>與上一個「存在」期別的空窗（+N）/ 重疊（−N）天數，0 = 無縫接續（僅設定頁清單有值）</summary>
    public int gapDays { get; set; }
}

/// <summary>設定頁清單回應 — 該年實際存在的期別 + 已刪除（可復原）清單</summary>
public class GasBillingPeriodListDto
{
    /// <summary>實際存在的期別（兩月一期時只有 6 筆）</summary>
    public List<GasBillingPeriodItemDto> periods { get; set; } = new();

    /// <summary>已刪除的期別（摺疊區顯示，可按「復原」還原）；起訖為刪除當下的名目值</summary>
    public List<GasBillingPeriodItemDto> skipped { get; set; } = new();
}
