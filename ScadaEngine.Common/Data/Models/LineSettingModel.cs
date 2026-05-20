namespace ScadaEngine.Common.Data.Models
{
    /// <summary>
    /// Line 通知設定檔 — 對應 ScadaEngine.Engine/Setting/LineSetting.json
    /// 機密性質：ChannelAccessToken 不得進入版控（檔案已加入 .gitignore）
    /// </summary>
    public class LineSettingModel
    {
        /// <summary>Line Messaging API Channel Access Token（長期 token）</summary>
        public string ChannelAccessToken { get; set; } = string.Empty;

        /// <summary>總開關 — false 時所有 Line 推播停用，警報流程仍正常運作</summary>
        public bool EnableNotification { get; set; } = true;

        /// <summary>每群組每分鐘推播上限（不含 Critical），超過則合併摘要</summary>
        public int RatePerMinute { get; set; } = 10;

        /// <summary>「測試發送」按鈕同群組節流秒數</summary>
        public int TestSendThrottleSeconds { get; set; } = 10;
    }
}
