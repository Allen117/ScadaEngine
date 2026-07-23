# 功能說明書：SCADA 監控頁 (ScadaPage)

## 1. 功能概述

SCADA 監控頁是本系統的**核心即時視覺化操控介面**，讓使用者透過圖形化畫面即時監看設備狀態並下達控制指令。頁面內容由「畫面設計 (Designer)」頁面建立並發布，ScadaPage 負責載入已發布的設計並綁定即時數據。

### 核心能力
- **載入已發布設計**：從 Designer 取得頁面定義（包含畫布尺寸、背景圖、Widget 佈局）
- **即時數據綁定**：每秒向 `/api/realtime/latest` 輪詢，驅動 Widget 顯示即時值
- **控制指令發送**：透過 `/api/control/write` API 下達 MQTT 控制指令寫入設備
- **頁面樹導覽**：多頁面、多層級的樹狀結構切換
- **權限控制**：按頁面粒度控制「可檢視 / 可操控」權限
- **警報著色**：結合警報規則，超限時自動變色提示

---

## 2. 路由

| 方法 | 路由 | 說明 | 認證 |
|------|------|------|------|
| GET | `/ScadaPage` | 監控頁面 | 需登入 |
| POST | `/api/control/write` | 控制指令寫入 | 需登入 |
| GET | `/api/control/manual-values` | 取得所有手動控制值 | 需登入 |
| POST | `/api/scadapage/accumulation` | 累積量元件批次查詢（當日/當月累積，30 秒輪詢） | 需登入 |
| POST | `/api/scadapage/circuit-metric` | 迴路指標元件批次查詢（四指標，30 秒輪詢，上限 50 筆/批） | 需登入 |

### 依賴的外部 API
| 方法 | 路由 | 用途 |
|------|------|------|
| GET | `/Designer/Load` | 載入已發布的設計頁面定義 |
| GET | `/api/realtime/latest` | 取得即時數據（每秒輪詢） |
| GET | `/api/alarm-rules` | 取得警報規則（初始化時載入一次） |

---

## 3. 檔案結構

```
Features/ScadaPage/
├── Controllers/
│   ├── ScadaPageController.cs    ← GET /ScadaPage 頁面入口
│   └── ControlController.cs      ← POST /api/control/write, GET /api/control/manual-values
├── Models/
│   └── ControlWriteDto.cs        ← 控制指令 DTO
└── Views/
    └── Index.cshtml              ← 頁面結構（頁面樹 + 畫布區）

Views/Shared/
└── _Layout.cshtml                ← 共用版面配置

wwwroot/
├── css/scadapage.css             ← 元件互動樣式（hover 標籤、警報脈動、泵旋轉、按鈕 3D 效果）
└── js/scadapage.js               ← 核心渲染引擎（IIFE，~1700 行）
```

---

## 4. 資料流架構

```
使用者開啟 /ScadaPage
    │
    ├─ Controller 注入權限資料 → window._isAdmin, window._scadaPagePerms
    │
    ├─ JS 初始化 ───────────────────────────────────────────────────
    │   ├─ fetch('/Designer/Load')          → 取得已發布設計（頁面樹 + Widget 定義）
    │   ├─ fetch('/api/alarm-rules')        → 載入警報規則（快取至 _alarmRuleMap）
    │   ├─ fetch('/api/control/manual-values') → 載入手動控制值快取（_aoManualValueMap）
    │   └─ fetchAndUpdateGauges()            → 首次取得即時數據
    │
    ├─ setInterval(fetchAndUpdateGauges, 1000)  ← 每秒輪詢即時數據
    │   └─ fetch('/api/realtime/latest')
    │       └─ updateScadaWidgets(data)     → 更新所有 Widget 顯示
    │
    └─ 使用者控制操作 ─────────────────────────────────────────────
        ├─ controlBtn 點擊        → POST /api/control/write { cid, value }
        ├─ AO 右鍵「手動控制」     → POST /api/control/write { cid, value, mode:"manual" }
        ├─ AO 右鍵「自動控制」     → POST /api/control/write { cid, mode:"auto" }
        ├─ DO 右鍵「ON/OFF」       → POST /api/control/write { cid, value }
        ├─ DO 右鍵「自動控制」     → POST /api/control/write { cid, mode:"auto" }
        ├─ Pump 啟動/停止          → POST /api/control/write { cid, value:1/0 }
        ├─ Pump 啟停切自動         → POST /api/control/write { cidStartStop, mode:"auto" }
        ├─ Pump 頻率設定           → POST /api/control/write { cidFreqSet, value:Hz值 }
        ├─ Pump 頻率切自動         → POST /api/control/write { cidFreqSet, mode:"auto" }
        ├─ Pump Gauge 拖拽         → mouseup 時 POST /api/control/write { cid, value:Hz值 }
        └─ 右鍵「趨勢圖」         → 寫入 localStorage，導向歷史趨勢頁
```

---

## 5. 控制器說明

### 5.1 ScadaPageController

簡單的頁面入口控制器，主要負責將權限資料注入前端。

| Action | 說明 |
|--------|------|
| `Index()` | 取得使用者角色 (`IsAdmin`)、頁面層級權限 (`scadaPages`)，透過 `ViewData` 注入前端 |

**權限注入機制**：
- `window._isAdmin`：布林值，Admin 擁有所有頁面的完整權限
- `window._scadaPagePerms`：JSON 物件 `{ pageId: { canView, canControl } }`

### 5.2 ControlController

API 控制器，處理所有控制指令的接收與派發。

#### POST `/api/control/write`

接收 `ControlWriteDto`，根據 `mode` 欄位分三種模式處理：

| 模式 | mode 值 | MQTT 發送 | DB 儲存 | 用途 |
|------|---------|-----------|---------|------|
| **手動** | `""` (預設) | 發送至 Engine | 儲存手動控制值 | 使用者手動操作 |
| **自動** | `"auto"` | 不發送 | 標記為自動控制 | 切換回自動控制邏輯 |
| **LogicFlow** | `"logicflow"` | 發送至 Engine | 不儲存 | 流程圖自動控制 |

**MQTT 控制指令流程**：
```
Web POST /api/control/write
    → MqttRealtimeSubscriberService.PublishControlCommandAsync(cid, value)
    → MQTT Broker (Topic: SCADA/Control/{CID})
    → Engine 接收並執行 Modbus 寫入
```

#### GET `/api/control/manual-values`

回傳所有已儲存的手動控制值，格式：
```json
{
  "196865-S10": { "value": 50.0, "isAuto": false },
  "196865-S20": { "value": 0,    "isAuto": true  }
}
```

---

## 6. 資料模型

### ControlWriteDto

