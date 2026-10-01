namespace ScadaEngine.Web.Features.AlarmSetting.Models
{
    /// <summary>
    /// 前端新增/更新警報規則時提交的 DTO
    /// </summary>
    public class AlarmRuleSaveDto
    {
        public int? id { get; set; }
        public string sid { get; set; } = string.Empty;
        public bool isEnabled { get; set; } = true;

        public bool isAlarmHigh { get; set; }
        public double? alarmHighValue { get; set; }
        public double? deadbandHigh { get; set; }
        public int alarmHighSeverity { get; set; } = 1;

        public bool isAlarmLow { get; set; }
        public double? alarmLowValue { get; set; }
        public double? deadbandLow { get; set; }
        public int alarmLowSeverity { get; set; } = 1;

        public bool isDiAlarm { get; set; }
        public string? diTriggerState { get; set; }
        public int diAlarmSeverity { get; set; } = 1;
        public string? diOnLabel { get; set; }
        public string? diOffLabel { get; set; }

        public string? remarks { get; set; }

        // ── 前置條件（連鎖遮蔽）──
        public bool isPrecondition { get; set; }
        public string? preSid { get; set; }
        /// <summary>GE / GT / LE / LT / EQ / NE / ON / OFF</summary>
        public string? preOperator { get; set; }
        public double? preValue { get; set; }
        public int preDelaySec { get; set; }

        // ── 恢復通知（各通道；一般恢復與前置條件清除皆適用）──
        public bool recoveryNotifyLine { get; set; } = true;
        public bool recoveryNotifyEmail { get; set; } = true;
        public bool recoveryNotifySms { get; set; } = true;

        /// <summary>本點警報 on-delay 秒數，0 = 立即</summary>
        public int alarmDelaySec { get; set; }
    }
}
