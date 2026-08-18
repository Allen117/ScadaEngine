namespace ScadaEngine.Web.Features.AlarmSetting.Models
{
    /// <summary>
    /// 簡訊通知收訊號碼 CRUD DTO — 對應 SmsNotifyTargets 資料表
    /// </summary>
    public class SmsTargetSaveDto
    {
        /// <summary>編輯時帶入；新增時為 null</summary>
        public int? id { get; set; }

        /// <summary>手機號碼，國際格式（+886912345678）或本地格式（0912345678）</summary>
        public string phoneNumber { get; set; } = string.Empty;

        /// <summary>顯示名稱，例如「王主任」</summary>
        public string label { get; set; } = string.Empty;

        /// <summary>接收嚴重度上限：0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收</summary>
        public byte maxSeverity { get; set; } = 3;

        /// <summary>簡訊語系：'zh-TW' 或 'en'</summary>
        public string language { get; set; } = "zh-TW";

        public bool isEnabled { get; set; } = true;
    }

    /// <summary>啟用切換 DTO</summary>
    public class SmsTargetToggleDto
    {
        public bool isEnabled { get; set; }
    }

    /// <summary>簡訊盒設定 DTO — 對應 SmsSetting.json（無機密欄位）</summary>
    public class SmsSenderConfigDto
    {
        public bool enableNotification { get; set; }

        /// <summary>"auto" = 自動掃描；或手動指定如 "COM3"</summary>
        public string comPort { get; set; } = "auto";

        /// <summary>0 = 自動嘗試（115200 → 9600）</summary>
        public int baudRate { get; set; }

        /// <summary>SIM PIN 碼；空字串 = 未設 PIN</summary>
        public string simPin { get; set; } = string.Empty;

        public int ratePerMinute { get; set; } = 10;

        /// <summary>每日發送總上限；0 = 不限制</summary>
        public int dailyQuota { get; set; } = 100;

        /// <summary>警報恢復時是否也發簡訊</summary>
        public bool sendRecovery { get; set; } = true;

        public int testSendThrottleSeconds { get; set; } = 10;
    }
}