| 屬性 | 型別 | 說明 |
|------|------|------|
| `cid` | string | 控制點位 ID (CID)，即目標 SID |
| `value` | double | 寫入數值（預設 1） |
| `mode` | string | 控制模式：`""` / `"manual"` / `"auto"` / `"logicflow"` |
| `szCid` | string | cid 別名（Log 用） |
| `nValue` | double | value 別名（Log 用） |

---

## 7. 前端渲染引擎 (scadapage.js)

以 IIFE 封裝，總計約 1700 行，不對外暴露 `window._xx`（所有互動透過 DOM 事件處理）。

### 7.1 初始化流程

```
DOMContentLoaded
    → initScadaViewer()       ← 載入設計、建構頁面樹、渲染首頁
    → _loadAlarmRules()       ← 載入警報規則快取
    → _loadManualControlValues() ← 載入手動控制值快取
    → fetchAndUpdateGauges()  ← 首次取得即時數據
    → setInterval(1000ms)     ← 每秒輪詢更新
```

### 7.2 設計載入與頁面樹

1. 呼叫 `GET /Designer/Load` 取得已發布設計
2. 建構 `nodeMap`（以 `szPageSid` 為 key）
3. 透過 `szParentPageSid` 建構父子關係，形成多層樹結構
4. 依 `nSortOrder` 排序
5. **權限篩選**：非 Admin 使用者只能看到 `canView=true` 的頁面
6. 渲染左側頁面樹，自動選取第一頁

### 頁面樹摺疊（Collapse / Expand）

- 有子節點的父頁面在名稱左側顯示 caret 圖示（純視覺指示，不可點擊）：
  - **展開時**：`fa-caret-down`（灰色）
  - **摺疊時**：`fa-caret-right`（藍色加粗），同時 hover 提示「雙擊展開子項 (N)」，作為「下面還有子畫面」的視覺暗示
- **互動方式**：
  - **單擊**父節點 → 選取該頁面（與葉節點一致）
  - **雙擊**父節點 → 切換摺疊／展開狀態
  - 葉節點不接受雙擊
- 葉節點以等寬空白佔位保持與父節點對齊
- `user-select:none` 防止雙擊選字
- 摺疊狀態以 localStorage key `scadaPage_collapsed_v1` 持久化（陣列形式存所有摺疊節點 ID），跨重新整理保留

### 頁面節點屬性

| 屬性 | 說明 |
|------|------|
| `szPageSid` | 頁面唯一識別碼 |
| `szPageName` | 頁面顯示名稱 |
| `szPageIcon` | Font Awesome 圖示 class |
| `szParentPageSid` | 父頁面 ID（null 表示根節點） |
| `nSortOrder` | 排序順序 |
| `nCanvasW` / `nCanvasH` | 畫布寬高（預設 1200×800） |
| `szBgDataUrl` | 背景圖 Data URL |
| `szWidgetStateJson` | Widget 狀態 JSON 陣列 |

### 7.3 畫布渲染

- 設定畫布寬高與背景圖
- 遍歷 `arrWidgetState` 逐一渲染 Widget
- 套用等比縮放（`_applyCanvasScale`）讓畫布自適應容器大小
- 監聽 `window.resize` 事件自動重算縮放

### 7.4 Widget 類型

系統支援 **8 種 Widget**：

#### (1) gauge — 半圓儀表板

| 屬性 | 說明 |
|------|------|
| `szSid` | 綁定的點位 SID |
| `fMin` / `fMax` | 量程範圍 |
| `szUnit` | 單位 |
| `szColor` | 主色 (預設 `#00c0ff`) |
| `szHighColor` / `szLowColor` | 高低警報色 |
| `szBgColor` | 背景色 |
| `szTitle` | Hover 顯示標題 |

- SVG 半圓弧形，200×145 viewBox
- 即時更新弧形角度與數值
- 品質 BAD 時顯示「斷線」
- 結合警報規則，超限時弧形與數值變色
- 右鍵可加入趨勢圖清單

#### (2) realtimeValue — 即時數值顯示

| 屬性 | 說明 |
|------|------|
| `szSid` | 綁定的點位 SID |
| `nFontSize` | 字體大小（預設 28） |
| `szFontColor` | 字體色 |
| `szUnit` | 單位 |
| `szHighColor` / `szLowColor` | 警報變色 |
| `szValueMode` | 顯示模式：不帶（即時值，預設）/ `day`（當日累積）/ `month`（當月累積） |
| `szAccKind` | 累積計算方式：`meter`（累積讀值差值）/ `integrate`（瞬時值時間積分）— 僅累積模式 |
| `dMaxValue` | meter 溢位上限（如電錶最大讀值），未設時倒退視為歸零 — 僅 meter |
| `szAccUnit` | 累積顯示單位（如 kW 積分後填 kWh），空則沿用 `szUnit` — 僅累積模式 |
| `nAccDecimals` | 累積值小數位數（0–4，預設 1；迴路指標模式沿用此鍵）— 僅累積/迴路指標模式 |
| `nCircuitId` / `szCircuitName` / `szMetric` | 迴路指標綁定（與 `szSid` 互斥；見下方「迴路指標模式」）— 僅迴路指標模式 |

- 純數值顯示，支援單位標示
- 品質 BAD 時顯示紅色「斷線」
- 結合警報規則變色
- 右鍵可加入趨勢圖清單

**顯示模式 = 當日/當月累積**（plan 2026-07-14-scadapage-accumulation-widget）：

- Designer 屬性面板「顯示模式」select 切換；切回即時值時 5 個累積鍵全部 `delete`，`szWidgetStateJson` 不留新欄位（既有頁面 JSON 完全向後相容）
- 執行期改掛 **`scada-rt-acc`** class（不掛 `scada-rt-value`），1 秒即時迴圈自然跳過，由獨立 **30 秒輪詢** `fetchAndUpdateAccumulations()` → `POST /api/scadapage/accumulation`（批次、以 sid+mode+kind+max 去重）更新
- 執行期畫面不顯示 badge，hover tooltip 顯示「點位名稱 + 日累/月累」（i18n：`scadapage.acc.day_badge` / `scadapage.acc.month_badge`）；Designer 編輯期保留左上角「日累/月累」badge 供辨識
- 兩種累積計算：
  - **meter**：當期累積 = 現值 − 期初（今日 00:00 / 本月 1 號 00:00）最近一筆 Quality=1 邊界值，溢位語意同 EnergyReport（`end<start` 且有 MaxValue → `(Max−Vs)+Ve`；無 → 視為歸零記 0 + 警告）。已知限制：整期只偵測一次溢位
  - **integrate**：對 HistoryData 每分鐘值做左矩形時間積分（值 × Δt 小時），段長 clamp 至 `min(下一筆, 期末, 本筆+MaxGap 5 分)`，掉線時段不灌水
