# 功能說明書：DB 來源管理 (DbCoordinator)

## 1. 功能概述

DB 來源是 Modbus / 計算點位之外的**第三類點位來源**。專供「資料早就被別的系統寫進 SQL 表了」這類場景使用：外部系統（PLC、第三方系統、手動匯入工具）只負責把每筆量測值（**已是工程值**）寫進統一入口表 `DBLatestData`，Engine 自動 polling 後直接 UPSERT LatestData / HistoryData、發布 MQTT，於是這些「外部資料」就能享有警報、計算點位、能源報表、即時數據等所有 SCADA 既有功能 — 對下游而言完全是透明的。

### 核心能力
- **零下游改動**：採既有 SID 字串型別與 `DB{Id}-S{N}` 格式，警報 / 計算 / 能源報表 / MQTT 全部不需改判斷邏輯
- **每個 Coordinator 100 個 slot**：Sequence 由 JSON 陣列順序自動產生（與 Modbus 對齊），1~100
- **設定唯一來源**：`ScadaEngine.Engine/DBPoint/DB通訊檔案產生工具.xlsm` 的巨集 → 各 sheet → `DBPoint/{SheetName}.json`
- **即時生效**：Web 端按「通知 Engine 重新載入 JSON」→ MQTT `SCADA/Sys/DbCoordinator/Reload` → Engine 重讀 JSON、UPSERT DB、重建 polling loops，免重啟 Engine
- **DBLatestData.Value 即工程值**：外部系統直接寫工程值，Engine 不再做任何縮放（不再有 Ratio）
- **Phase 1 唯讀 UI**：Web `/DbCoordinator` 顯示已載入結果（Coordinator + 點位狀態），編輯透過 Excel 巨集

---

## 2. 路由

| 方法 | 路由 | 說明 | 認證 |
|------|------|------|------|
| GET  | `/DbCoordinator`        | DB 來源管理頁面（唯讀清單） | 需登入 |
| POST | `/DbCoordinator/Reload` | 通知 Engine 重新載入 `DBPoint/*.json`（發 MQTT） | 需登入 |

---

## 3. SID 命名

```
DB{CoordinatorId}-S{Sequence}
```

例：`DB1-S1`、`DB1-S100`、`DB2-S5`

- `CoordinatorId` 由 `DBCoordinator` 表 Identity 產生
- `Name UNIQUE + UPSERT by Name` → 同名 sheet 永遠拿到同一個 Id → SID 永遠穩定
- 與 `{ModbusID}-S{N}`、`CALC-S{N}` 純文字前綴不撞號

---

## 4. 資料表

### 4.1 `DBCoordinator`

| 欄位 | 型態 | 說明 |
|------|------|------|
| `Id` | int IDENTITY PK | SID 中的 `{CoordinatorId}` |
| `Name` | nvarchar(100) UNIQUE | = sheet name = JSON 檔名 |
| `PollingInterval` | int (default 1000) | 毫秒；Engine 內 clamp 下限 200ms |
| `ConnectTimeout` | int (default 1000) | 毫秒；polling 讀 DBLatestData 的 SqlCommand.CommandTimeout（向上取整為秒，最小 1 秒） |
| `MonitorEnabled` | bit (default 1) | 整個 Coordinator 開關 |
| `CreatedAt` | datetime | 系統維護 |

### 4.2 `DBPoints`

| 欄位 | 型態 | 說明 |
|------|------|------|
| `SID` | nvarchar(100) PK | `DB{Id}-S{Seq}` |
| `CoordinatorId` | int | FK → DBCoordinator.Id |
| `Sequence` | int (1~100) | 序號；由載入器以 JSON 陣列索引+1 自動產生（JSON 不顯式指定，與 Modbus 對齊） |
| `Name` | nvarchar(100) | 點位名稱 |
| `Unit` | nvarchar(50) | 物理單位 |
| `Min` | float (default 0) | 顯示下限 + 控制寫入下限（寫入路徑做時驗證） |
| `Max` | float (default 100) | 顯示上限 + 控制寫入上限（寫入路徑做時驗證） |

