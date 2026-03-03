namespace ScadaEngine.Common;

/// <summary>
/// 共用類別 Class1，提供基礎功能示範。
/// 此類別為 Common 專案的範例實作。
/// </summary>
public class Class1
{
    /// <summary>
    /// 預設建構函式。
    /// </summary>
    public Class1()
    {
        // 基礎初始化邏輯
    }

    /// <summary>
    /// 示範方法，回傳歡迎訊息。
    /// </summary>
    /// <param name="szName">使用者名稱</param>
    /// <returns>歡迎訊息</returns>
    public string GetWelcomeMessage(string szName = "SCADA System")
    {
        return $"歡迎使用 {szName} - ScadaEngine.Common 專案";
    }

    /// <summary>
    /// 示範方法，取得當前時間戳記。
    /// </summary>
    /// <returns>格式化時間字串</returns>
    public string GetCurrentTimestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}