- 後端 `WidgetAccumulationService`（Scoped）+ `WidgetAccumulationCache`（Singleton）分層快取：完成日（L1）/今日完成小時（L2）bucket 永久快取、當前小時尾段（L3）即時算、結果 30 秒微快取（L4）；穩態每 SID 每輪詢僅掃當前小時 ≤60 筆，整月掃描只在首次補洞發生一次；跨日/跨月由 cache key 含日期自然切期 + 每日 prune
- 狀態呈現：`no_data`（期初/期內無資料）→ 灰字 `--`；`stale`（點位資料過舊）→ 值以灰色呈現；累積模式**不套用**警報高低限著色（警報規則針對瞬時值）
- 設定：`appsettings.json` `ScadaPageAccumulation`（`MaxStalenessHours` 預設 2、`MaxGapMinutes` 預設 5、`ResultCacheSeconds` 預設 30）
- 綁定 SID 恰為某迴路的 kWh 累積讀值 SID、且溢位上限空白時，自動帶入該迴路的 `MaxKwh`（綁定當下與切到累積模式時皆會嘗試）

**顯示模式 = 迴路指標**（plan 2026-07-23-designer-table-ems-accumulation-autofill）：

AI 點位除綁單一 SID 外，也可在 picker 的「**迴路**」來源分頁直接綁 `EnergyCircuit`（含**虛擬迴路**如虛擬主要電表），依綁定型別自動切換屬性面板（SID 型顯示模式區塊 ↔ 迴路指標四選）。

四指標語意（「本月」刻意三分，曆月與期別可同表比對）：

| `szMetric` | 名稱 | 期界 | 計算來源 |
|------------|------|------|---------|
| `day_kwh` | 本日度數 | 曆日（今日 00:00 起） | `EnergyReportService.GetBucketKwhForRangesAsync`（遞迴葉子 × EffectiveSign、staleness、溢位，與用電報表同核心） |
| `month_kwh` | 本月度數 | 曆月（本月 1 號 00:00 起） | 同上 |
| `period_kwh` | 本月電度 | 月結期別（BillingPeriods） | `ElectricityCostService.GetStatusAsync().totalKwh` — 與 EMS 電費狀態卡**同一支方法、零重算** |
| `period_cost` | 本月電費 | 月結期別 | 同上 `.totalCost`（progressive/surcharge 子迴路為占比分攤估算 `isEstimated`，tooltip 註記「（估算）」） |

- 綁定鍵：`nCircuitId` + `szCircuitName`（快照，執行期以 id 為準）+ `szMetric`，與 `szSid` 互斥（綁迴路清 SID 鍵與 SID 型累積鍵；改綁點位時迴路鍵全 `delete`）— 未使用者 JSON 零改動
- 執行期掛 **`scada-rt-cmetric`** class（1 秒即時迴圈與 SID 累積輪詢皆不觸碰），由獨立 **30 秒輪詢** `fetchAndUpdateCircuitMetrics()` → `POST /api/scadapage/circuit-metric`（批次、以 circuitId+metric 去重、**超過 50 筆自動分批**）更新
- 後端 `WidgetCircuitMetricService`（Scoped）+ `WidgetCircuitMetricCache`（Singleton，結果 TTL 60 秒 + per-key 鎖防 stampede）；30s 輪詢 × 60s 快取下負載與 EMS 首頁同量級。設定：`appsettings.json` `ScadaPageCircuitMetric:ResultCacheSeconds`（預設 60）
- 狀態呈現：`no_data`（迴路已刪除/無綁 SID 葉子）與 `no_plan`（電費指標但未選電價方案）→ 灰字 `--`；`stale`（部分邊界值缺）→ 值以灰色呈現
- 單位自動決定：kWh 指標固定 kWh；`period_cost` 依語系顯示「元 / NT$」。hover tooltip = 迴路名 + 指標名
- Designer 編輯期左上角綠色 badge 顯示指標縮寫（日度/月度/期度/期費）
- 迴路被刪除後：執行期回 `no_data` 灰字（依 `nCircuitId` 查無迴路），Designer 顯示快照名 — 不自動清 JSON

#### (3) diPoint — DI 數位輸入點位

| 屬性 | 說明 |
|------|------|
| `szSid` | 綁定的點位 SID |
| `szDisplayMode` | 顯示模式：`indicator`（圓形燈號）/ `text`（文字） |
| `szOnColor` / `szOffColor` | ON/OFF 顏色 |
| `szOnLabel` / `szOffLabel` | ON/OFF 文字（text 模式） |
| `nIndicatorSize` | 燈號直徑 |
| `szAlarmColor` | 警報色 |

- ON/OFF 判定邏輯：值為 1、'1'、true、'ON' 或 ≥1 視為 ON
- 結合 DI 警報規則，觸發條件匹配時脈動動畫 (`di-alarm-pulse`)
- 右鍵可加入趨勢圖清單

#### (4) controlBtn — 控制按鈕

| 屬性 | 說明 |
|------|------|
| `szCid` | 控制目標 CID |
| `szBtnLabel` | 按鈕文字 |
| `szBtnIcon` | Font Awesome 圖示 |
| `fCtrlValue` | 下達數值（預設 1） |
| `szBtnColor` | 按鈕顏色 |

- 點擊時彈出 `confirm` 確認對話框
- 發送 `POST /api/control/write { cid, value }`（預設手動模式）
- 未綁定 CID 時按鈕 disabled
- 需有 `canControl` 權限才綁定點擊事件

#### (5) aoPoint — AO 類比輸出點位

| 屬性 | 說明 |
|------|------|
| `szCid` | 控制目標 CID |
| `szDisplayName` | 顯示名稱 |
| `szUnit` | 單位 |
| `fMin` / `fMax` / `fStep` | 輸入範圍與步進 |
| `nDecimalPlaces` | 小數位數 |
| `szMenuManualLabel` | 右鍵「手動控制」選項文字 |
| `szMenuAutoLabel` | 右鍵「自動控制」選項文字 |

**右鍵選單操作**：
- **手動控制**：輸入數值 → 範圍驗證 → `POST { cid, value, mode:"manual" }`
- **自動控制**：confirm 確認 → `POST { cid, mode:"auto" }`
- 當前模式以藍色外框高亮 (`_applyActiveStyle`)

#### (6) doPoint — DO 數位輸出點位

| 屬性 | 說明 |
|------|------|
| `szCid` | 控制目標 CID |
| `szDisplayName` | 顯示名稱 |
| `nOnValue` / `nOffValue` | ON/OFF 寫入值（預設 1/0） |
| `szMenuOnLabel` / `szMenuOffLabel` | 右鍵 ON/OFF 選項文字 |
| `szMenuAutoLabel` | 右鍵「自動控制」選項文字 |

**右鍵選單操作**：
- **ON**：confirm → `POST { cid, value: nOnValue }`
- **OFF**：confirm → `POST { cid, value: nOffValue }`
- **自動控制**：confirm → `POST { cid, mode:"auto" }`
- 當前狀態以藍色外框高亮

