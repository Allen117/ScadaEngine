# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

始終用繁體中文說明

依照 馬斯克 第一性原理

## Documentation Rules

新增或修改功能後，須同步更新 `docs/` 下對應的功能說明書。

## 實作計畫（Plan）規則

當使用者說「**規劃**」「**計畫**」「**plan**」「**先想清楚**」，
或任務符合以下**任一**條件時，先建立 plan.md 再動工：

- 跨多個檔案的新功能或重構
- 預計需跨多個對話才能完成
- 涉及設計決策（DB schema、架構選型、介面設計）

**流程**：

1. 依 `docs/plans/_template.md` 結構建立
2. 檔名：`docs/plans/YYYY-MM-DD-{kebab-任務名}.md`
3. **寫完 plan 後一律停下，等使用者明確說「OK」「開工」「執行」「動手」才開始實作**
   - ⚠️ 使用者回答 plan 內的提問**不算**動工授權，僅是補資訊以完善 plan
   - ⚠️ 即使提問都答完、plan 看似完整，也要等明確的 go-ahead，不可自行推進
   - 若 plan 因使用者回覆而需更新，先改 plan、再次停下等確認
   - ❌ 不可自設「回答完問題就動手」「說 OK 就動手」等暗示性條件來繞過此規則
4. 實作中即時更新勾選狀態
5. 完成後狀態改為「已完成」、回填 commit hash、搬到 `docs/plans/_archive/`

**不需要 plan.md**：單檔小修、問答、讀碼、bug 根因調查、使用者已給出明確一步到位指令。

個別 plan.md 已由 `docs/plans/.gitignore` 排除，不會進 git。詳細工作流見 `docs/plans/README.md`。

## Project Overview

.NET 8 SCADA 工業監控系統，包含 Engine（Modbus 資料採集 + MQTT 發布）與 Web（ASP.NET Core MVC 儀表板）。

## Code Architecture Rules

### Web 專案必須遵循 Feature Folder + MVC 架構

新增功能時，按以下結構建立檔案：

```
Features/{FeatureName}/
├── Controllers/{FeatureName}Controller.cs   ← 只放 Action 方法，不放 DTO/Model 類別
├── Models/                                   ← 所有 DTO、ViewModel、DB Model 放這裡
│   ├── {FeatureName}ViewModel.cs
│   └── {FeatureName}RequestDtos.cs
└── Views/{ActionName}.cshtml                 ← Razor View
```

**嚴格規則：**
- **Controller** 只負責接收請求、呼叫 Service、回傳結果，不含商業邏輯
- **Model/DTO 類別禁止寫在 Controller 或 Service 檔案底部**，必須放在 `Models/` 資料夾內各自的 `.cs` 檔
- **Service**（DB 操作、商業邏輯）放在 `Services/` 資料夾，透過 DI 注入 Controller
- 每個 `.cs` 檔只放一個主要 class（小型相關 DTO 可合併為一個檔案，如 `RequestDtos.cs`）
- 新 Service 須在 `Program.cs` 中註冊

### Razor View 前端分離規則

**CSS 和 JavaScript 禁止寫在 `.cshtml` 內**，必須抽離為獨立靜態檔案：

```
wwwroot/
├── css/{feature}.css    ← 該功能的樣式
└── js/{feature}.js      ← 該功能的邏輯（使用 IIFE 封裝，暴露至 window 供 View 呼叫）
```

- `.cshtml` 只保留 HTML/Razor 結構、`<link>` 引用 CSS、`@section Scripts { <script src> }` 引用 JS
- JS 中如有 HTML 實體（`&le;`、`&times;` 等），須轉換為 Unicode 跳脫（`\u2264`、`\u00d7`）
- JS 使用 IIFE `(function(){ ... })();` 封裝，對外介面掛在 `window._xx` 供 `onclick` 等屬性呼叫


## Build & Run Commands

