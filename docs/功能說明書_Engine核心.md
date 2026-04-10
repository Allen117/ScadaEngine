# ScadaEngine.Engine 核心功能說明書

> 文件版本：1.0　｜　產出日期：2026-03-31　｜　對應程式碼：ScadaEngine.Engine 專案

---

## 1. 系統概述

ScadaEngine.Engine 是一個 .NET 8 Worker Service，作為 SCADA 工業監控系統的資料採集核心。它以背景服務形式運行（支援 Windows Service 或 Console 模式），負責：

1. **Modbus TCP 資料採集** — 輪詢工業設備，讀取感測器數值
2. **MQTT 即時發布** — 將採集到的數據即時推送給訂閱端（Web 儀表板）
3. **資料庫持久化** — 將歷史資料與最新值寫入 SQL Server
4. **警報監控** — 比對警報規則，偵測異常並記錄事件
5. **計算點位** — 根據使用者自訂公式衍生虛擬感測器值
6. **條件控制** — 依據感測器數值自動執行 Modbus 寫入
7. **流程圖控制** — 執行視覺化邏輯流程圖（AND/OR/定時器/排程）

---

## 2. 系統架構

### 2.1 整體資料流

```
┌──────────────────┐
│ Modbus TCP 設備   │  ← 工業現場感測器/控制器（多台）
└────────┬─────────┘
         │ FC01/02/03/04 輪詢
         ▼
┌──────────────────────────────────────────────────┐
│  ModbusCollectionManager（多執行緒管理器）          │
│  └─ ModbusCommunicationService × N（每設備一條）   │
│     └─ FluentModbus TCP 讀取 → 原始值 × Ratio     │
│        → 產出 RealtimeDataModel                   │
└────────┬─────────────────────────────────────────┘
         │ DataCollected 事件
         ▼
┌──────────────────────────────────────────────────┐
│  Worker（主服務，事件驅動）                         │
│  OnModbusDataCollected() 依序處理：               │
│  ① HistoryDataStorageService  → 歷史資料暫存      │
│  ② RealtimeDataStorageService → 最新值暫存        │
│  ③ AlarmMonitorService        → 警報規則比對      │
│  ④ CalculatedPointService     → 公式計算 + MQTT   │
│  ⑤ AlarmMonitorService        → 計算結果也做警報   │
└────────┬─────────────────────────────────────────┘
         │
    ┌────┼────────────────────┐
    ▼    ▼                    ▼
┌───────┐ ┌──────────┐  ┌──────────────┐
│ MQTT  │ │ SQL Server│  │ EventLog 表  │
│ Broker│ │HistoryData│  │ （警報記錄）  │
│       │ │LatestData │  │              │
└───┬───┘ └──────────┘  └──────────────┘
    │ subscribe
    ▼
┌──────────────────┐
│ ScadaEngine.Web  │  ← 儀表板即時顯示
└──────────────────┘
```

### 2.2 並行背景服務

| 服務 | 類型 | 執行頻率 | 說明 |
|------|------|---------|------|
| Worker | BackgroundService | 事件驅動 | 主服務，協調所有子服務 |
| ConditionControlService | BackgroundService | 每 5 秒 | 條件規則 → Modbus 寫入 |
| LogicFlowExecutionService | BackgroundService | 每 200ms | 流程圖邏輯 → Modbus 寫入 |
| MqttControlSubscribeService | BackgroundService | 事件驅動 | 接收 MQTT 控制指令 |
| HistoryDataStorageService | Timer | 每 60 秒 | 歷史資料批次寫入 DB |
| RealtimeDataStorageService | Timer | 每 5 秒 | 最新值批次 UPSERT DB |

---

## 3. 啟動流程

### 3.1 Program.cs 啟動順序

```
1. 偵測運行模式（Windows Service 或 Console）
2. 設定 Serilog 日誌
   ├─ Service 模式：檔案日誌（Log/ScadaEngine-*.log，保留 30 天）
   └─ Console 模式：從 appsettings.json 讀取 + Console 輸出
3. 註冊 DI 服務
   ├─ AddHostedService<Worker>
   ├─ AddModbusServices()        → ModbusConfigService, ModbusCollectionManager
   ├─ AddMqttServices()          → MqttConfigService, MqttPublishService, MqttControlSubscribeService
   ├─ AddDataServices()          → DB 配置, Repository, 儲存服務, 警報, 計算點位
   ├─ AddHostedService<ConditionControlService>
   └─ AddHostedService<LogicFlowExecutionService>
4. 初始化資料庫（自動建表）
5. 初始化 MQTT 連線
6. 啟動所有背景服務
```

