namespace ScadaEngine.Web.Features.AlarmSetting.Models
{
    /// <summary>
    /// Line 通知收件群組 CRUD DTO — 對應 LineNotifyTargets 資料表
    /// </summary>
    public class LineTargetSaveDto
    {
        /// <summary>編輯時帶入；新增時為 null</summary>
        public int? id { get; set; }

        /// <summary>Line GroupID（C 開頭 33 字元）</summary>
        public string groupId { get; set; } = string.Empty;

        /// <summary>顯示名稱，例如「主管群」</summary>
        public string label { get; set; } = string.Empty;

        /// <summary>接收嚴重度上限：0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收</summary>
        public byte maxSeverity { get; set; } = 3;

        public bool isEnabled { get; set; } = true;
    }

    /// <summary>啟用切換 DTO</summary>
    public class LineTargetToggleDto
    {
        public bool isEnabled { get; set; }
    }
}