```bash
# Run Engine (Modbus collector + MQTT publisher) — from repo root
cd ScadaEngine.Engine && dotnet run

# Run Web (ASP.NET Core dashboard) — from repo root
cd ScadaEngine.Web && dotnet run
# Or use the convenience script:
啟動登入頁.bat        # opens browser + starts Web

# Build entire solution
dotnet build ScadaEngine.sln

# Kill a stuck Web process on Windows
powershell.exe -Command "Get-Process -Name 'ScadaEngine.Web' | Stop-Process -Force"
```

Web runs on **http://localhost:5038** (HTTP) / **https://localhost:7189** (HTTPS).
Engine has no HTTP endpoint — it is a Windows Service / Console background process.

> **Important**: Razor views are precompiled. Any `.cshtml` change requires a **rebuild** (`dotnet build`) to take effect.

---

## Solution Architecture

```
ScadaEngine.sln
├── ScadaEngine.Common     — Shared models & DB config service (class library)
├── ScadaEngine.Algorithm  — Algorithm utilities (class library, currently minimal)
├── ScadaEngine.Engine     — .NET 8 Worker Service (Modbus → MQTT publisher)
└── ScadaEngine.Web        — .NET 8 ASP.NET Core MVC (dashboard, http://localhost:5038)
```

### Data Flow

```
Modbus TCP Devices
    ↓ FC01/02/03/04 polling (FluentModbus)
ScadaEngine.Engine / ModbusCommunicationService
    ↓ raw value × Ratio → engineering value, quality = Good/Bad
MqttPublishService  (Topic: SCADA/Realtime/{coordinatorName}/{SID}, QoS=1, Retain=true)
    ↓
MQTT Broker (127.0.0.1:1883, no auth)
    ↓ subscribe SCADA/Realtime/+/+
ScadaEngine.Web / MqttRealtimeSubscriberService
    ↓ ConcurrentDictionary<SID, RealtimeDataItemModel>
RealtimeController → /RealTime page
```

Engine also writes to SQL Server: `HistoryData` (time-series) and `LatestData` (upsert).

### DB 來源 Flow（外部寫入 → Engine polling → SCADA pipeline）

```
外部系統（PLC / 第三方 / 手動匯入）
    ↓ INSERT/UPDATE WHERE SID='DB1-S5' （Value 直接寫工程值）
DBLatestData  (SID, Value, Timestamp, Quality) — 統一入口表
    ↓ polling (per Coordinator PollingInterval, CommandTimeout=ConnectTimeout)：SELECT WHERE SID LIKE 'DB1-%'
DbCommunicationService (Engine BackgroundService)
    ├─ HistoryData：所有已配置 row 餵 HistoryDataStorageService（分鐘 dedup + 60s Timer），Timestamp = polling 當下時間（與 Modbus 一致）
    │   讀失敗（HasEverReadSuccessfully=true 後）→ 對全部 SID 寫 Quality=Bad、Value=LastSeen 最近成功值
    └─ 變化偵測（值/品質/DBLatestData.Timestamp 任一不同才繼續，僅擋下游噪音通道）
        ↓ UPSERT LatestData (Timestamp = DBLatestData.Timestamp)
        ↓ publish MQTT SCADA/Realtime/{Name}/{SID} → 後續警報 / 計算點 / 能源報表 / Web 即時數據
```

SID 命名 `DB{CoordinatorId}-S{Sequence}`（不與 `{ModbusID}-S{N}` 或 `CALC-S{N}` 撞號）；Sequence 由載入器以 JSON 陣列索引+1 自動產生（與 Modbus 對齊，JSON 不顯式指定），每個 Coordinator 上限 100 點。設定唯一來源是 `ScadaEngine.Engine/DBPoint/DB通訊檔案產生工具.xlsm` 的巨集 → `DBPoint/{SheetName}.json`，Engine 啟動讀全部 JSON、UPSERT by Name 到 DBCoordinator/DBPoints；Web 端「DB 來源」頁按「通知 Engine 重新載入 JSON」即發 `SCADA/Sys/DbCoordinator/Reload`（Retain=false），Engine 即時重建 polling loops。**Excel 中加新點位一律加最後一行**，中間插入會位移後續 SID。詳見 [docs/功能說明書_DB來源管理.md](docs/功能說明書_DB來源管理.md)。