### 4.3 `DBLatestData`（統一入口表）

| 欄位 | 型態 | 說明 |
|------|------|------|
| `SID` | nvarchar(100) PK | 例 `DB1-S5` |
| `Value` | float | **工程值**（外部系統直接寫工程值，Engine 不再做縮放） |
| `Timestamp` | datetime | 由外部系統決定 |
| `Quality` | int | 1=Good, 0=Bad |

> 外部系統只需 `INSERT/UPDATE WHERE SID='DB{n}-S{m}'`。Engine 寫入路徑（Phase 2）也統一 UPDATE 此表，欄位 schema 永遠不變。

---

## 5. Data Flow

```
外部系統（PLC / 第三方 / 手動）
    ↓ INSERT/UPDATE WHERE SID='DB1-S5'  （Value 直接寫工程值）
DBLatestData
    ↓ polling (每 PollingInterval ms，CommandTimeout = ConnectTimeout)：SELECT WHERE SID LIKE 'DB1-%'
DbCommunicationService (Engine BackgroundService)
    ↓
    ├─ HistoryData 路徑（無條件）：所有已配置 row → HistoryDataStorageService.AddRealtimeDataBatch()
    │   → 分鐘 key dedup + 每 60 秒 Timer 批次寫 HistoryData
    │   → Timestamp = polling 當下時間（截到分鐘），與 Modbus 一致
    │
    └─ 變化偵測（值/品質/Timestamp 任一不同才繼續，僅擋下方四條路徑）
        ↓ UPSERT LatestData (Timestamp = DBLatestData.Timestamp)
        ↓ MqttPublishService → SCADA/Realtime/{Name}/{SID}
        ↓ AlarmMonitorService.EvaluateBatchAsync
        ↓ CalculatedPointService.CalculateAndPublish
SCADA 既有 pipeline（警報 / 計算點 / 能源報表 / Web 即時數據）
```

讀失敗分支（`GetDbLatestDataByPrefixAsync` 拋例外）：

```
SQL exception
    ↓
若 HasEverReadSuccessfully == false → 直接 return（避免 Engine 剛啟動就連線失敗時灌一堆 0 進歷史）
若 HasEverReadSuccessfully == true → 對 PointsBySid 全部 SID 構造
    { Value = LastSeen 最近一次成功值（無則 0）, Quality = "Bad", dtTimestamp = Now }
    → 同樣餵給 HistoryDataStorageService（保留歷史軌跡，明確標 Bad）
```

關鍵設計：
- **HistoryData.Timestamp 用 polling 當下時間（分鐘對齊）**：與 Modbus 行為一致，每分鐘穩定一筆。`DBLatestData.Timestamp` 僅作變化偵測比對基準，不再寫進 HistoryData
  - 動機：外部系統若停止更新 DBLatestData（Timestamp 完全停滯），HistoryData 仍要有完整時間軸供 trending / 用電報表使用
  - 推翻早期決策（HistoryData.Timestamp = DBLatestData.Timestamp）— 該設計在外部停寫場景會出現大段空缺
- **DBLatestData.Value 即工程值**：外部系統把已換算好的工程值寫進來，Engine 不再做 raw × Ratio 換算（DB 來源語意上就應該是已處理過的值）
- **變化偵測只擋下游噪音通道**：MQTT / 警報 / 計算點 / LatestData 在「值/品質/Timestamp 三者皆相同」時跳過，避免靜態資料反覆刷；HistoryData 不擋，依靠分鐘 dedup 自然降頻
- **讀失敗 → 寫 Bad 歷史**：DBLatestData 連線 / SQL 失敗時，若曾經成功讀過至少一次，仍寫 HistoryData（Quality=Bad，Value 用最近一次成功值），讓 trending 能標出資料中斷區間
- **未配置 SID**：DBLatestData 出現 `DB99-S1` 但 Coordinator 99 不存在 → log warning 一次後跳過，不爆

---

## 6. 設定檔示範：`DBPoint/Production.json`

