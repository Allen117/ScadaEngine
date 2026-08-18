namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// 簡訊通知收訊號碼 — 對應 SmsNotifyTargets 資料表
    /// </summary>
    public class SmsNotifyTargetModel
    {
        public int nId { get; set; }

        /// <summary>手機號碼，國際格式（+886912345678）或本地格式（0912345678）</summary>
        public string szPhoneNumber { get; set; } = string.Empty;

        public string szLabel { get; set; } = string.Empty;

        /// <summary>接收嚴重度上限：0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收</summary>
        public byte nMaxSeverity { get; set; } = 3;

        /// <summary>簡訊語系：'zh-TW' 或 'en'（每號碼獨立）</summary>
        public string szLanguage { get; set; } = "zh-TW";

        public bool isEnabled { get; set; } = true;
        public DateTime dtCreatedAt { get; set; }
        public DateTime? dtUpdatedAt { get; set; }
    }
}