#### (7) pump — 水泵元件

| 屬性 | 說明 |
|------|------|
| `szSidRun` | 運轉狀態 SID（DI 讀取） |
| `szSidFault` | 故障狀態 SID（DI 讀取） |
| `szSidMode` | 手/自動模式 SID |
| `szSidFreq` | 頻率回授 SID |
| `szCidStartStop` | 啟停控制 CID |
| `szCidFreqSet` | 頻率設定 CID |
| `nFreqSetMin` / `nFreqSetMax` | 頻率設定範圍 |
| `nFreqMax` | 頻率 Gauge 最大刻度 |
| `szRunColor` / `szStopColor` / `szFaultColor` | 各狀態色 |
| `szManualColor` / `szAutoColor` | 手/自動模式色 |
| `szOutletDir` | 出口方向（right / left / up） |

**SVG 渲染**：
- 泵體（含基座、管路、葉片）以 SVG 繪製
- 運轉中：葉片旋轉動畫 (`pump-spin`)
- 故障：整體變紅
- 手/自動：泵體顏色切換（黃/藍）
- 頻率 Linear Gauge：直條圖顯示目前頻率

**右鍵選單**（二級結構）：
- **啟動停止**（子選單）：啟動 / 停止 / 自動控制
- **頻率設定**（子選單）：輸入框 + 確定按鈕 / 自動控制
- **監控點位**（勾選框）：可勾選運轉、故障、模式、頻率等 SID 加入趨勢圖

> 頻率設定的「確定」走手動模式（M 角標亮起），「自動控制」對 `szCidFreqSet` 標記為自動（M 角標熄滅）；與啟動停止的手/自切換相互獨立。

**Gauge 拖拽**：
- 滑鼠在頻率 Gauge 直條上拖拽可即時調整
- mousedown → mousemove 更新視覺 → mouseup 發送控制指令

#### (8) table — 資料表格

| 屬性 | 說明 |
|------|------|
| `nCols` / `nRows` | 欄位數 / 列數（欄數不設上限，ScadaPage 依 `nCols` 完整顯示） |
| `szHeaderColor` | 表頭背景色 |
| `szBodyBgOdd` / `szBodyBgEven` | 奇/偶數資料列底色（`null` = 沿用預設外觀） |
| `szBorderColor` | 資料列分隔線色（`null` = 預設 `#f0f0f0`） |
| `arrCells` | 二維陣列，每格可綁定 SID 或迴路指標 |
| `arrColDecimals` | 各欄小數位數（迴路指標 cell 亦沿用） |

- 每格可綁定 AI 或 DI 點位
- AI：數值顯示 + 警報變色
- DI：ON/OFF 文字 + 警報變色
- 品質 BAD 時顯示「斷線」
- 右鍵可針對單一儲存格加入趨勢圖

**迴路指標 cell**（plan 2026-07-23）：資料 cell 也可在 picker「迴路」分頁綁 `EnergyCircuit` 四指標
（cell 級新鍵 `nCircuitId` / `szCircuitName` / `szMetric`，與 `szSid` 互斥，解除綁定時全 `delete`）。
四指標語意與計算來源同 realtimeValue「迴路指標模式」一節。執行期 td 掛 `scada-cmetric-cell` class 且**不帶 `data-sid`**
→ 1 秒 `td[data-sid]` 路徑不觸碰，由 30 秒 `fetchAndUpdateCircuitMetrics()` 更新；`title` 屬性 hover 顯示「迴路名＋指標名」。

**表頭驅動整列自動帶入**（plan 2026-07-23 決策 2）：表格慣例 = 第 1 欄（col 0）列名、第 1 列（row 0）欄名。
在某資料 cell 綁定**迴路**、或綁定的**點位 SID 反查命中迴路**（`EnergyCircuit` 的 SID/VoltageSID/CurrentSID/PowerSID/PowerFactorSID 五欄比對）時：

- 即時值欄（V/A/kW/PF/kWh）與指標欄（本日/本月…）**一律進同一個確認清單 modal**（`cmetricFillModal`，樣式複用列範本 modal）：
  逐列顯示「欄名 → 將帶入的點位/指標」＋標籤（即時值/指標/未找到對應點位/已綁定不覆蓋），可帶入項預設勾選、
  可逐項取消客製，按「帶入勾選項目」才套用（「不帶入」則完全不動）；套用後 toast 摘要
- **即時值欄建議來源順序**（v8）：(1) **同設備點位優先** — 在剛綁定點位的同一設備（szDeviceLabel 相同）下以點位名
  比對角色（別名走 `_roleAliases` + 表頭別名表；支援前綴式命名 PM-1-KWH 取末段），列＝設備的心智模型優先；
  (2) 找不到才退回**迴路五 SID 欄**（迴路綁定/虛擬迴路情境的唯一來源）。兩者皆無 → 該欄標「未找到對應點位」
- 只填空白 cell（未綁 SID 也未綁迴路指標）；已綁定 cell 顯示於清單但鎖定不可勾（**不覆蓋**）
- **回落規則（保留固定樣板，v8）**：表頭**任一欄命中**（即時值或指標皆算）即由本流程接手；
  綁一般點位且表頭**完全無命中**時才回落既有「列範本」建議流程（plan 2026-06-01）。
  綁**迴路**時一律由本流程接手（列範本需點位名比對，迴路綁定無從回落）
- **列範本落格改「表頭優先」**（v6，回應「沒依第一列名順序排列」）：列範本套用時每個角色先找**表頭命中該角色的空白欄對號入座**
  （左右都找，不限綁定欄右側；表頭正規化與別名沿用 `_roleAliases` + `_normalizeHeaderText`），
  沒命中表頭的角色才照舊自綁定欄右鄰循序遞補，且**不搶表頭命中其他角色的欄**；
  建議面板預覽逐列顯示「帶入欄『表頭名』」讓使用者確認落點，欄位不足亦標示

比對規則：表頭先**正規化**（trim、全形→半形、去尾端括號單位後綴如「電壓(V)」「本月電費（元）」、壓縮空白、小寫）再與別名表**完全相符**。
別名表集中於 `picker.js` 的 `CIRCUIT_HEADER_ALIASES` 常數，現場要加詞只改一處：

