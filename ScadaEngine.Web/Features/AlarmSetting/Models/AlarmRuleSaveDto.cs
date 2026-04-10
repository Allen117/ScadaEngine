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
    }
}
