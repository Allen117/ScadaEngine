namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// 警報規則 DB Model — 對應 AlarmRules 資料表
    /// </summary>
    public class AlarmRuleModel
    {
        public int nId { get; set; }
        public string szSID { get; set; } = string.Empty;
        public bool isEnabled { get; set; } = true;

        // ── 上限警報 ──
        public bool isAlarmHigh { get; set; }
        public double? dAlarmHighValue { get; set; }
        public double? dDeadbandHigh { get; set; }
        public byte nAlarmHighSeverity { get; set; } = 1;

        // ── 下限警報 ──
        public bool isAlarmLow { get; set; }
        public double? dAlarmLowValue { get; set; }
        public double? dDeadbandLow { get; set; }
        public byte nAlarmLowSeverity { get; set; } = 1;

        // ── DI 警報 ──
        public bool isDiAlarm { get; set; }
        public string? szDiTriggerState { get; set; }
        public byte nDiAlarmSeverity { get; set; } = 1;
        public string? szDiOnLabel { get; set; }
        public string? szDiOffLabel { get; set; }

        public string? szRemarks { get; set; }

        // ── 前置條件（連鎖遮蔽）──
        public bool isPrecondition { get; set; }
        public string? szPreSID { get; set; }
        /// <summary>GE / GT / LE / LT / EQ / NE / ON / OFF</summary>
        public string? szPreOperator { get; set; }
        public double? dPreValue { get; set; }
        public int nPreDelaySec { get; set; }

        // ── 恢復通知（各通道是否發恢復通知；一般恢復與前置條件清除皆適用）──
        public bool isRecoveryNotifyLine { get; set; } = true;
        public bool isRecoveryNotifyEmail { get; set; } = true;
        /// <summary>另受 SmsSetting.SendRecovery 全域開關約束</summary>
        public bool isRecoveryNotifySms { get; set; } = true;

        /// <summary>本點警報 on-delay 秒數（高/低/DI 各自計時），0 = 立即</summary>
        public int nAlarmDelaySec { get; set; }

        /// <summary>點位名稱（JOIN 查詢時帶出，非 AlarmRules 表欄位）</summary>
        public string? szPointName { get; set; }
    }
}