> **HistoryData.Timestamp 的取捨**：DB 來源的 HistoryData.Timestamp 用 polling 當下時間（不抄 DBLatestData.Timestamp），與 Modbus 行為一致 — 外部停寫時 HistoryData 仍每分鐘一筆，trending / 用電報表時間軸不斷。LatestData / Realtime / MQTT 的 Timestamp 仍用 DBLatestData.Timestamp 反映外部寫入真實時間。

### Alarm MQTT Topic（未恢復警報即時推播）

```
AlarmMonitorService (Engine) 觸發/恢復
    ↓ AlarmMqttPublisher
SCADA/Alarm/Active/{SID}/{type}   QoS=1, Retain=true
    觸發 → 完整 JSON（severity/message/triggerValue/...）
    恢復 → 空 payload（清除 retained）
    ↓ subscribe SCADA/Alarm/Active/+/+
MqttAlarmSubscriberService (Web) → ConcurrentDictionary<"{SID}:{type}", ActiveAlarmItem>
    ↓ GET /Realtime/ActiveAlarms（記憶體讀取）
前端 active-alarm-panel.js（3 秒輪詢）
```

`{type}` ∈ `high` / `low` / `di`，對應 `EventLog.Operator` 2/3/4。Engine 啟動後 republish 一次目前 active 警報以覆蓋舊 retained；Web 啟動時從 `EventLog WHERE ClearedAt IS NULL AND EventType=0` 預填快取。

### 用電報表 Flow（On-demand 計算 + Engine 葉子層預聚合）

```
使用者按「查詢」 → /EnergyReport/api/query
    ↓
EnergyReportService.GetReportAsync(circuitId, granularity, start, end)
    1) GetByIdAsync                取迴路
    2) GetLeavesUnderAsync         遞迴展開所有葉子（綁 SID）
    3) BuildBoundaries(粒度,...)   產出 N+1 個邊界時刻
    4) for each leaf:
         OUTER APPLY HistoryData WHERE Timestamp <= boundary
           AND Timestamp >= boundary - MaxStalenessHours AND Quality=1   一條 SQL 取所有邊界值
         相鄰邊界相減 + 套 kWh 溢位規則 → N 個 delta
       各葉子 delta 同位置相加 → 該 bucket kWh
    ↓
EnergyReportResult { buckets[], totalKwh, isHasWarning }
    ↓
Chart.js 長條圖 + 表格；按「匯出 Excel」 → /EnergyReport/api/export → ClosedXML .xlsx
```

階層存於 `EnergyCircuit`（自參照樹，葉子綁 SID + MaxKwh，虛擬節點不綁）。每個節點有 `Sign` 欄位 (±1)，葉子有效權重 = 從查詢根到葉路徑上所有 sign 的乘積（不含查詢根本身），用以表達「總表 − 已分配子表」「A+B+C−D」這類算式；root 強制 +1，bucket 可為負值。溢位規則仍套在葉子原始 delta 上，sign 只在合併時套用。粒度綁固定期間選擇器（時=日、日=月、月=起訖月、年=起訖年），避免任意起訖造成 bucket 邊界語意混亂。HistoryData 是唯一資料來源，**Engine 只做葉子層 hourly 預聚合**（見下節），**sign / 階層 / 虛擬節點加總仍 on-demand 在 Web 端做**，迴路結構改變即時生效不需回填。詳細功能說明見 [docs/功能說明書_能源管理.md](docs/功能說明書_能源管理.md)。

