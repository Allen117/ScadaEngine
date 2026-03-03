namespace ScadaEngine.Web.Features.MainPage.Models;

/// <summary>
/// 主頁面視圖模型 - 採用肥 Model 設計，包含系統狀態邏輯
/// </summary>
public class MainPageViewModel
{
    /// <summary>
    /// 使用者名稱 (匈牙利命名法: sz = string)
    /// </summary>
    public string szUserName { get; set; } = string.Empty;

    /// <summary>
    /// 登入時間 (匈牙利命名法: dt = DateTime)
    /// </summary>
    public DateTime dtLoginTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 系統狀態資訊
    /// </summary>
    public SystemStatusModel systemStatus { get; set; } = new SystemStatusModel();

    /// <summary>
    /// 取得格式化的登入時間
    /// </summary>
    /// <returns>格式化的時間字串</returns>
    public string GetFormattedLoginTime()
    {
        return dtLoginTime.ToString("yyyy/MM/dd HH:mm:ss");
    }

    /// <summary>
    /// 檢查使用者是否為管理員
    /// </summary>
    /// <returns>是管理員回傳 true</returns>
    public bool IsAdministrator()
    {
        return szUserName.Equals("admin", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 系統狀態模型
/// </summary>
public class SystemStatusModel
{
    /// <summary>
    /// 系統是否在線 (匈牙利命名法: is = bool)
    /// </summary>
    public bool isSystemOnline { get; set; } = true;

    /// <summary>
    /// 資料庫連線狀態 (匈牙利命名法: is = bool)
    /// </summary>
    public bool isDatabaseConnected { get; set; } = true;

    /// <summary>
    /// Modbus 通訊狀態 (匈牙利命名法: is = bool)
    /// </summary>
    public bool isModbusReady { get; set; } = false;

    /// <summary>
    /// MQTT 發布狀態 (匈牙利命名法: is = bool)
    /// </summary>
    public bool isMqttReady { get; set; } = false;

    /// <summary>
    /// 連線設備數量 (匈牙利命名法: n = int)
    /// </summary>
    public int nConnectedDevices { get; set; } = 0;

    /// <summary>
    /// 取得系統整體健康狀態
    /// </summary>
    /// <returns>健康狀態文字</returns>
    public string GetOverallHealthStatus()
    {
        if (isSystemOnline && isDatabaseConnected && isModbusReady && isMqttReady)
            return "優良";
        
        if (isSystemOnline && isDatabaseConnected)
            return "良好";
        
        if (isSystemOnline)
            return "普通";
        
        return "異常";
    }

    /// <summary>
    /// 取得狀態顏色類別
    /// </summary>
    /// <returns>Bootstrap 顏色類別</returns>
    public string GetHealthStatusClass()
    {
        return GetOverallHealthStatus() switch
        {
            "優良" => "success",
            "良好" => "info",
            "普通" => "warning",
            _ => "danger"
        };
    }
}