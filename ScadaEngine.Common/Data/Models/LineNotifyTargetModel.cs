namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// Line 通知收件群組 — 對應 LineNotifyTargets 資料表
    /// </summary>
    public class LineNotifyTargetModel
    {
        public int nId { get; set; }
        public string szGroupId { get; set; } = string.Empty;
        public string szLabel { get; set; } = string.Empty;

        /// <summary>接收嚴重度上限：0=只收 Critical, 1=Critical+High, 2=Critical+High+Medium, 3=全收</summary>
        public byte nMaxSeverity { get; set; } = 3;

        public bool isEnabled { get; set; } = true;
        public DateTime dtCreatedAt { get; set; }
        public DateTime? dtUpdatedAt { get; set; }
    }
}