### 3.2 Worker 啟動流程

```
ExecuteAsync()
  ├─ 訂閱 ModbusCollectionManager.DataCollected 事件
  ├─ 訂閱 ModbusCollectionManager.DeviceStatusChanged 事件
  ├─ 啟動 ModbusCollectionManager.StartAsync()
  └─ 進入主迴圈（每 60 秒記錄系統狀態）
```

---

## 4. Modbus TCP 資料採集

### 4.1 配置檔結構

**位置**：`ScadaEngine.Engine/Modbus/*.json`

每個 JSON 檔代表一台 Modbus TCP 設備。引擎啟動時自動掃描此目錄下所有 `*.json` 檔案。

```json
{
  "IP": "192.168.1.1",
  "Port": 502,
  "ModbusId": "1,2,3",
  "ConnectTimeout": 500,
  "Tags": [
    {
      "Name": "冰水出水溫度",
      "Address": "30513",
      "DataType": "SWAPPEDFP",
      "Ratio": "1",
      "Unit": "°C",
      "Min": "0",
      "Max": "100"
    }
  ]
}
```

**欄位說明**：

| 欄位 | 型別 | 必填 | 說明 |
|------|------|------|------|
| IP | string | Y | 設備 IP 位址 |
| Port | int | Y | TCP 通訊埠（預設 502） |
| ModbusId | string | Y | Modbus 站號，多個以逗號分隔（如 `"1,2,3"`） |
| ConnectTimeout | int | N | 連線逾時（毫秒，預設 1000） |
| Tags | array | Y | 點位定義陣列 |
| Tags[].Name | string | Y | 點位名稱 |
| Tags[].Address | string | Y | 5 位數慣例地址（見下方說明） |
| Tags[].DataType | string | Y | 資料型態（見下方說明） |
| Tags[].Ratio | string | N | 縮放比例（預設 "1"） |
| Tags[].Unit | string | N | 工程單位（如 °C、V、A） |
| Tags[].Min / Max | string | N | 控制範圍值 |

### 4.2 Modbus 地址慣例（5 位數格式）

| 地址範圍 | 功能碼 | 類型 | 存取 |
|---------|--------|------|------|
| 00001 ~ 09999 | FC01 | Coils (DO) | 讀/寫 |
| 10001 ~ 19999 | FC02 | Discrete Inputs (DI) | 唯讀 |
| 30001 ~ 39999 | FC04 | Input Registers | 唯讀 |
| 40001 ~ 49999 | FC03 | Holding Registers | 讀/寫 |

**地址解析範例**：
- `30513` → FC04, 實際地址 = 30513 - 30001 = 512
- `40001` → FC03, 實際地址 = 40001 - 40001 = 0

### 4.3 支援的資料型態

| 型態名稱 | 暫存器數量 | 位元組順序 | 說明 |
|---------|-----------|-----------|------|
| INTEGER | 1 | — | 有號 16-bit 整數（short） |
| UINTEGER | 1 | — | 無號 16-bit 整數（ushort） |
| FLOATINGPT | 2 | Low Word First (CDAB) | IEEE 754 單精度浮點數 |
| SWAPPEDFP | 2 | High Word First (ABCD) | IEEE 754 單精度浮點數（交換字組） |
| DOUBLE | 4 | Low Word First (GHEFCDAB) | IEEE 754 雙精度浮點數 |
| SWAPPEDDOUBLE | 4 | High Word First (ABCDEFGH) | IEEE 754 雙精度浮點數（交換字組） |
| UINT32BE | 2 | Big-Endian (ABCD) | 無號 32-bit 整數 |

**物理值計算公式**：`實際值 = 原始值 × Ratio`

### 4.4 多設備併行採集機制

```
ModbusCollectionManager
├─ ConcurrentDictionary<設備Key, ModbusCommunicationService>
├─ ConcurrentDictionary<設備Key, CancellationTokenSource>
└─ ConcurrentDictionary<設備Key, Task>

每台設備獨立一條 async Task：
  1. ConnectAsync() — 建立 TCP 連線
  2. 迴圈：
     a. 檢查連線 → 斷線時嘗試重連（ShouldReconnect 判斷間隔）
     b. ReadAllTagsAsync() — 依功能碼分組批次讀取
     c. 觸發 DataCollected 事件
     d. await Task.Delay(採集週期)
  3. DisconnectAsync() — 清理連線
```

