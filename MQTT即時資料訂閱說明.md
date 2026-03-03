# SCADA 即時資料 MQTT 訂閱說明

本文件說明如何透過 MQTT Broker 取得 SCADA 引擎發布的即時點位資料，適用於任何想要訂閱資料的應用程式或開發人員。

---

## 1. Broker 連線資訊

| 項目 | 值 | 備註 |
|------|-----|------|
| Broker IP | `127.0.0.1` | 可於 `MqttSetting/MqttSetting.json` 修改 |
| Port | `1883` | 標準 MQTT 未加密埠 |
| 認證 | 無 | 目前無帳號密碼 |
| 協定 | MQTT v5 / v3.1.1 | 皆可連線 |

> 設定檔路徑：`ScadaEngine.Engine/MqttSetting/MqttSetting.json`

---

## 2. Topic 結構

```
SCADA/Realtime/{coordinatorName}/{SID}
```

- **`SCADA/Realtime`** — 固定前綴，對應設定檔 `szBaseTopic`
- **`{coordinatorName}`** — Coordinator 名稱，對應 Modbus JSON 設定檔名稱（即 `ModbusCoordinator.Name`），例如 `CH_Chiller`
- **`{SID}`** — 點位唯一識別碼，格式為 `{設備編號}-S{序號}`，例如 `196865-S1`

### 訂閱所有點位（Wildcard）

```
SCADA/Realtime/+/+
```

使用兩層 `+` 萬用字元一次訂閱所有 Coordinator 下的所有點位。

### 訂閱特定 Coordinator 下所有點位

```
SCADA/Realtime/CH_Chiller/+
```

### 訂閱單一點位

```
SCADA/Realtime/CH_Chiller/196865-S1
```

---

## 3. Payload 格式（JSON）

每個 MQTT 訊息的 Payload 為 **UTF-8 編碼的 JSON 字串**，欄位均為小寫 camelCase：

```json
{
  "sid":             "196865-S1",
  "coordinatorName": "CH_Chiller",
  "name":            "CH1MOA",
  "value":           6.0,
  "unit":            "",
  "quality":         "Good",
  "timestamp":       1740571681000,
  "address":         0
}
```

### 欄位說明

| 欄位 | 型別 | 說明 | 範例 |
|------|------|------|------|
| `sid` | string | 點位唯一識別碼 (SID) | `"196865-S1"` |
| `coordinatorName` | string | Coordinator 名稱（對應 JSON 設定檔名） | `"CH_Chiller"` |
| `name` | string | 點位標籤名稱 | `"CH1MOA"` |
| `value` | number (double) | 工程值（已套用 Ratio 換算） | `6.0` |
| `unit` | string | 工程單位，無單位則為空字串 | `"℃"`, `"%"`, `""` |
| `quality` | string | 資料品質，見下方說明 | `"Good"` |
| `timestamp` | number (int64) | Unix 毫秒時間戳記 (UTC) | `1740571681000` |
| `address` | number (int) | Modbus 暫存器起始位址（0-based） | `0` |

### quality 品質值

| 值 | 說明 |
|----|------|
| `"Good"` | 資料正常，可信賴 |
| `"Bad"` | 通訊異常，資料不可信 |
| `"Uncertain"` | 資料不確定（如設備初始化中） |

---

## 4. 發布特性

| 項目 | 值 |
|------|-----|
| QoS | **1（At Least Once）** |
| Retain | **true** |
| 發布週期 | 依 Modbus 輪詢週期（約 1~2 秒/次） |

**Retain = true** 的意義：每個點位的最後一筆資料會被 Broker 保留。
新的訂閱者連線後，即使 Engine 未發布新資料，也會**立即收到每個點位的最後已知值**。

---

## 5. 時間戳記轉換

`timestamp` 為 **Unix 毫秒（UTC）**，各語言轉換方式：

```python
# Python
from datetime import datetime, timezone
ts_ms = 1740571681000
dt = datetime.fromtimestamp(ts_ms / 1000, tz=timezone.utc)
```

```javascript
// JavaScript
const ts_ms = 1740571681000;
const dt = new Date(ts_ms);
console.log(dt.toLocaleString());
```

```csharp
// C#
long ts_ms = 1740571681000;
DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(ts_ms).LocalDateTime;
```

```java
// Java
long ts_ms = 1740571681000L;
Instant instant = Instant.ofEpochMilli(ts_ms);
```

---

## 6. 訂閱範例

### Python（paho-mqtt）

