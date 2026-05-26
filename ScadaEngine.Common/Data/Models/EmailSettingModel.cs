namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// Email 通知設定檔 — 對應 ScadaEngine.Engine/Setting/EmailSetting.json
    /// SMTP 連線資訊與寄件者設定，密碼明文（檔案應加入 .gitignore）
    /// </summary>
    public class EmailSettingModel
    {
        /// <summary>總開關 — false 時所有 Email 推播停用，警報流程仍正常運作</summary>
        public bool EnableNotification { get; set; } = true;

        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;

        /// <summary>是否使用 SSL（隱式 TLS，通常 port 465 用）</summary>
        public bool UseSsl { get; set; } = false;

        /// <summary>是否使用 STARTTLS（顯式 TLS，通常 port 587 用）</summary>
        public bool UseStartTls { get; set; } = true;

        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        /// <summary>寄件者 Email</summary>
        public string FromAddress { get; set; } = string.Empty;

        /// <summary>寄件者顯示名稱</summary>
        public string FromDisplayName { get; set; } = "SCADA Engine";

        /// <summary>每群組每分鐘推播上限（不含 Critical），超過則合併摘要</summary>
        public int RatePerMinute { get; set; } = 10;

        /// <summary>「測試寄送」按鈕同收件人節流秒數</summary>
        public int TestSendThrottleSeconds { get; set; } = 10;
    }
}