**設備 Key 格式**：`{IP}:{Port}`（如 `192.168.1.1:502`）

**採集週期**：每台設備可獨立設定（讀取自 DB 的 `ModbusCoordinator.DelayTime`），預設 1000ms。

### 4.5 SID 唯一識別碼產生規則

```
SID = {nDatabaseId × 65536 + nModbusId × 256 + 1}-S{TagIndex}
```

- `nDatabaseId`：`ModbusCoordinator.Id`（資料庫自動遞增）
- `nModbusId`：Modbus 站號
- `TagIndex`：點位在 Tags 陣列中的順序（1-based）

**範例**：DatabaseId=3, ModbusId=1, 第 2 個 Tag → `196865-S2`

### 4.6 批次讀取最佳化

每個功能碼下的點位會依地址排序並分組為連續批次（ModbusBatchGroup），以減少 Modbus 請求次數：

```
FC04 的點位 [addr=512, addr=513, addr=514, addr=600, addr=601]
→ 分組為：
  批次1: startAddr=512, count=3（連續）
  批次2: startAddr=600, count=2（連續）
```

### 4.7 斷線重連機制

| 項目 | 說明 |
|------|------|
| 偵測方式 | `IsConnected` 旗標 + TCP 例外 |
| 重連間隔 | `ConnectTimeout × 20`（如 500ms → 10 秒） |
| 斷線時行為 | 產出 Quality="Bad" 的資料，繼續輪詢 |
| 重連判斷 | `ShouldReconnect()` 檢查距上次重連是否已過間隔 |

### 4.8 設定檔熱更新

Engine 啟動後會透過 `FileSystemWatcher` 監控 `Modbus/` 目錄：

- **修改 JSON 檔**：停止該設備的採集任務 → 重新載入 → 重啟採集
- **新增 JSON 檔**：自動載入並啟動新設備採集

---

## 5. MQTT 即時發布

### 5.1 MQTT 配置

**位置**：`ScadaEngine.Engine/MqttSetting/MqttSetting.json`

| 參數 | 預設值 | 說明 |
|------|--------|------|
| BrokerIp | 127.0.0.1 | MQTT Broker IP |
| Port | 1883 | Broker 通訊埠 |
| ClientId | SCADA_Main_Engine | 用戶端識別碼 |
| BaseTopic | SCADA/Realtime | 基礎主題前綴 |
| Retain | true | 是否保留最新訊息 |

### 5.2 發布主題格式

```
{BaseTopic}/{CoordinatorName}/{SID}
```

**範例**：`SCADA/Realtime/Modbus/196865-S1`

### 5.3 訊息 Payload（JSON）

```json
{
  "sid": "196865-S1",
  "coordinatorName": "Modbus",
  "name": "冰水出水溫度",
  "value": 7.5,
  "unit": "°C",
  "quality": "Good",
  "timestamp": 1711843200000,
  "address": 30513
}
```

### 5.4 發布策略

| 策略 | 說明 |
|------|------|
| 變更發布 | 值變動時立即發布 |
| 定期發布 | 值未變但超過 3 分鐘未發布時強制發布 |
| QoS | 1（At Least Once） |
| Retain | true（Broker 保留最新訊息） |

### 5.5 MQTT 重連機制

| 項目 | 說明 |
|------|------|
| 首次失敗 | 不自動重連（由初始化呼叫端處理） |
| 運行中斷線 | 等待 2 秒後嘗試重連 |
| 重連失敗 | 啟動 30 秒定期重試計時器 |
| Clean Session | false（恢復先前訂閱狀態） |
| Keep Alive | 30 秒 |

---

## 6. 資料庫儲存

### 6.1 資料庫自動建表

Engine 啟動時讀取 `DatabaseSchema/DatabaseSchema.json`，對每張表檢查是否存在：
- 不存在 → 自動產生 `CREATE TABLE` SQL 並執行
- 已存在 → 跳過（不會修改既有結構）

### 6.2 歷史資料儲存（HistoryDataStorageService）

| 項目 | 說明 |
|------|------|
| 目標表 | HistoryData |
| 儲存頻率 | 每 60 秒批次 INSERT |
| 時間精度 | 分鐘級（秒歸零） |
| 去重邏輯 | Cache Key = `{SID}_{yyyyMMddHHmm}`，同 SID 每分鐘只保留一筆 |
| 主鍵 | (SID, Timestamp) 複合主鍵 |
| 叢集索引 | (SID, Timestamp) |

