namespace ScadaEngine.Engine.Models;

/// <summary>
/// MQTT 連線配置模型
/// </summary>
public class MqttConfigModel
{
    /// <summary>
    /// MQTT Broker 的 IP 地址
    /// </summary>
    public string szBrokerIp { get; set; } = "127.0.0.1";

    /// <summary>
    /// MQTT 通訊埠
    /// </summary>
    public int nPort { get; set; } = 1883;

    /// <summary>
    /// 客戶端識別碼
    /// </summary>
    public string szClientId { get; set; } = "SCADA_Main_Engine";

    /// <summary>
    /// 發布主題的前綴
    /// </summary>
    public string szBaseTopic { get; set; } = "SCADA/Realtime";

    /// <summary>
    /// 是否保留最後一筆訊息
    /// </summary>
    public bool isRetain { get; set; } = true;
}

/// <summary>
/// MQTT 設定檔案完整配置模型
/// </summary>
public class MqttSettingModel
{
    /// <summary>
    /// MQTT 連線配置
    /// </summary>
    public MqttConfigModel MqttConfig { get; set; } = new MqttConfigModel();

    /// <summary>
    /// 資料庫連線字串配置
    /// </summary>
    public ConnectionStringModel ConnectionStrings { get; set; } = new ConnectionStringModel();
}

/// <summary>
/// 連線字串配置模型
/// </summary>
public class ConnectionStringModel
{
    /// <summary>
    /// 預設資料庫連線字串
    /// </summary>
    public string szDefaultConnection { get; set; } = string.Empty;
}