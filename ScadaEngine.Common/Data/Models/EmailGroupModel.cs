namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// Email 通知群組 — 對應 EmailGroups 資料表
    /// </summary>
    public class EmailGroupModel
    {
        public int nId { get; set; }
        public string szName { get; set; } = string.Empty;
        public string szLabel { get; set; } = string.Empty;

        /// <summary>0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收</summary>
        public byte nMaxSeverity { get; set; } = 3;

        /// <summary>通知訊息語系：'zh-TW' 或 'en'</summary>
        public string szLanguage { get; set; } = "zh-TW";

        public bool isEnabled { get; set; } = true;
        public string? szRemarks { get; set; }
        public DateTime dtCreatedAt { get; set; }
        public DateTime? dtUpdatedAt { get; set; }
    }

    /// <summary>
    /// Email 收件人 — 對應 EmailRecipients 資料表
    /// </summary>
    public class EmailRecipientModel
    {
        public int nId { get; set; }
        public int nGroupId { get; set; }
        public string szEmailAddress { get; set; } = string.Empty;
        public string? szDisplayName { get; set; }
        public bool isEnabled { get; set; } = true;
        public DateTime dtCreatedAt { get; set; }
        public DateTime? dtUpdatedAt { get; set; }
    }
}