**HistoryData 表結構**：

| 欄位 | 型別 | 說明 |
|------|------|------|
| SID | nvarchar(50) | 點位識別碼（PK） |
| Timestamp | datetime | 紀錄時間（PK） |
| Value | float | 工程值 |
| Quality | int | 品質碼（1=Good, 0=Bad） |

### 6.3 最新值儲存（RealtimeDataStorageService）

| 項目 | 說明 |
|------|------|
| 目標表 | LatestData |
| 儲存頻率 | 每 5 秒批次 UPSERT |
| 時間精度 | 秒級（去除毫秒） |
| 快取機制 | 雙層快取：`_latestDataCache`（5 秒清空）+ `_persistentCache`（永不清空） |
| 持久快取用途 | 供 CalculatedPointService、LogicFlowExecutionService 即時查詢 |

**LatestData 表結構**：

| 欄位 | 型別 | 說明 |
|------|------|------|
| SID | nvarchar(100) | 點位識別碼（PK） |
| Value | float | 最新工程值 |
| Timestamp | datetime | 更新時間 |
| Quality | int | 品質碼（1=Good, 0=Bad） |

### 6.4 關鍵資料庫表（啟動時自動建立）

| 表名 | 用途 | 主鍵 |
|------|------|------|
| ModbusCoordinator | 設備登記（IP、站號、採集週期） | Id (自動遞增) |
| ModbusPoints | 點位配置（SID、名稱、地址、型態、比例） | SID |
| HistoryData | 時序歷史資料 | (SID, Timestamp) |
| LatestData | 每個 SID 的最新值快照 | SID |
| AlarmRules | 警報規則定義 | Id (自動遞增) |
| EventLog | 警報/事件生命週期記錄 | Id (bigint 自動遞增) |
| CalculatedPoints | 計算點位公式設定 | SID |
| ConditionControlRules | 條件控制規則 | Id (自動遞增) |
| LogicFlowTree | 流程圖樹狀結構 | Id (自動遞增) |
| LogicFlowDiagram | 流程圖 JSON 內容 | TreeId |
| TimeSchedules | 排程設定 | Id (自動遞增) |
| Users | 使用者帳號 | UserID (自動遞增) |
| UserPermissions | 使用者權限 JSON | UserID |
| ScadaDesign | 畫面設計版本 | Id (自動遞增) |
| ScadaDesignPage | 畫面設計頁面 | Id (自動遞增) |
| ManualControlValue | 手動控制值暫存 | SID |

---

## 7. 警報監控（AlarmMonitorService）

### 7.1 概述

- **註冊方式**：Singleton，由 Worker.OnModbusDataCollected 事件驅動
- **規則來源**：AlarmRules 資料表（每 60 秒自動重載）
- **規則索引**：`ConcurrentDictionary<SID, AlarmRuleModel>`
- **狀態追蹤**：`ConcurrentDictionary<"{SID}:{type}", AlarmState>`

### 7.2 初始化流程

```
InitializeAsync()
  ├─ 從 AlarmRules 表載入所有啟用規則（IsEnabled=1）
  ├─ 從 EventLog 表還原活躍警報狀態（ClearedAt IS NULL）
  └─ 啟動 60 秒定期重載計時器
```

### 7.3 支援的警報類型

#### 上限警報（High Alarm）

| 條件 | 說明 |
|------|------|
| 觸發 | `value >= (AlarmHighValue - DeadbandHigh)` |
| 恢復 | `value < (AlarmHighValue - DeadbandHigh)` |
| 嚴重度 | 由 AlarmHighSeverity 定義 |

#### 下限警報（Low Alarm）

| 條件 | 說明 |
|------|------|
| 觸發 | `value <= (AlarmLowValue + DeadbandLow)` |
| 恢復 | `value > (AlarmLowValue + DeadbandLow)` |
| 嚴重度 | 由 AlarmLowSeverity 定義 |

#### 數位輸入警報（DI Alarm）

| 條件 | 說明 |
|------|------|
| 觸發 | DiTriggerState="ON" 且 value≈1，或 DiTriggerState="OFF" 且 value≈0 |
| 恢復 | 狀態恢復為非觸發值 |
| 標籤 | DiOnLabel（如「運轉」）、DiOffLabel（如「停止」） |

### 7.4 警報事件生命週期

```
正常狀態 ──觸發──→ 活躍警報 ──恢復──→ 已恢復
                     │
                     └──操作人員確認──→ 已確認
```

