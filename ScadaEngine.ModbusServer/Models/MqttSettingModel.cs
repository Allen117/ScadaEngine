namespace ScadaEngine.ModbusServer.Models;

/// <summary>對應 MqttSetting/MqttSetting.json（與 Engine / Web 同格式，屬性名即 JSON key）</summary>
public class MqttSettingRootModel
{
    public MqttConfigModel MqttConfig { get; set; } = new();
}

public class MqttConfigModel
{
    public string szBrokerIp { get; set; } = "127.0.0.1";
    public int nPort { get; set; } = 1883;
    public string szClientId { get; set; } = "SCADA_Modbus_Gateway";
    public string szBaseTopic { get; set; } = "SCADA/Realtime";
    public bool isRetain { get; set; } = true;
}