```jsonc
{
  "Name": "Production",
  "PollingInterval": 1000,
  "ConnectTimeout": 1000,
  "MonitorEnabled": true,
  "Points": [
    { "Name": "CH1MOA",   "Unit": "",    "Min": 0, "Max": 100 },
    { "Name": "CH1RUN",   "Unit": "",    "Min": 0, "Max": 100 },
    { "Name": "CH1ALARM", "Unit": "",    "Min": 0, "Max": 100 },
    { "Name": "CH2MOA",   "Unit": "",    "Min": 0, "Max": 100 }
  ]
}
```

JSON 內**不放 Id 也不放 Sequence**：
- `Id`：由 Engine 啟動時 UPSERT by Name 從 DBCoordinator 表取得
- `Sequence`：由載入器以 JSON 陣列索引+1 自動產生（與 Modbus 一致）

⚠️ **編輯守則**：在 Excel 中加新點位**一律加最後一行**，不要插入中間，否則後續點位的 SID 會位移，破壞既有的警報規則 / 計算點 / 畫面綁定。

對應產生：
- DBCoordinator: `Id=1, Name='Production', PollingInterval=1000, ConnectTimeout=1000, MonitorEnabled=1`
- DBPoints: `DB1-S1`（CH1MOA, Min=0, Max=100）、`DB1-S2`（CH1RUN）、`DB1-S3`（CH1ALARM）...

外部系統要做的：把工程值寫入 `DBLatestData`，例 `DB1-S2` 寫 Value=1（即工程值，不再有 Ratio 換算）。

---

## 7. Reload 機制（Web → Engine 即時生效）

```
使用者改完 Excel → 跑巨集生成新 JSON → 切到 Web /DbCoordinator → 按「通知 Engine 重新載入 JSON」
    ↓
DbCoordinatorReloadPublisher (Web Singleton + IHostedService)
    ↓ MQTT 發布 SCADA/Sys/DbCoordinator/Reload   QoS=1, Retain=false
    ↓
DbCoordinatorReloadSubscriber (Engine BackgroundService)
    ↓ DbCommunicationService.ReloadAsync()
        1) 取消舊的所有 polling loops（CancellationTokenSource.Cancel）
        2) DbCoordinatorJsonLoader.LoadAllAsync 重讀全部 JSON、UPSERT DB
        3) 為每個 enabled Coordinator 啟動新的 polling loop
```

設計重點：
- **Retain=false**：Engine 重啟時不會 replay 最後一筆 reload（Engine 啟動本來就會做完整 reload）
- **SemaphoreSlim(1,1) 序列化**：避免 reload 與 polling loop 半更新狀態
- **失敗只 log**：MQTT broker 失聯時 reload 通知不擋 UI，使用者重按或重啟 Engine 即可

---

## 8. UI

### 8.1 Web 頁面 `/DbCoordinator`

採用與 `/CommSetting` 一致的「左側清單 + 右側詳情」雙欄佈局：

- **上方按鈕列**：「重新整理頁面」「通知 Engine 重新載入 JSON」
- **左側「DB 通訊」卡**：列出所有已載入的 DB Coordinator（依 `DBPoint/*.json` 順序），停用者顯示灰色「停用」徽章
- **右側「設備詳細資料」卡**：點擊左側項目後顯示該 Coordinator 的 Id / Name / PollingInterval / ConnectTimeout / MonitorEnabled / 點位數
- 預設自動載入第一筆 Coordinator
- 唯讀（Phase 1）；編輯透過 `DBPoint/DB通訊檔案產生工具.xlsm`，按右上「通知 Engine 重新載入 JSON」即時生效

### 8.2 SID 下拉清單整合

DB 來源 SID 已自動納入下列功能的「選擇 SID」下拉：
- 警報設定（`/AlarmSetting`）
- 條件控制（`/ConditionCtrl`）
- 計算點位的輸入變數（`/CalcPoint`）
- 歷史趨勢（`/History/Trend`）
- 畫面設計（`/Designer`）— 點位 GroupName="DB"
- 電表/迴路設定（`/EnergyMeter`）— 僅 Unit=kWh 的點位
- 即時數據快取預填（`/RealTime`）

---

## 9. Phase 2（寫入路徑）