#### 邊界值 Staleness Window

`EnergyReportService.GetBoundaryValuesAsync` 與 `EnergyLeafAggregator` 取「最近一筆 HistoryData」時加上 `Timestamp >= boundary - MaxStalenessHours` 條件（預設 2 小時，`appsettings.json:EnergyAggregation.MaxStalenessHours` 可調）。超過視窗一律視為 null，避免電表斷線復原時把整段累積差異全壓在恢復首小時的「巨柱」假象。**預聚合與 on-demand 必須讀同一份設定**，保住兩條路徑語意一致 — 不然「當前未完成那小時」（on-demand 算）和「上個小時」（讀 EnergyLeafHourly）會出現數字不連續。

#### 葉子層 Hourly 預聚合（Engine）

```
Engine BackgroundService (XX:02 觸發 + 啟動 catch-up)
    ├─ EnergyLeafAggregationService → 每葉子 SID 取 [hourStart, hourStart+1hr) 兩邊界值 → 套溢位 → UPSERT
    └─ EnergyLeafBackfillSubscriber (訂閱 SCADA/Sys/EnergyLeafHourly/Backfill, Retain=false)
         payload { "sid": null | "DB1-S5", "from": "yyyy-MM-dd", "to": "yyyy-MM-dd" }
         按月分批 + 每批 sleep BackfillBatchSleepMs (預設 500ms)
    ↓ UPSERT (MERGE on (SID, HourStart))
EnergyLeafHourly (SID, HourStart, DeltaKwh, Quality, IsRolledOver, CreatedAt)
```

**Sparse storage 三段語意**（與 `EnergyLeafAggregator.ComputeAsync` 一致）：
- **兩邊界都有值** → `Q=1, Delta=計算值（含 MaxKwh 溢位修正）`
- **只缺一邊**（staleness 過期或 SID 剛上線）→ `Q=0, Delta=0`（掉線 transition 標記，留給儀表板畫灰 bar）
- **兩邊都缺** → **不寫該列**（避免長期斷線 SID 灌爆空列；Web 端在記憶體用 dictionary lookup 補 0 即可）

觸發時點 XX:02 是給 DB 來源 polling buffer（避免 XX:00 整點時最末幾筆 HistoryData 還沒寫入）。啟動時做 `CatchUpHours`（預設 24）小時的補算覆蓋週末停機等情境。HourStart 用 local time（與 HistoryData.Timestamp 同基準，不轉 UTC）。Engine 完全不做 sign / 虛擬節點加總，那些仍由 Web on-demand 套用，確保迴路結構改變即時生效。Backfill MQTT 訊號典型用途：首次全量補算（sid=null）、單一 SID 在 MaxKwh 改動後重算歷史。

### 演算法 status 協定

LogicFlow `algorithm` 節點呼叫 Python `/algorithms/{name}/evaluate` 或 in-process C# 演算法時，回傳結構化 StatusCode：

```
result (dict<str,double>)        — 輸出值
statusCodeId / statusCodeName    — 例 10 / "DIVIDE_BY_ZERO"（merged，取 perOutput 中 severity 最高者）
severity                         — Info / Warning / Error
perOutput (dict<port,status>)    — 每個 output port (含 variadic suffix) 的 status
```

`Error` severity 走 **per-output port** 判斷：`LogicFlowExecutionService.HasUpstreamBad` 對 algorithm 節點依消費邊線的 `sourcePort` 查 `AlgoPerOutputStatus[sourcePort]`，**僅該 port 下游**節點不再評估、輸出節點本輪跳過不寫 Modbus；同節點其他 OK port 下游正常傳值。`Warning` / `Info` 仍傳遞結果，僅 UI 邊線反灰 + tooltip。