| 帶入 | 表頭別名 |
|------|---------|
| `VoltageSID` 即時值 | V / 電壓 / 電壓V / Voltage / Volt |
| `CurrentSID` 即時值 | A / 電流 / 電流A / Current / Amp / Amps |
| `PowerSID` 即時值 | kW / 功率 / 即時功率 / 瞬時功率 / 實功 / Power |
| `PowerFactorSID` 即時值 | PF / 功因 / 功率因數 / Power Factor / cosφ / cosphi |
| `SID`（kWh 累積讀值）即時值 | kWh / 電度 / 度數 / 累積電度 / 電表讀值 / 累計度數 / Energy |
| 指標 `day_kwh` | 本日度數 / 本日用電 / 今日度數 / 今日用電 / 當日度數 / 當日用電 / 當日累積 / 日用電量 / 本日kWh / 今日kWh / 當日kWh / 日kWh / 本日用電量 / Today kWh / Daily kWh / Day kWh |
| 指標 `month_kwh` | 本月度數 / 本月用電 / 當月度數 / 當月用電 / 當月累積 / 月用電量 / 本月kWh / 當月kWh / 月kWh / 本月用電量 / Month kWh / Monthly kWh |
| 指標 `period_kwh` | 本月電度 / 本期電度 / 本期度數 / 本期用電 / 本期kWh / Period kWh / Billing kWh |
| 指標 `period_cost` | 本月電費 / 本期電費 / 當月電費 / 電費 / 電費金額 / Cost / Period Cost / Electricity Cost |

自訂表頭不命中就不自動帶（仍可手動逐 cell 綁定）；迴路未設定某欄 SID（如無 PF 點）時該欄略過。

**預設底色樣式（2026-07-04）**：Designer 屬性面板提供 5 套一鍵樣式色塊 —
經典深灰 / SCADA 藍 / EMS 綠 / 深色 / 極簡白（定義於 `widget-defs.js` 的 `TABLE_STYLE_PRESETS`）。

- **一次性填色**：點選 preset 是把顏色**複製**進 props（表頭色、奇/偶列底色、框線色，並**整組覆蓋**所有儲存格字色），不儲存 preset key，套用後仍可用「表頭底色 / 奇數列底色 / 偶數列底色 / 框線色」欄位個別微調
- **向下相容**：三個新 props 預設 `null` — 舊存檔與未套樣式的新表格完全維持現行外觀（Designer 淡灰斑馬紋、執行期白底），不會因本功能改變既有場域

#### (9) text — 靜態文字標籤

純文字顯示，可設定字型、大小、顏色、粗體、斜體、背景色，不綁定任何數據。

#### (10) 馬達型設備 — 冷卻水塔 / 空調箱風扇 / 冰機（coolingTower / ahuFan / chiller）

仿水泵（pump）的「馬達型設備」家族，**依設備差異客製**綁定與控制能力。三者的 SVG 圖形（本體 + 主數值條）由 **`wwwroot/js/common/motor-equip-svg.js`（`window.MotorEquip.build`）統一產生，供 Designer 預覽與 ScadaPage 執行期共用同一份圖**（避免兩處走樣）。此模組由 Designer 與 ScadaPage 兩頁的 View 各自 `<script>` 引入。

**各設備綁定集**：

| 設備 | SID 監控 | CID 控制 | 主數值條 | 旋轉 |
|------|----------|----------|----------|------|
| 冷卻水塔 `coolingTower` | 運轉 / 故障 / 手自動 / **風扇頻率** / **出水溫**(僅 tooltip) | 啟停 / 頻率設定 / 自動 | 頻率（VFD，可拖曳） | 頂部風扇 |
| 空調箱風扇 `ahuFan` | 運轉 / 故障 / 手自動 / **頻率** | 啟停 / 頻率設定 / 自動 | 頻率（VFD，可拖曳） | 中央風扇 |
| 冰機 `chiller` | 運轉 / 故障 / **遠端/現場**(=szSidMode) / **負載%** / **冰水出水溫**(僅 tooltip) | 啟停 / **冰水設定溫度** / 自動 | 負載%（唯讀，**底部水平橫條**） | 無（狀態以機身雙筒顏色表示） |

props 命名沿用 pump 慣例（`szSidXxx` + `szXxxName` 成對）：共同 `szSidRun/szSidFault/szSidMode/szCidStartStop`；VFD 型另有 `szSidFreq/szCidFreqSet/nFreqSetMin/Max/nFreqMax`，冷卻水塔加 `szSidWaterTemp`；冰機為 `szSidLoad/szSidChwOut/szCidSetTemp/nLoadMax`。外觀色與 pump 相同（`szRunColor/szStopColor/szFaultColor/szManualColor/szAutoColor/szBgColor`），三者**不提供出口方向**。屬性面板每條綁定列已綁時有「重選」＋「✕ 取消綁定」（清空 SID＋名稱）。

> **冷卻水塔圖形**為**等軸測方箱塔體**（2026-07 依使用者參考圖重繪）：左面浪板紋、右面 X 型斜撐面板、頂部黃色雙層安全欄杆＋小馬達、上置圓形風扇（6 葉）。葉輪畫在壓扁前座標、外層再套 `scale(1,0.5)` 等軸壓扁，旋轉沿用 `pump-spin`（transform-origin 落在內層群組，旋轉在局部座標系進行故呈橢圓轉動）。葉輪盤面＝狀態色、箱體＝手自動模式色，語意與其他馬達型設備一致；Designer 元件庫縮圖（Index.cshtml inline SVG）為同構簡化版。

> **冰機圖形**為**水冷式雙筒身前視圖**（2026-07 依現場照片重繪，Artifact 預覽 v4 定稿）：上筒＝冷凝器、下筒＝蒸發器；**不畫接管**（2026-07-23 拿掉原本左右四支法蘭接管——管路 pipe 元件由使用者自由決定對接位置）；後方帶螺旋壓縮機輪廓與殼間連通管，左前控制箱含固定深色 HMI 螢幕（不變色）、指示燈、壓力錶。**顏色職責**與其他馬達型設備不同：**機身雙筒＝狀態色**（運轉綠/停機灰/故障紅，頂部小警示燈同色）；`szSidMode` 對冰機語意為**遠端/現場**（1=遠端→控制箱面板灰、0=現場→面板深黃 `szManualColor`，預設 `#c79100`，屬性面板標籤「現場面板顏色」，自動色不適用）。負載% 為**底部水平橫條**（軌道 x10 寬66、% 文字置中條內，`viewBox 0 0 120 124`；無條時 `0 0 120 110`），右下角為設定溫度覆蓋層——**設定溫度切手動時值前顯示小 M**（吃 `szCidSetTemp` 的 isAuto，掛 `scada-mode-badge` class 由既有 `_toggleModeBadge` 輪詢驅動），與右上角「開關手動 M」（`szCidStartStop`）各自獨立。Designer 元件庫縮圖為同構簡化版。