**觸發時**：寫入 EventLog（OccurredAt=現在, ClearedAt=NULL）
**恢復時**：更新 EventLog（ClearedAt=現在）
**確認時**：更新 EventLog（IsAcknowledged=true, AcknowledgedBy=操作人員）

### 7.5 嚴重度等級

| 等級 | 值 | 色碼 | 說明 |
|------|-----|------|------|
| 緊急 | 0 | #dc3545（紅） | 立即處理 |
| 高 | 1 | #fd7e14（橘） | 盡快處理 |
| 中 | 2 | #ffc107（黃） | 排程處理 |
| 低 | 3 | #6c757d（灰） | 記錄參考 |

### 7.6 品質門檻

只有 Quality="Good" 的資料才會進行警報評估。Bad 品質的資料直接跳過，避免因通訊異常產生假警報。

---

## 8. 計算點位（CalculatedPointService）

### 8.1 概述

- **用途**：根據使用者自訂數學公式，從多個原始點位衍生計算值
- **引擎**：NCalc 表達式計算引擎
- **SID 格式**：`CALC-S{N}`（如 `CALC-S1`）
- **規則重載**：每 60 秒從 CalculatedPoints 表重新載入

### 8.2 公式範例

| 名稱 | 公式 | 輸入對應 | 說明 |
|------|------|---------|------|
| 功率 | `V * I / 1000` | `{"V":"196865-S1", "I":"196865-S2"}` | 電壓×電流÷1000 |
| 平均溫度 | `(T1 + T2) / 2` | `{"T1":"196865-S3", "T2":"196865-S4"}` | 兩點平均 |
| COP | `Qc / P` | `{"Qc":"CALC-S1", "P":"196865-S5"}` | 冷凍能力÷耗電 |

### 8.3 執行流程

```
CalculateAndPublish(triggerDataList)
  ├─ 從 RealtimeDataStorageService.GetAllLatestValues() 取得所有最新值
  ├─ 逐一計算每個啟用的計算點位：
  │   ├─ 將 InputMappings 的變數綁定為 NCalc 參數
  │   ├─ 若來源 SID 找不到 → Quality="Bad"，變數設為 0
  │   ├─ 若結果為 NaN/Infinity → Quality="Bad"
  │   └─ 產出 RealtimeDataModel
  ├─ 送入 HistoryDataStorageService（歷史存檔）
  ├─ 送入 RealtimeDataStorageService（最新值快取）
  ├─ 非同步發布至 MQTT
  └─ 回傳計算結果（供 AlarmMonitorService 進行警報檢查）
```

### 8.4 MQTT 發布主題

```
SCADA/Realtime/{GroupName}/{SID}
```

- `GroupName` 來自 CalculatedPoints.GroupName 欄位
- 若未設定 GroupName，預設為 `"Calculated"`

---

## 9. 條件控制（ConditionControlService）

### 9.1 概述

- **類型**：BackgroundService（獨立背景服務）
- **評估頻率**：每 5 秒
- **規則重載**：每 30 秒
- **設備配置重載**：每 10 分鐘

### 9.2 規則結構

```
IF 監控點位(ConditionPointSID) {運算子} {門檻值}
THEN 寫入 控制點位(ControlPointSID) = {控制值}
```

**支援運算子**：

| 代碼 | 運算子 |
|------|--------|
| 0 | > (大於) |
| 1 | < (小於) |
| 2 | >= (大於等於) |
| 3 | <= (小於等於) |
| 4 | == (等於) |
| 5 | != (不等於) |

### 9.3 冷卻時間

每條規則觸發後有 **30 秒冷卻期**，避免數值震盪時反覆執行 Modbus 寫入。

---

## 10. 流程圖控制（LogicFlowExecutionService）

### 10.1 概述

- **類型**：BackgroundService
- **執行頻率**：每 200ms 評估所有啟用的流程圖
- **規則重載**：每 15 秒
- **啟動保護期**：前 60 秒只評估不寫入，防止重啟時意外動作

### 10.2 節點類型

| 節點 | 功能 | 說明 |
|------|------|------|
| Input | 讀取感測值 | 從 RealtimeDataStorageService 快取取值 |
| Comparator | 比較判斷 | 與門檻值比較，輸出 0 或 1 |
| AND | 邏輯且 | 所有輸入皆為 1 時輸出 1 |
| OR | 邏輯或 | 任一輸入為 1 時輸出 1 |
| Timer (TP) | 脈衝定時器 | 可設定 ON/OFF 時間長度 |
| Schedule | 排程 | 依 TimeSchedules 表判斷是否在排程內 |
| Output | 寫入控制 | 執行 Modbus 寫入動作 |