每個 output port 各自跑「OK ↔ 非 OK」切換偵測寫 EventLog，SID 格式 `ALGO:{nodeId}@{treeId}:{outputKey}`，Severity ≥ Warning 才寫（Info / WARMUP 不寫，避免雜訊）。混合狀態（cop1 Error / cop2 OK）會分兩條時序線記錄，不被 merged 掩蓋。

Python helper：`from _status import AlgoStatus, make_status, make_result`（`_*.py` 不會被當演算法載入）。
C# helper：`using ScadaEngine.Algorithms;`（`_*.cs` 不會被當演算法編譯，而是當共用 SyntaxTree 加入每個演算法的編譯，每個 algo 透過 `AlgorithmResult.Ok(...)` / `AlgorithmResult.From(..., AlgorithmStatusCode.DivideByZero)` 回傳）。

對照表單一來源：[docs/功能說明書_演算法服務.md](docs/功能說明書_演算法服務.md)。

UI：節點本身僅在「**所有 output port 都 Error**」時整顆 `.algo-status-bad` 反灰；混合狀態下節點正常、由邊線各自反灰：Error → 紅 `#dc3545` + `ah-bad`、Warning → 橘 `#fd7e14` + `ah-warn`。Hover 邊線顯示 SVG `<title>` tooltip：`{演算法名} : {outputKey} : {codeName} ({severity})`，**所有語系皆顯示英文 codeName**（不走 i18n）。

### 警報規則異動即時生效（控制 Topic）

```
Web AlarmRuleService (儲存 / 刪除規則) → DB AlarmRules
    ↓ AlarmRuleReloadPublisher
SCADA/Sys/AlarmRules/Reload   QoS=1, Retain=false
    payload: { "sid": "{SID}" | null }
    ↓
AlarmRuleReloadSubscriber (Engine BackgroundService)
    ↓ AlarmMonitorService.ReloadAndReevaluateAsync()
    1) ReloadRulesAsync (含孤立警報清掃)
    2) GetLatestDataAsync 取最新值（含計算點，因為 CalculatedPointService 寫入 LatestData）
    3) EvaluateBatchAsync → 觸發 / 恢復 → 寫 EventLog → 發 SCADA/Alarm/Active/...
```

`Sys` 命名空間預留給未來其他系統級控制訊號（避開 `SCADA/Control/#` Modbus 控制 CID 解析路徑）。Retain=false 避免 Engine 重啟時又跑一次最後一筆 reload；Engine 啟動時 `InitializeAsync` 本來就會做完整 reload，不需 retained 補位。`AlarmMonitorService` 內以 `SemaphoreSlim(1,1)` 序列化 reload 避免快取半更新狀態，並保留原 60 秒 Timer 作為 broker 失聯時的退路。

---

## Key Configuration Files

| File | Purpose |
|------|---------|
| `ScadaEngine.Engine/Setting/dbSetting.json` | SQL Server connection (host, DB, user, pass) |
| `ScadaEngine.Engine/MqttSetting/MqttSetting.json` | MQTT broker IP/port/topic/retain |
| `ScadaEngine.Engine/Modbus/Modbus.json` | Modbus device definitions (IP, port, tags) |
| `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` | DB table schema for auto-init |
| `ScadaEngine.Engine/DBPoint/*.json` | DB 來源 Coordinator 點位定義（由 `DB通訊檔案產生工具.xlsm` 巨集產生，Engine 啟動 + reload MQTT 訊號時載入） |

Web reads Engine's `dbSetting.json` via a relative path `../ScadaEngine.Engine/Setting/dbSetting.json` — both projects must run from their own directories.

---

## Database Schema (SQL Server: `wsnCsharp`)

