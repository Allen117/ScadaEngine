using System.Collections.Concurrent;

namespace ScadaEngine.Web.Services
{
    /// <summary>
    /// 簡訊測試發送服務 — 給「測試發送」按鈕使用
    /// 與 Line/Email 不同：序列埠在 Engine 手上，Web 只負責 throttle + 發 MQTT 命令，
    /// 實際發送與結果由 Engine 執行（結果寫 EventLog + SCADA/Sys/Sms/Status 的 lastTest 欄位）
    /// - 同號碼 throttle 秒數讀自 SmsSetting.json（預設 10 秒），避免使用者狂按燒簡訊費
    /// </summary>
    public class SmsTestSendService
    {
        private readonly ILogger<SmsTestSendService> _logger;
        private readonly SmsCommandPublisher _commandPublisher;
        private readonly SmsSenderConfigService _configService;

        private readonly ConcurrentDictionary<string, DateTime> _lastTestAt = new();

        public SmsTestSendService(
            ILogger<SmsTestSendService> logger,
            SmsCommandPublisher commandPublisher,
            SmsSenderConfigService configService)
        {
            _logger = logger;
            _commandPublisher = commandPublisher;
            _configService = configService;
        }

        public async Task<TestSendResult> SendTestAsync(string szPhoneNumber, string szLabel, string szLanguage = "zh-TW")
        {
            var setting = _configService.LoadFromFile();

            if (!setting.EnableNotification)
                return TestSendResult.Failure("簡訊通知未啟用，請先在簡訊盒設定開啟");

            int nThrottle = setting.TestSendThrottleSeconds > 0 ? setting.TestSendThrottleSeconds : 10;
            if (_lastTestAt.TryGetValue(szPhoneNumber, out var dtLast))
            {
                var dtElapsed = DateTime.UtcNow - dtLast;
                if (dtElapsed.TotalSeconds < nThrottle)
                {
                    int nRemain = (int)Math.Ceiling(nThrottle - dtElapsed.TotalSeconds);
                    return TestSendResult.Throttled(nRemain);
                }
            }

            // 先記錄送出時間（即使後續發布失敗也要記，避免重試風暴）
            _lastTestAt[szPhoneNumber] = DateTime.UtcNow;

            var isPublished = await _commandPublisher.PublishTestAsync(szPhoneNumber, szLabel, szLanguage);
            if (!isPublished)
                return TestSendResult.Failure("命令送出失敗（MQTT 未連線），請確認 broker 狀態");

            _logger.LogInformation("簡訊測試命令已送出: Phone={Phone}", szPhoneNumber);
            return new TestSendResult
            {
                isSuccess = true,
                szMessage = "測試命令已送出，發送結果請看簡訊盒狀態與事件記錄（簡訊需 5~10 秒送達）"
            };
        }
    }
}
