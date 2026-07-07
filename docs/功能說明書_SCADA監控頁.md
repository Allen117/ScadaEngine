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
| `arrCells` | 二維陣列，每格可綁定 SID |
| `arrColDecimals` | 各欄小數位數 |

- 每格可綁定 AI 或 DI 點位
- AI：數值顯示 + 警報變色
- DI：ON/OFF 文字 + 警報變色
- 品質 BAD 時顯示「斷線」
- 右鍵可針對單一儲存格加入趨勢圖

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
| 冰機 `chiller` | 運轉 / 故障 / 手自動 / **負載%** / **冰水出水溫**(僅 tooltip) | 啟停 / **冰水設定溫度** / 自動 | 負載%（唯讀，不可拖曳） | 無（狀態以核心面板色表示） |

props 命名沿用 pump 慣例（`szSidXxx` + `szXxxName` 成對）：共同 `szSidRun/szSidFault/szSidMode/szCidStartStop`；VFD 型另有 `szSidFreq/szCidFreqSet/nFreqSetMin/Max/nFreqMax`，冷卻水塔加 `szSidWaterTemp`；冰機為 `szSidLoad/szSidChwOut/szCidSetTemp/nLoadMax`。外觀色與 pump 相同（`szRunColor/szStopColor/szFaultColor/szManualColor/szAutoColor/szBgColor`），三者**不提供出口方向**。

**執行期行為**（`scadapage.js`，class = `.scada-motor`，`dataset.motorType` 區分）：
- 狀態判定同 pump：故障 > 運轉 > 停止，運轉時風扇 `pump-spin` 旋轉（冰機不轉）
- 手動模式顯示 M 角標（`szCidStartStop`；VFD 型另有 `szCidFreqSet`）
- **右鍵選單**：啟動停止（子選單）+ VFD 頻率設定（子選單）/ 冰機設定溫度（項目）+ 監控點位趨勢圖
- **VFD 頻率條拖曳**：與 pump 共用同一 mousedown 處理（選擇器擴充為 `.scada-pump, .scada-motor`，重用 `pump-gauge-*` class）
- **冰機設定溫度**：**無上下限**，於元件**右下角常駐顯示**目前設定值，**雙擊即可編輯**（prompt 輸入 → 寫入 `szCidSetTemp`，`actionType='ao_manual'`）；顯示值取自 AO 手動值快取
- 斷線（Bad quality）顯示「斷線」；hover 顯示「標題 — 狀態 — 模式 — 主數值 — 額外溫度」

> **控制 EventLog**：啟停 / 頻率 / 自動沿用 pump 的 `pump_start_stop` / `pump_freq` / `pump_auto` actionType，訊息帶設備名稱（`displayName`）以資區別（例「啟動泵浦「1F 冷卻水塔」」）；冰機設定溫度走 `ao_manual`。此為刻意選擇的零後端改動方案。

> **設計決策**：採「抽共用核心」策略——三設備共用一份 `MotorEquip` 圖形模組與一套執行期邏輯（右鍵 / 拖曳 / 輪詢），並重用 pump 既有 helper（`_buildModeBadgeHtml` / `_pumpStartStop` 等）。水泵本身未遷移以避免生產回歸風險。詳見 `docs/plans/_archive/2026-07-07-designer-冰機冷卻水塔空調箱風扇元件.md`。

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