**執行期行為**（`scadapage.js`，class = `.scada-motor`，`dataset.motorType` 區分）：
- 狀態判定同 pump：故障 > 運轉 > 停止，運轉時風扇 `pump-spin` 旋轉（冰機不轉）
- 手動模式顯示 M 角標（`szCidStartStop`＝開關手動，右上；VFD 型另有 `szCidFreqSet`；冰機另有**設定溫度手動 M**顯示於右下設定值旁，吃 `szCidSetTemp` 的 isAuto）
- **右鍵選單**：啟動停止（子選單）+ VFD 頻率設定（子選單）/ 冰機設定溫度（項目）+ 監控點位趨勢圖
- **VFD 頻率條拖曳**：與 pump 共用同一 mousedown 處理（選擇器擴充為 `.scada-pump, .scada-motor`，重用 `pump-gauge-*` class）
- **冰機設定溫度**：**無上下限**，於元件**右下角常駐顯示**目前設定值，**雙擊即可編輯**（prompt 輸入 → 寫入 `szCidSetTemp`，`actionType='ao_manual'`）；顯示值取自 AO 手動值快取
- 斷線（Bad quality）**圖形照畫**（停止灰）並於元件上方置中顯示紅字「斷線」，hover 仍浮出「標題 — 斷線」（2026-07-23 改版，原本整塊換成純文字）；hover 平時顯示「標題 — 狀態 — 模式 — 主數值 — 額外溫度」

> **控制 EventLog**：啟停 / 頻率 / 自動沿用 pump 的 `pump_start_stop` / `pump_freq` / `pump_auto` actionType，訊息帶設備名稱（`displayName`）以資區別（例「啟動泵浦「1F 冷卻水塔」」）；冰機設定溫度走 `ao_manual`。此為刻意選擇的零後端改動方案。

> **設計決策**：採「抽共用核心」策略——三設備共用一份 `MotorEquip` 圖形模組與一套執行期邏輯（右鍵 / 拖曳 / 輪詢），並重用 pump 既有 helper（`_buildModeBadgeHtml` / `_pumpStartStop` 等）。水泵本身未遷移以避免生產回歸風險。詳見 `docs/plans/_archive/2026-07-07-designer-冰機冷卻水塔空調箱風扇元件.md`。

#### (11) pipe — 管路流動元件（正交折線）

**正交折線（orthogonal polyline）管路**（2026-07 改版）：一條管由一串節點（`arrPoints`）組成，每段仍為水平或垂直（不變量：任兩相鄰節點共 x 或共 y），可自由拉出 L 型／ㄇ 型／多轉角走線 — **一條折線管＝一個元件＝一份綁定**，取代舊「多段直管拼接」做法（轉角流動不連續、綁定分散）。

**資料模型**：`props.arrPoints = [{x,y}, ...]`（≥2 點，座標相對 widget 左上角 px）。widget 外框（left/top/width/height）＝節點 bounding box ＋ 線寬留白（`PipeSvg.padOf` = ⌈粗細/2⌉+2），節點編輯後自動重算。`szOrient` 保留為相容欄位不再寫入。

**舊存檔相容**：載入時無 `arrPoints` → 由 `szOrient` ＋ widget 寬高推導 2 節點直管（h：左中→右中；v：上中→下中，端點內縮粗細/2 使 round cap 與舊版圓角端等視覺）。ScadaPage 執行期推導**不回寫**；Designer 端 `preparePipeWidget` 於渲染時轉為新格式，重存即落 `arrPoints`。

**流動動畫技術**：SVG 同一條 `<path>` 疊三層 — 底層 track（停止/斷線色實線）、上層 flow（流動色 `stroke-dasharray: 12 10` ＋ `stroke-dashoffset` keyframes `pipe-dash`）、頂層 hit（加寬透明 stroke，唯一 `pointer-events` 命中區）。`stroke-linejoin/linecap: round` 使轉角自然圓滑、dash 沿整條路徑**連續過彎**（瀏覽器原生處理，任意段數零額外成本）。流向以 `animation-direction:reverse` 切換，流速檔 1-5 映射動畫時長（`PipeSvg.SPEED_DUR`）。viewBox 與 widget 尺寸 1:1（大小由節點決定、無非等比縮放，舊版「CSS gradient 防縮放扭曲」的顧慮不復存在）。

**Designer 節點編輯**（widget-core.js）：

- 選取管路 → 顯示節點手把（`.pipe-node`；端點實心、中間節點空心）
- **拖曳節點**：自由拖（貼齊 10px 格），即時正交修正 — 只動直接鄰點：鄰點是端點→沿共用段原方向跟隨（原水平繼承 y、原垂直繼承 x）；鄰點是中間節點→修正在不破壞其外側線段的軸上（外側原水平→改鄰點 x、原垂直→改鄰點 y），任何案例（含連續共線節點）修正後全鏈仍正交、無需連鎖擴散。放開時去除拖到重合的重複節點
- **雙擊管身**：於最近線段中點插入節點（插入當下共線，拖開即折彎）
- **右鍵節點**：刪除；刪後前後兩點不共軸→自動補一個轉角點維持正交；少於 2 點禁刪
- 右下角 **resize 鈕停用**（隱藏＋`startResize`/`setSize` 短路），大小完全由節點決定，屬性面板寬高欄唯讀；整體拖移（管身 mousedown）照舊
- 命中只認管身加寬透明 stroke（容器 `pointer-events:none`）— bounding box 空白區不會擋到底下元件

**綁定（二擇一互斥）** — `szBindMode` 為單一真相（`''` 未綁 / `'di'` / `'analog'`），DI 與「類比量＋閾值」共用同一 `szSid` 欄，任一時刻僅一種生效：

| 模式 | 流動判定 |
|------|----------|
| DI (`di`) | 即時值為 ON（1 / true / ≥1）→ 流動；OFF → 靜止 |
| 類比 (`analog`) | 即時值 `> 閾值`（或 `≥`，由 `szCompare` 決定）→ 流動；否則靜止 |

於屬性面板點另一種綁定並完成選點時，若已綁另一模式且有值 → **跳 confirm**：確認則清除原綁定改綁新的、取消則完全不動（互斥收斂在 `picker.js` 的 `confirmPointPick` pipe 分支）。首次進入類比模式以點位中間值作為預設閾值。

props：`szBindMode / szSid / szPointName / fThreshold / szCompare('gt'|'gte') / arrPoints([{x,y}...]) / szOrient('h'|'v'，相容欄位) / nThickness / szFlowColor / szStopColor / szBadColor / nSpeed(1-5) / szDir('fwd'|'rev') / szBgColor`。

- 拖入畫布**直接建立**（不先開 picker，預設 2 節點水平直管），綁定於屬性面板完成
- 斷線（Bad quality）→ 以 `szBadColor` 靜止顯示、不流動，tooltip 顯示「斷線」
- 未綁定 → 純裝飾，固定流動
- hover 管身顯示「標題 — 狀態（流動 / 靜止 / 斷線）— 數值(類比)」tooltip；右鍵管身開趨勢圖照舊
- 圖形共用 `wwwroot/js/common/pipe-svg.js`（`window.PipeSvg.build`）：Designer `buildPipeHtml`（widget-defs.js）與 ScadaPage `buildPipeViewHtml`（scadapage.js）皆為薄包裝，同 `motor-equip-svg.js` 單一真相模式；樣式 `.pipe-svg / .pipe-svg-track / .pipe-svg-flow / .pipe-svg-hit`（舊 `.pipe-h / .pipe-v` CSS 保留一版防未升級快取頁面）