### 10.3 樂觀鎖

LogicFlowDiagram 表使用 `Version` 欄位實作樂觀鎖，防止 Web 端與 Engine 端同時修改流程圖造成衝突。

---

## 11. 設定檔一覽

| 檔案路徑 | 用途 |
|---------|------|
| `Setting/dbSetting.json` | SQL Server 連線設定（IP、DB、帳密） |
| `MqttSetting/MqttSetting.json` | MQTT Broker 連線設定 |
| `Modbus/*.json` | Modbus 設備定義（支援多檔多設備） |
| `DatabaseSchema/DatabaseSchema.json` | 資料庫表定義（自動建表用） |

### 11.1 dbSetting.json

```json
{
  "DatabaseAddress": "127.0.0.1",
  "DataBaseName": "wsnCsharp",
  "DataBaseAccount": "wsn",
  "DataBasePassword": "wsn"
}
```

**產生的連線字串**：`Server={host};Database={db};User Id={user};Password={pass};TrustServerCertificate=true`

---

## 12. 日誌與監控

### 12.1 日誌配置

| 模式 | 輸出 | 保留天數 |
|------|------|---------|
| Windows Service | 檔案（`Log/ScadaEngine-*.log`） | 30 天 |
| Windows Service (Error) | 檔案（`Log/ScadaEngine-Error-*.log`） | 90 天 |
| Console (開發) | Console + 設定檔配置 | — |

### 12.2 系統狀態報告

Worker 每 60 秒輸出一次系統狀態，包含管理設備數量與各設備連線狀態。

### 12.3 關鍵日誌訊息

| 訊息 | 層級 | 含義 |
|------|------|------|
| `Modbus 連線建立成功` | Info | 設備 TCP 連線正常 |
| `Modbus 連線建立失敗` | Warning | 設備不可達 |
| `成功儲存 N 筆歷史資料` | Debug | 歷史資料已寫入 DB |
| `已更新 N 筆最新資料` | Debug | 最新值已更新 DB |
| `警報觸發: SID [type]` | Warning | 警報狀態轉換 |
| `警報恢復: SID [type]` | Info | 警報恢復正常 |
| `計算點位 SID 計算失敗` | Error | NCalc 公式錯誤 |

---

## 13. 容錯與可靠性

### 13.1 優雅關機

```
StopAsync()
  ├─ 取消事件訂閱
  ├─ StopAsync() 停止所有設備採集
  ├─ HistoryDataStorageService.Dispose() — 儲存剩餘歷史資料
  └─ RealtimeDataStorageService.Dispose() — 儲存剩餘最新值
```

### 13.2 容錯設計

| 故障場景 | 處理方式 |
|---------|---------|
| 設備 TCP 斷線 | 自動重連，產出 Bad Quality 資料 |
| MQTT Broker 不可達 | 30 秒定期重試，資料仍寫入 DB |
| 資料庫連線失敗 | 拋出例外，資料暫存於記憶體 |
| JSON 設定檔格式錯誤 | 跳過該設備，不影響其他設備 |
| NCalc 公式計算失敗 | 跳過該計算點位，回傳 null |
| 主迴圈例外 | 記錄錯誤，等待 5 秒後繼續 |

### 13.3 執行緒安全

- 所有跨執行緒共享集合使用 `ConcurrentDictionary`
- 儲存服務使用 `lock` 保護暫存區讀寫
- Modbus 連線使用 `SemaphoreSlim(1,1)` 確保同一設備不會同時讀取

---

## 14. 部署

### 14.1 Console 模式（開發用）

```bash
cd ScadaEngine.Engine
dotnet run
```

### 14.2 Windows Service 模式（生產用）

```powershell
# 發布
dotnet publish -c Release -o ./Publish

# 註冊 Windows 服務
sc.exe create "SCADA Engine Service" binPath="C:\path\to\ScadaEngine.Engine.exe"
sc.exe start "SCADA Engine Service"
```

### 14.3 相依服務

| 服務 | 必要性 | 說明 |
|------|--------|------|
| SQL Server | 必要 | 預設 127.0.0.1, DB=wsnCsharp |
| MQTT Broker | 必要 | 預設 127.0.0.1:1883（如 Mosquitto） |
| Modbus TCP 設備 | 選用 | 無設備時以空載模式執行 |
