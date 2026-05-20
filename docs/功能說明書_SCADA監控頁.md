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
        ├─ Pump 頻率設定           → POST /api/control/write { cid, value:Hz值 }
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

- 純數值顯示，支援單位標示
- 品質 BAD 時顯示紅色「斷線」
- 結合警報規則變色
- 右鍵可加入趨勢圖清單

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
- **頻率設定**：輸入框 + 確定按鈕
- **監控點位**（勾選框）：可勾選運轉、故障、模式、頻率等 SID 加入趨勢圖

**Gauge 拖拽**：
- 滑鼠在頻率 Gauge 直條上拖拽可即時調整
- mousedown → mousemove 更新視覺 → mouseup 發送控制指令

#### (8) table — 資料表格

| 屬性 | 說明 |
|------|------|
| `nCols` / `nRows` | 欄位數（1~4）/ 列數 |
| `szHeaderColor` | 表頭背景色 |
| `arrCells` | 二維陣列，每格可綁定 SID |
| `arrColDecimals` | 各欄小數位數 |

- 每格可綁定 AI 或 DI 點位
- AI：數值顯示 + 警報變色
- DI：ON/OFF 文字 + 警報變色
- 品質 BAD 時顯示「斷線」
- 右鍵可針對單一儲存格加入趨勢圖

#### (9) text — 靜態文字標籤

純文字顯示，可設定字型、大小、顏色、粗體、斜體、背景色，不綁定任何數據。

---

## 8. 即時數據更新機制

### 輪詢策略
- **頻率**：每 1 秒呼叫 `fetchAndUpdateGauges()`
- **API**：`GET /api/realtime/latest`
- **快取**：更新後的資料存入 `lastData` 陣列，供頁面切換時立即套用

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