| Table | Key Columns | Purpose |
|-------|-------------|---------|
| `ModbusCoordinator` | Id, Name, ModbusID, DelayTime, MonitorEnabled | Device registry (sidebar source) |
| `ModbusPoints` | SID (PK), Name, Address, DataType, Ratio, Unit | Point configuration |
| `HistoryData` | SID+Timestamp (PK), Value, Quality | Time-series history |
| `LatestData` | SID (PK), Value, Timestamp, Quality | Last known value per point |
| `Users` | UserID, Username, PasswordHash, Role, IsActive | Web login (SHA256 hex password) |
| `DBCoordinator` | Id, Name (UNIQUE), PollingInterval, ConnectTimeout, MonitorEnabled | DB 來源設備（由 `DBPoint/*.json` UPSERT by Name） |
| `DBPoints` | SID (PK), CoordinatorId, Sequence, Name, Unit, Min, Max | DB 來源點位定義（每 Coordinator 上限 100 點；Sequence 由載入器以陣列順序自動產生） |
| `DBLatestData` | SID (PK), Value, Timestamp, Quality | DB 來源統一入口表 — 外部系統 INSERT/UPDATE 此表（Value 寫工程值），Engine polling |

SID 格式：
- Modbus：`{ModbusID}-S{N}` 例 `196865-S1`
- 計算點位：`CALC-S{N}` 例 `CALC-S3`
- DB 來源：`DB{CoordinatorId}-S{Sequence}` 例 `DB1-S5`

---

## Web Project Structure

The Web project uses a **Features** folder layout alongside the conventional `Views/` folder:

```
ScadaEngine.Web/
├── Features/
│   ├── _ViewImports.cshtml          ← MUST exist for Tag Helpers to work in Features/
│   ├── Login/
│   │   ├── Controllers/LoginController.cs
│   │   ├── Models/LoginModel.cs
│   │   └── Views/Index.cshtml
│   └── Realtime/
│       ├── Controllers/RealtimeController.cs
│       ├── Models/RealtimeMonitorViewModel.cs
│       └── Views/Index.cshtml
├── Views/
│   ├── _ViewImports.cshtml          ← Only applies to Views/ subdirectory
│   └── Shared/_Layout.cshtml
├── Services/
│   ├── MqttRealtimeSubscriberService.cs   ← Singleton BackgroundService, MQTT subscriber
│   └── WebDatabaseService.cs
└── Program.cs
```

**Critical**: `_ViewImports.cshtml` in `Views/` does NOT apply to `Features/` views. The `Features/_ViewImports.cshtml` file is required for Tag Helpers (`asp-for`, `asp-action`, etc.) to work in Feature views.

View discovery is configured in `Program.cs` to look in both `/Views/{1}/{0}.cshtml` and `/Features/{1}/Views/{0}.cshtml`.


## Naming Conventions

This codebase uses Hungarian notation throughout:

| Prefix | Type | Example |
|--------|------|---------|
| `sz` | string | `szName`, `szBrokerIp` |
| `n` | int | `nPort`, `nTotalPoints` |
| `f` | float | `fValue`, `fRatio` |
| `d` | double | `dValue` |
| `dt` | DateTime | `dtTimestamp`, `dtLastUpdated` |
| `is` | bool | `isConnected`, `isMonitorEnabled` |
| `_` prefix | private field | `_logger`, `_mqttClient` |

---

## Key Patterns & Pitfalls

### Dapper Column Mapping
`CoordinatorModel` and other models use Hungarian property names (`szName`, `szModbusID`) that don't match DB column names (`Name`, `ModbusID`). Dapper maps by property name by default — the `[Column]` attribute is NOT used. Always use SQL aliases:
```sql
SELECT Name AS szName, ModbusID AS szModbusID, ...
FROM ModbusCoordinator
```

### MQTT JSON Parsing
The Web subscriber uses case-insensitive dictionary parsing to handle PascalCase/camelCase variations in the payload:
```csharp
var props = jsonDoc.RootElement.EnumerateObject()
    .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
```

### MqttRealtimeSubscriberService
Registered as both `AddSingleton` and `AddHostedService` so it can be injected into controllers by type and also run as a background service. Pre-fills cache from `ModbusPoints` table with `hasData=false` placeholders on startup so all configured points appear in the UI even before MQTT data arrives.