> **元件庫分類（Designer）**：元件庫改為三類 — 顯示元件（表格 / 儀錶板 / 文字）、點位與控制（控制按鈕 / AI / DI / AO / DO）、設備與動畫（水泵 / 管路 / 冷卻水塔 / 空調箱風扇 / 冰機）。各類獨立捲動（`.widget-cat-items` overflow-y:auto），`.designer-outer` 釘視窗高使面板本身不捲，避免 100% 時多餘的整體捲軸。

---

## 8. 即時數據更新機制

### 輪詢策略
- **頻率**：每 1 秒呼叫 `fetchAndUpdateGauges()`
- **API**：`GET /api/realtime/latest`
- **快取**：更新後的資料存入 `lastData` 陣列，供頁面切換時立即套用
- **累積量元件另走慢輪詢**：每 30 秒 `fetchAndUpdateAccumulations()` → `POST /api/scadapage/accumulation`（換頁時於 `renderScadaCanvas` 立即補打一次，不等下一輪）；頁面上無累積元件時不發請求
- **迴路指標元件另走慢輪詢**：每 30 秒 `fetchAndUpdateCircuitMetrics()` → `POST /api/scadapage/circuit-metric`（同樣換頁立即補打；以 circuitId+metric 去重、超過 50 筆分批）；頁面上無迴路指標元件時不發請求

### Widget 更新邏輯 (`updateScadaWidgets`)

```
建立三個索引 Map：
    sidMap         → { SID: 數值(float) }（有效數值）
    sidValueMap    → { SID: 原始值 }（含 '--'）
    sidQualityMap  → { SID: 品質(大寫) }

依序更新各類 Widget：
    ├─ .scada-gauge        → 重建 SVG（含警報色判定）
    ├─ .scada-rt-value     → 重建 HTML（含警報色判定）
    ├─ .scada-di-point     → 重建 HTML（含 DI 警報脈動）
    ├─ .scada-pump         → 比對 stateKey 決定是否重建 SVG / 僅更新 Gauge
    ├─ .scada-pipe         → 依 bindMode（DI ON/OFF｜類比 vs 閾值｜Bad）比對 pipeKey 決定是否重建
    └─ .scada-table td[data-sid] → 逐格更新文字與色彩
```

### 品質處理
- `BAD`：Gauge 顯示「斷線」、RT Value 顯示紅色「斷線」、DI/Pump 顯示紅色「斷線」
- 正常品質：依警報規則決定是否變色

### 警報著色邏輯
- 初始化時載入 `/api/alarm-rules`，建構 `_alarmRuleMap[SID]`
- AI 點位：高限觸發 → `szHighColor` (紅)、低限觸發 → `szLowColor` (橘)
- DI 點位：觸發條件 (ON/OFF) 匹配時 → `szAlarmColor` (紅) + 脈動動畫
- Deadband 計算：`fVal >= (dAlarmHighValue - dDeadbandHigh)` 或 `fVal <= (dAlarmLowValue + dDeadbandLow)`

---

## 9. 控制指令流程

### 共通確認機制
所有控制操作（controlBtn 除外的「自動」模式）都會先彈出 `confirm()` 對話框確認。

### 回饋機制
- 成功：右下角 Toast 提示（綠色，3 秒後消失）
- 失敗：`alert()` 顯示錯誤訊息
- MQTT 未連線：回傳 HTTP 503

### 手動控制值快取
- 初始化時載入 `_aoManualValueMap`
- 每次手動/自動控制成功後立即更新快取
- 右鍵選單中以藍色外框高亮當前模式
- **跨分頁同步**：`/api/realtime/latest` 每秒 polling 回傳的 `isAuto` 欄位會反向更新 `_aoManualValueMap`，所以別人在別處切手動 / 切自動時，本分頁在下一秒內看到狀態變化

### 手動模式 M 角標（控制元件視覺指示）

控制元件（AO / DO / Pump）在「手動模式」時右上角顯示橘色圓形 `M` badge，用以一眼分辨「目前由人工強制控制中」與「自動 / LogicFlow 控制中」。

- **資料來源**：`ManualControlValue` 表的 `IsAuto` 欄位（false = 手動模式 → 顯示 badge）
- **同步路徑**：
  1. **Singleton 快取**：`MqttRealtimeSubscriberService._manualAutoMap`（`RefreshManualAutoMapAsync()` 在啟動時 + 每次 `/api/realtime/latest` 呼叫時刷新）
  2. **API 投影**：`GetLatestData()` 回傳的每筆 `data[]` 元素加 `isAuto: true | false | null`（null = 該點位無 `ManualControlValue` 紀錄）
  3. **前端 toggle**：`updateScadaWidgets` 從 polling 回傳建 `sidIsAutoMap`，掃 `.scada-mode-badge[data-cid]` 統一 toggle 顯示
  4. **Optimistic 即時反饋**：8 個寫入路徑（AO 手動/自動、DO 手動/自動、Pump 啟停/頻率/變頻拖拽/切自動）在 `_aoManualValueMap` 更新後同步呼叫 `_toggleModeBadge(szCid, isAuto)`，操作者不必等 polling
  5. **寫入路徑通知**：`ControlController` 寫完 DB 後呼叫 `_mqttService.UpdateManualAutoFlag()` 同步 Singleton 快取，避免 1 秒內其他分頁讀到舊狀態
- **Pump 雙 badge**：
  - 有頻率 gauge（`bHasFreq=true`）→ 泵本體（`szCidStartStop`）badge 在左上、變頻器（`szCidFreqSet`）badge 在右上
  - 無頻率 gauge → 泵本體 badge 在右上（單一）
- **PLC mode 顏色保留**：Pump 既有 `szSidMode` 來自 PLC 的「自動 / 手動」訊號（運維人員在現場面板切）控制泵身顏色（橘黃 = 手動、藍 = 自動），與 M badge 是兩個獨立語意層，互不覆蓋
- **i18n**：badge 字面 `M` 中英通用不走 i18n；hover tooltip 走 `scadapage.badge.manual_mode_tooltip`（zh-TW「手動模式」/ en「Manual mode」）
- **樣式**：`.scada-mode-badge` 定義於 `wwwroot/css/scadapage.css`（14×14 圓形、`#f59e0b` 橘、`top:-4px; right:-4px`，預設 `display:none`，由 JS 控制）

### 控制行為審計（EventLog）

所有人為觸發的控制動作（按鈕、AO 手動寫值、AO 切自動、DO ON/OFF、DO 切自動、泵浦啟停、頻率設定、泵浦切自動）在 **MQTT 發送成功**（或自動模式 DB 標記成功）後會寫入一筆 `EventLog`：