寫入路徑統一執行 `UPDATE DBLatestData SET Value=工程值, Timestamp=GETDATE(), Quality=1 WHERE SID=@sid`，並先驗證寫入值在 `DBPoints.Min ≤ Value ≤ Max` 範圍內（超出 → log warning 並回傳 false，不寫）。寫入語意採 last-writer-wins（與 SCADA 控制本來就是的語義一致）。

| 路徑 | 狀態 | 備註 |
|------|------|------|
| 手動控制頁面對所有 DB SID 開放（每個 DB 點都可讀可寫） | 暫不開工 | 由手動控制頁未來擴充 |
| **LogicFlow 節點輸出可寫** | **已實作** | output 節點綁 DB SID 時，`LogicFlowExecutionService.ExecuteControlWriteAsync` 以 SID 前綴 `DB` 分流走 `IDataRepository.UpdateDbLatestDataAsync`；ManualControl 互斥、啟動保護期、重試機制與 Modbus 寫入一致 |
| MQTT `SCADA/Control/{SID}` 訂閱 | 暫不開工 | — |

---

## 10. 已知風險與緩解

| 風險 | 緩解 |
|------|------|
| PollingInterval 設過短把連線池打滿 | `DbCommunicationService` 內 clamp `MIN_POLLING_INTERVAL_MS=200` |
| Coordinator 已建但外部尚未接通 → 每秒空 query | `MonitorEnabled=false` 整個 Coordinator 暫停；Phase 1 不做更細的「無資料就退避」 |
| 外部系統與 Engine 同時寫 DBLatestData（Phase 2） | SQL Server row-level lock 處理；last-writer-wins 為控制系統合理語意 |
| 外部系統時鐘不準導致 Web 端 STALE 警示 | 使用者問題；Engine 不修正時間。Realtime / LatestData 的 Timestamp 仍來自 DBLatestData 以反映外部寫入真實時間，HistoryData 則用 polling 當下時間以保證時間軸完整 |
| 外部停止更新 DBLatestData 時 HistoryData trending 看到一條完美水平線 | **這是設計取捨** — HistoryData 寫入脫鉤外部 timestamp 才能在停寫場景仍有資料軌跡。若有「停滯偵測」需求應由外部寫入端把 Quality 改成 Bad，或另開 watchdog 議題 |

---

## 11. 相關檔案

### Engine
- `ScadaEngine.Engine/Models/DbCoordinatorModel.cs`
- `ScadaEngine.Engine/Models/DbPointModel.cs`
- `ScadaEngine.Engine/Services/DbCoordinatorJsonLoader.cs`
- `ScadaEngine.Engine/Services/DbCommunicationService.cs`
- `ScadaEngine.Engine/Services/DbCoordinatorReloadSubscriber.cs`
- `ScadaEngine.Engine/DBPoint/*.json`（含 `DB通訊檔案產生工具.xlsm`）

### Web
- `ScadaEngine.Web/Features/DbCoordinator/Controllers/DbCoordinatorController.cs`
- `ScadaEngine.Web/Features/DbCoordinator/Models/DbCoordinatorViewModel.cs`
- `ScadaEngine.Web/Features/DbCoordinator/Views/Index.cshtml`
- `ScadaEngine.Web/Services/DbCoordinatorService.cs`
- `ScadaEngine.Web/Services/DbCoordinatorReloadPublisher.cs`
- `ScadaEngine.Web/wwwroot/css/dbcoordinator.css`
- `ScadaEngine.Web/wwwroot/js/dbcoordinator.js`

### 共用
- `ScadaEngine.Engine/Data/Interfaces/IDataRepository.cs` — 新增 `GetAllDbCoordinatorsAsync` / `GetAllDbPointsAsync` / `GetDbPointsByCoordinatorIdAsync` / `SaveDbCoordinatorAsync` / `SaveDbPointsAsync` / `GetDbLatestDataByPrefixAsync`
- `ScadaEngine.Engine/Data/Repositories/SqlServerDataRepository.cs` — 對應實作
- `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` — DBCoordinator / DBPoints / DBLatestData 三張表