```python
import paho.mqtt.client as mqtt
import json

BROKER = "127.0.0.1"
PORT   = 1883
TOPIC  = "SCADA/Realtime/+/+"

def on_connect(client, userdata, flags, rc, properties=None):
    print(f"Connected: {rc}")
    client.subscribe(TOPIC)

def on_message(client, userdata, msg):
    payload = json.loads(msg.payload.decode("utf-8"))
    sid       = payload.get("sid", "")
    name      = payload.get("name", "")
    value     = payload.get("value", 0)
    unit      = payload.get("unit", "")
    quality   = payload.get("quality", "")
    timestamp = payload.get("timestamp", 0)

    print(f"[{sid}] {name} = {value} {unit}  品質:{quality}  時間戳:{timestamp}")

client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2)
client.on_connect = on_connect
client.on_message = on_message
client.connect(BROKER, PORT, keepalive=60)
client.loop_forever()
```

### Node.js（mqtt.js）

```javascript
const mqtt = require("mqtt");

const client = mqtt.connect("mqtt://127.0.0.1:1883");

client.on("connect", () => {
  client.subscribe("SCADA/Realtime/+/+");
  console.log("已訂閱即時資料");
});

client.on("message", (topic, message) => {
  const data = JSON.parse(message.toString());
  console.log(`[${data.sid}] ${data.name} = ${data.value} ${data.unit}  品質:${data.quality}`);
});
```

### C#（MQTTnet）

```csharp
using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;

var factory = new MqttFactory();
var client  = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("127.0.0.1", 1883)
    .WithClientId("MyApp_Reader")
    .Build();

client.ApplicationMessageReceivedAsync += e =>
{
    var topic   = e.ApplicationMessage.Topic;
    var payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
    var data    = JsonDocument.Parse(payload).RootElement;

    var sid     = data.GetProperty("sid").GetString();
    var name    = data.GetProperty("name").GetString();
    var value   = data.GetProperty("value").GetDouble();
    var quality = data.GetProperty("quality").GetString();

    Console.WriteLine($"[{sid}] {name} = {value}  品質:{quality}");
    return Task.CompletedTask;
};

await client.ConnectAsync(options);
await client.SubscribeAsync("SCADA/Realtime/+/+");

Console.ReadLine();
await client.DisconnectAsync();
```

---

## 7. 點位清單查詢

所有已設定的點位資訊儲存於資料庫 `ModbusPoints` 資料表：

```sql
SELECT SID, Name, Address, DataType, Ratio, Unit
FROM ModbusPoints
ORDER BY SID;
```

| 欄位 | 說明 |
|------|------|
| `SID` | 對應 MQTT topic 的 `{SID}` 段，以及 Payload 的 `sid` 欄位 |
| `Name` | 點位標籤名稱，對應 Payload 的 `name` |
| `Address` | Modbus 位址（5 位格式，如 `40001`） |
| `DataType` | 資料型別（Integer / FloatingPt / SwappedFP 等） |
| `Ratio` | 換算倍率，`工程值 = 原始值 × Ratio` |
| `Unit` | 工程單位 |

---

## 8. 完整資料流程

```
Modbus 設備
    │  TCP 輪詢（FC01/02/03/04）
    ▼
ScadaEngine.Engine
    │  原始值 × Ratio → 工程值
    │  品質判斷（Good / Bad）
    ▼
MqttPublishService
    │  JSON 序列化（ToMqttPayload）
    │  Topic: SCADA/Realtime/{coordinatorName}/{SID}
    │  QoS=1, Retain=true
    ▼
MQTT Broker（127.0.0.1:1883）
    │  Retain 儲存最後一筆
    ▼
訂閱者（任何 MQTT Client）
    │  Subscribe: SCADA/Realtime/+/+
    ▼
應用程式（Web / Python / Node.js / ...）
```

---

## 9. 常見問題

**Q：訂閱後沒有收到資料？**
A：確認 SCADA Engine 服務是否正在運行，以及 Broker 是否可連線（`telnet 127.0.0.1 1883`）。

**Q：收到的 `name` 欄位顯示 SID 而非標籤名稱？**
A：Engine 可能使用舊版 Payload 格式（不含 `name` 欄位）。請重啟 Engine 以發布含 `name` 欄位的新格式訊息。

**Q：如何只訂閱特定設備的點位？**
A：使用 `SCADA/Realtime/{coordinatorName}/+` 訂閱特定 Coordinator 下的所有點位。Coordinator 名稱對應 `ScadaEngine.Engine/Modbus/` 目錄下的 JSON 設定檔名稱（不含副檔名），即 `ModbusCoordinator.Name`。

**Q：`value` 欄位的精度？**
A：Payload 使用 `double`（64-bit 浮點數），Engine 內部為 `float`（32-bit），轉換時可能有微小誤差。建議使用方四捨五入至合理小數位。