| 欄位 | 內容 |
|------|------|
| `EventType` | `3`（資訊） |
| `Severity` | `4`（無） — 專為審計性紀錄新增的一級，視覺以淡灰 badge 區分 |
| `SID` | 控制點 CID |
| `MessageKey` | `control.action.*`（10 種，由 `ControlActionType` 決定） |
| `MessageArgs` | JSON `{ username, name, value? }` |
| `Message` | 寫入時 culture 預先格式化的字串（fallback） |

顯示時 EventLog 頁面透過 `AlarmMessageLocalizer` 依使用者當下 culture 翻譯。前端 `scadapage.js` 9 處 `/api/control/write` 呼叫各自帶 `actionType` 與 `displayName`（取 `el.dataset.szDisplayName || szTitle`），後端 `ControlController` 解析後委派 `ControlEventLogger.LogAsync` 寫入。

**不會寫入的情境**：
- MQTT 發送失敗（503）
- 自動模式 DB 標記失敗
- `mode=logicflow`（Engine/LogicFlow 自動觸發，非人類操作）
- 前端未帶 `actionType`（相容舊版前端）

**使用者識別**：取 `User.Identity?.Name`（cookie 失效不會走到這 — `[Authorize]` 已擋下）；空值 fallback `"anonymous"` 防呆。

**EventLog 頁面顯示**：因控制動作為一次性操作、無「恢復」「確認」概念，`EventType=3` 的紀錄在 EventLog 頁面的「恢復時間 / 確認狀態 / 確認者」三欄統一顯示 `-`（`eventlog.js` `renderTable` / `exportCSV` 以 `eventType === 3` 為判定基準，未來其他 Info 來源也自動套用）。

**已知限制**：每次控制都會寫一筆，工地高頻操作可能撐大 `EventLog` 表，目前無自動清檔策略。

---

## 10. 權限控制

### 檢視權限 (`canView`)
- 控制頁面樹中哪些頁面可見
- `initScadaViewer()` 中 `filterByPerm()` 遞迴過濾不可見頁面

### 操控權限 (`canControl`)
- 控制 Widget 是否可下達指令
- controlBtn：有權限才綁定 click 事件
- AO/DO/Pump：有權限才綁定 contextmenu 事件
- 無權限時 Widget 僅顯示、不可操作

### Admin 特權
- `window._isAdmin = true` 時略過所有權限檢查

---

## 11. 趨勢圖整合

各 Widget 的右鍵選單皆包含「趨勢圖」選項，點擊後：

1. 查找點位資訊（名稱、單位）
2. 寫入 `localStorage['SCADA_TREND_PRELOAD']`
3. 格式：`{ sids: [{ sid, name, unit }, ...] }`
4. 使用者手動切換到「歷史趨勢」頁面即可查看
5. 重複加入時 Toast 提示「選取的點位已在趨勢圖清單中」

---

## 12. 畫布等比縮放

`_applyCanvasScale()` 函式：
1. 計算可用空間（父容器寬高 - 24px 邊距）
2. 取得畫布設計尺寸（`nCanvasW × nCanvasH`）
3. 計算縮放比例：`Math.min(可用寬 / 畫布寬, 可用高 / 畫布高)`
4. 使用 CSS `zoom` 屬性等比縮放
5. 設定 `overflow: hidden` 防止溢出
6. 監聽 `window.resize` 自動重算

---

## 13. UI 佈局

```
┌─────────────────────────────────────────────┐
│  _Layout.cshtml（Navbar）                     │
├──────────┬──────────────────────────────────┤
│ 頁面樹    │  畫布區                            │
│ (160px)  │  ┌──────────────────────────────┐ │
│          │  │  scadaCanvas                  │ │
│ ◆ 首頁   │  │  (等比縮放顯示)                │ │
│  ├ 子頁1  │  │                               │ │
│  └ 子頁2  │  │  [gauge] [rtValue] [table]   │ │
│ ◆ 系統   │  │  [pump]  [diPoint] [ctrlBtn] │ │
│          │  │  [aoPoint] [doPoint] [text]   │ │
│          │  │                               │ │
│          │  └──────────────────────────────┘ │
├──────────┴──────────────────────────────────┤
│  _Layout.cshtml（Footer）                     │
└─────────────────────────────────────────────┘
```

---

## 14. CSS 樣式 (scadapage.css)

### Hover 標籤
所有 Widget 的 `.scada-hover-label` 預設 `display:none`，hover 時 `display:block`。

### 動畫

| 動畫名稱 | 效果 | 用途 |
|----------|------|------|
| `di-alarm-pulse` | 1s 週期透明度脈動 | DI 警報觸發 |
| `pump-spin` | 1.5s 連續旋轉 | 水泵運轉中 |

### 控制按鈕 3D 效果 (`.ctrl-btn-exec`)
- 漸層背景 + 多層陰影模擬立體感
- Hover：`translateY(-1px)` 上浮
- Active：`translateY(1px)` + inset 陰影下壓

### AO/DO 浮動按鈕
- `.ao-point-label-btn` / `.do-point-label-btn`
- 相同的 hover 上浮 / active 下壓效果

---

## 15. 右鍵選單系統

### 共通設計
- 固定定位 (`position:fixed`)，`z-index:99999`
- 白色背景 + 陰影 + 圓角
- 智慧定位（`_positionContextMenu`）：超出視窗邊界時自動翻轉
- 點擊外部區域自動關閉
- 開啟新選單前先關閉所有舊選單（`_removeAllContextMenus`）

### 選單類型

| 選單 | 觸發 Widget | 選項 |
|------|------------|------|
| 趨勢圖選單 | gauge, rtValue, diPoint, table cell | 加入趨勢圖清單 |
| AO 選單 | aoPoint | 手動控制（輸入框）、自動控制 |
| DO 選單 | doPoint | ON、OFF、自動控制 |
| Pump 選單 | pump | 啟停（二級子選單）、頻率設定、監控點位勾選 + 趨勢圖 |

---

## 16. 依賴關係

### 後端依賴
| 服務 | 用途 |
|------|------|
| `PermissionService` | 取得使用者角色與頁面權限 |
| `MqttRealtimeSubscriberService` | 發送 MQTT 控制指令 |
| `IDataRepository` | 儲存/載入手動控制值、標記自動控制 |

### 前端依賴
| 來源 | 用途 |
|------|------|
| `Designer` 功能 | 提供已發布設計資料 (`/Designer/Load`) |
| `Realtime` 功能 | 提供即時數據 (`/api/realtime/latest`) |
| `AlarmSetting` 功能 | 提供警報規則 (`/api/alarm-rules`) |
| Bootstrap 5 | Alert 元件（Toast） |
| Font Awesome 6 | 圖示（按鈕、選單、樹節點） |
| localStorage | 趨勢圖預載資料 (`SCADA_TREND_PRELOAD`) |