### MQTT Retain Flag
Engine publishes with `Retain=true`. When restarting Engine, old retained messages (without `name` field) remain on the broker. A full restart of both Engine and broker clears stale retained messages.

### IDataRepository (Scoped)
Defined in `ScadaEngine.Engine` but used by both Engine and Web. Web registers `SqlServerDataRepository` as Scoped. `MqttRealtimeSubscriberService` (Singleton) accesses it via `IServiceProvider.CreateScope()`.

---

## UI 設計系統

改動 `.cshtml` / `.css` / `wwwroot/` 前先讀 [docs/設計規範.md](docs/設計規範.md)，含框架、元件模式、色彩、字體、間距、圓角、陰影、動畫、Z-Index、圖示慣例。

## i18n 規則（zh-TW + en，僅指定頁面）

僅以下頁面已導入 i18n，新增/修改其字串時：

- **已 i18n 範圍**：ScadaPage、Realtime、EnergyReport、History/Trend、EventLog、AccountSetting、共用 `_Layout`
- **.cshtml 字串**：`@Localizer["key"]`（key 命名 `feature.section.purpose` 全小寫底線分）
- **JS 字串**：`window.i18n.t('key', {args})`，IIFE 內可宣告 `function t(key, args) { return window.i18n.t(key, args); }` 簡化
- **Controller / Service / Excel exporter**：建構子注入 `IStringLocalizer<T>`，走 `_l["key"].Value`。Singleton 服務若依賴此須改 Scoped
- **resx 檔位於 `ScadaEngine.Web/Resources/`**：中性 `.resx`（內容＝zh-TW）+ `.en.resx`，搭配 `[assembly: NeutralResourcesLanguage("zh-TW")]`。zh-TW 與 en 必須同步補
- **SCADA 專業詞先查 [`docs/i18n-glossary.md`](docs/i18n-glossary.md)**，新詞要先加進 glossary 再用

新增功能時，未在 i18n 範圍的頁面字串不需走 IStringLocalizer，但若**新增 Layout 側欄選單** 則該選單字必須走 `Views.Shared._Layout.{,en}.resx`。

**警報訊息結構化**（Engine 跨 Web）：Engine 寫 EventLog 時除人類可讀 `Message` 外，須帶 `MessageKey`（i18n key）+ `MessageArgs`（JSON）。Web 顯示時透過 `AlarmMessageLocalizer` 依 culture 翻譯。三類警報固定 key：
- `alarm.high_exceed` ← args `{ name, threshold }`
- `alarm.low_below` ← args `{ name, threshold }`
- `alarm.di_triggered` ← args `{ name, state }`

**控制操作訊息結構化**（Web 內部）：ScadaPage 控制行為由 `ControlEventLogger` 寫入 `EventLog`（EventType=3 資訊、Severity=3 低）。10 種 `control.action.*` key（button_pressed / ao_manual_set / ao_switch_auto / do_set_on / do_set_off / do_switch_auto / pump_start / pump_stop / pump_freq_set / pump_switch_auto），args 統一以 `{ username, name, value? }` 結構傳入。`AlarmMessageLocalizer.ParseArgs` 對 `control.action.*` 前綴的 key 委派 `ControlEventLogger.ArgOrderForKey` 取位置順序，避免重複維護。新增動作類型時改動：`ControlActionType` enum + `ControlEventLogger.BuildKeyAndArgs/ArgOrderForKey` 兩個 switch + `SharedResource.{,en}.resx` 雙語 key。

DB 內容（點位名、迴路名、警報規則的 DiOnLabel/DiOffLabel）為使用者輸入，**不在 i18n 範圍** — 切英文時若 user input 為中文，這是運維責任而非系統責任。

詳細架構見 [docs/功能說明書_多語系.md](docs/功能說明書_多語系.md)。
