namespace ScadaEngine.Web.Features.AlarmSetting.Models
{
    /// <summary>SMTP 寄件者設定 — 對應 EmailSetting.json</summary>
    public class EmailSenderConfigDto
    {
        public bool enableNotification { get; set; } = true;
        public string smtpHost { get; set; } = string.Empty;
        public int smtpPort { get; set; } = 587;
        public bool useSsl { get; set; } = false;
        public bool useStartTls { get; set; } = true;
        public string username { get; set; } = string.Empty;
        /// <summary>儲存時為空字串表示「不變更密碼」；讀取時 API 永遠回傳空字串（不外洩）</summary>
        public string password { get; set; } = string.Empty;
        public string fromAddress { get; set; } = string.Empty;
        public string fromDisplayName { get; set; } = "SCADA Engine";
        public int ratePerMinute { get; set; } = 10;
        public int testSendThrottleSeconds { get; set; } = 10;

        /// <summary>讀取時表示「目前是否已設定密碼」（給 UI 顯示用）</summary>
        public bool hasPassword { get; set; }
    }

    /// <summary>Email 群組 CRUD DTO</summary>
    public class EmailGroupSaveDto
    {
        public int? id { get; set; }
        public string name { get; set; } = string.Empty;
        public string label { get; set; } = string.Empty;
        public byte maxSeverity { get; set; } = 3;
        public string language { get; set; } = "zh-TW";
        public bool isEnabled { get; set; } = true;
        public string? remarks { get; set; }
    }

    public class EmailGroupToggleDto
    {
        public bool isEnabled { get; set; }
    }

    /// <summary>Email 收件人 CRUD DTO</summary>
    public class EmailRecipientSaveDto
    {
        public int? id { get; set; }
        public int groupId { get; set; }
        public string emailAddress { get; set; } = string.Empty;
        public string? displayName { get; set; }
        public bool isEnabled { get; set; } = true;
    }

    /// <summary>群組-規則對應儲存 DTO（一次覆寫某群組的所有對應）</summary>
    public class EmailGroupRuleMappingDto
    {
        public int groupId { get; set; }
        /// <summary>該群組要接收的 AlarmRules.Id 清單（空陣列視為「全收」）</summary>
        public List<int> alarmRuleIds { get; set; } = new();
    }
}
