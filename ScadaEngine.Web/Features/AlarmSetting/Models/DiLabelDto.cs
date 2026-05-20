namespace ScadaEngine.Web.Features.AlarmSetting.Models
{
    /// <summary>
    /// 由 Designer 已發布頁面解析出的 DI 點位 ON/OFF 標籤對應，
    /// 提供 AlarmSetting Modal 中 DI 警報區塊的唯讀同步顯示。
    /// </summary>
    public class DiLabelDto
    {
        public string onLabel { get; set; } = "ON";
        public string offLabel { get; set; } = "OFF";
    }
}
