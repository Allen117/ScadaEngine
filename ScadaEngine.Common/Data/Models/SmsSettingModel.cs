namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// 簡訊通知設定檔 — 對應 ScadaEngine.Engine/Setting/SmsSetting.json
    /// 簡訊盒 = 序列埠 GSM/4G Modem，走 3GPP TS 27.005 標準 AT 指令（不綁廠牌）
    /// </summary>
    public class SmsSettingModel
    {
        /// <summary>總開關 — false 時所有簡訊停用，警報流程仍正常運作</summary>
        public bool EnableNotification { get; set; } = true;

        /// <summary>COM port："auto" = 自動掃描偵測；或手動指定如 "COM3"</summary>
        public string ComPort { get; set; } = "auto";

        /// <summary>Baud rate；0 = 自動嘗試（115200 → 9600）</summary>
        public int BaudRate { get; set; } = 0;

        /// <summary>SIM PIN 碼；空字串 = SIM 未設 PIN（建議現場關 PIN）</summary>
        public string SimPin { get; set; } = string.Empty;

        /// <summary>每分鐘發送上限（不含 Critical），超過則合併摘要</summary>
        public int RatePerMinute { get; set; } = 10;

        /// <summary>每日發送總上限（防警報風暴燒簡訊費）；0 = 不限制</summary>
        public int DailyQuota { get; set; } = 100;

        /// <summary>警報恢復時是否也發簡訊（簡訊按封計費，關閉可省一半費用）</summary>
        public bool SendRecovery { get; set; } = true;

        /// <summary>「測試發送」按鈕同號碼節流秒數</summary>
        public int TestSendThrottleSeconds { get; set; } = 10;
    }
}
