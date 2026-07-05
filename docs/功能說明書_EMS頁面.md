# 功能說明書：EMS 能源管理 Hub 頁面

## 1. 功能概述

`/EMS` 是「能源管理」模組的進入點 Hub 頁，集中以 4 個子頁簽呈現該模組所有功能。

| 子頁 | 路由 |
|------|------|
| 水系統迴路設定 | `/ChilledWaterSystem` |
| 電表/迴路設定 | `/EnergyMeter` |
| 用電報表 | `/EnergyReport` |
| 冷凍噸報表 | `/RefrigerationTonReport` |

點主 navbar 的「能源管理」會直接進入 `/EMS`（原本 dropdown 形式取消）；進入後 navbar 切換為淡綠/白主題、brand 變成「EMS 能源管理」、主選單只剩 4 個子頁簽（其餘隱藏）。

## 2. 路由 & 權限

| 方法 | 路由 | 說明 | 認證 |
|------|------|------|------|
| GET | `/EMS` | EMS Hub 頁 | 需登入 |

權限規則：

- `/EMS` 已加入 `PermissionService.ConfigurablePages`，帳號管理 UI 可勾選。
- 額外規則：使用者**未勾 /EMS** 但**勾了任一個 4 子頁**時，`CanAccessPage("/EMS")` 仍回 true（`PermissionService` 內 `_aEmsChildren` 特例）。這保證使用者一定能從主 navbar 走進子頁。
- 4 個子頁皆無權限的使用者進 `/EMS` 會被 `EmsController.Index` redirect 回 `/ScadaPage`，避免進空殼頁。

## 3. 視覺設計

由 `wwwroot/css/ems.css` 透過 `body.ems-mode` 限定，與其他頁面藍底 navbar 完全分離：

| 元件 | 樣式 |
|------|------|
| body 背景 | `#f1f8f4` |
| navbar 背景 | `linear-gradient(135deg, #e8f5e9 0%, #ffffff 100%)`，底邊框 `#c8e6c9` |
| navbar brand 字色 | `#2e7d32`（粗體） |
| navbar brand icon | `#43a047`（葉子 icon） |
| nav-link 字色 | `#2e7d32`，hover `#1b5e20` |
| nav-link active | 背景 `#66bb6a` 白字，圓角 6px |
| dropdown hover | 背景 `#e8f5e9` |

色票採 Material Design Green 系列，對白/淡綠底對比足夠。

## 4. Layout 模式切換

共用 `Views/Shared/_Layout.cshtml`，靠**路由表自動偵測** + ViewData fallback。`_Layout` 內：

```csharp
bool isEmsMode = ViewData["EmsMode"] as bool? == true
    || PermissionService.IsEmsRoute(Context.Request.Path.Value);
```

`PermissionService.EmsRoutes` 為「EMS 體系」的權威清單：

```csharp
public static readonly string[] EmsRoutes =
[
    "/EMS",
    "/ChilledWaterSystem",
    "/EnergyMeter",
    "/EnergyReport",
    "/RefrigerationTonReport",
];
```

只要 `Context.Request.Path` 命中清單，`_Layout` 就會自動套淡綠主題、改 brand、隱藏其他模組選單。**未來新增 EMS 體系的頁面，只要把路由加進這個陣列即可**，不需要動 Controller 或 Layout。

EmsMode 開關控制五件事：

1. `<body>` 加 `ems-mode` class，吃 `ems.css` 樣式覆蓋（navbar + footer）
2. `<nav>` class 從 `navbar-dark bg-primary` 換成 `navbar-light`
3. brand href 從 `/ScadaPage` 換成 `/EMS`、icon 從 `fa-industry` 換成 `fa-leaf`、字串走 `layout.brand.ems` 而非 `layout.brand`
4. navbar 主選單清單從「ScadaPage/RealTime/控制邏輯/歷史資料/能源管理/系統設定」整個改成「水系統迴路設定/電表迴路設定/用電報表/冷凍噸報表」4 個直接 nav-link
5. navbar 右側「語系」左邊加上「← 回 SCADA」連結（`layout.ems.back_scada`，直連 `/ScadaPage`），讓使用者隨時跳回主模組

footer 在 EmsMode 下背景換成 `linear-gradient(135deg, #66bb6a → #43a047)` 配白字；語系切換 / 使用者選單 / 登出 modal / 版本資訊完全共用。

## 5. 主 navbar 對 /EMS 的入口改造

原本 `_Layout.cshtml` 的「能源管理」是一個 dropdown，內含 4 個 dropdown-item 直連各子頁。本次改造後（決策 2）：

```cshtml
@if (canAccess("/ChilledWaterSystem") || canAccess("/EnergyMeter") || ...)
{
    <li class="nav-item">
        <a class="nav-link" href="/EMS">
            <i class="fas fa-leaf me-1"></i>
            @Localizer["layout.menu.energy"]
        </a>
    </li>
}
```

新流程：主 navbar 能源管理 → `/EMS` → 4 個子頁簽（在 EMS 模式 navbar 上）→ 子頁。單一動線，使用者不會被 dropdown 與 hub 兩種入口混淆。

## 6. i18n

新增 key 在 `Resources/Views.Shared._Layout.{,en}.resx`：

| Key | zh-TW | en |
|-----|-------|----|
| `layout.brand.ems` | EMS 能源管理 | EMS Energy Management |

`/EMS` 頁面 ViewLocalizer 走 `Resources/Features.Ems.Views.Index.{,en}.resx`，目前只有：

| Key | zh-TW | en |
|-----|-------|----|
| `ems.title` | 能源管理 | Energy Management |

主選單的「能源管理」「水系統/電表/用電報表/冷凍噸」字串走原本就有的 `layout.menu.*`，這次不動。

## 7. 主內容區

Hub 主內容為卡片式 dashboard（`container-fluid` + Bootstrap grid，單一 `.row.g-3` 自動換行）：

| 卡片 | 欄寬 (xl) | 說明 |
|------|-----------|------|
| 今日即時需量 | col-xl-4 | 迴路下拉 + 即時 kW + 今日最高需量 + 全日趨勢折線圖，60s 自動刷新（`ems.js`） |
| 主要電表用電長條圖 | col-xl-8 | 主要電表的日/月/年用電長條圖（`ems-hub-energy.js`），詳見 §7.1 |
| 子迴路用電圓餅圖 | col-xl-4 | 主要電表直接子迴路用電占比（`ems-hub-energy.js`），詳見 §7.2 |
| 去年同期比較表 | col-xl-8 | 主要電表 + 直接子迴路本期 vs 去年同期比較（`ems-hub-energy.js`），詳見 §7.3 |

### 7.1 主要電表用電長條圖卡片

- 「主要電表」= `EnergyCircuit.IsMainMeter = 1`（全系統唯一，於 /EnergyMeter 頁勾選）。前端先打 `GET /EMS/api/main-meter` 拿 id，再走既有 `GET /EMS/api/circuit-energy` 取資料 — 與 /CircuitInfo 三張長條圖同一計算核心，數字完全一致
- header 內「日 / 月 / 年」切換鈕（**預設「日」**）+ 對應 pivot 選擇器：日→`<input type="date">`、月→`<input type="month">`、年→年份數字。UI 粒度對應後端 granularity：日→`hour`、月→`day`、年→`month`
- 「日」模式且選的是今天時，每 60 秒自動刷新（比照 /CircuitInfo 日卡片）；其他模式/日期不輪詢

### 7.2 子迴路用電圓餅圖卡片

- `GET /EMS/api/main-meter-breakdown?granularity=&pivot=` — 後端自行解析主要電表（前端不帶 circuitId），回傳各**直接子迴路**在所選期間的用電量（已乘子迴路 Sign）；**無子迴路時回主要電表自己一筆**（圓餅顯示自己 100%）
- 期間總量 = 該粒度所有 bucket 加總（重用 `EnergyReportService` 同一計算核心，staleness window / 溢位規則與長條圖一致）
- **負值處理**：期間總量為負的子迴路（Sign=-1 回饋/發電）不進圓餅，改在卡片下方以小字列出「XX：-N kWh（未列入占比）」；全部 ≤ 0 時顯示「期間內無用電資料」
- tooltip 顯示 `名稱: N kWh (P%)`，圖例置底
- 日/月/年切換與時間選擇器**與比較表共用同一組**（長條圖獨立一組）

### 7.3 去年同期比較表卡片

- `GET /EMS/api/main-meter-yoy?granularity=&pivot=` — 首列為主要電表（★ 標示 + 底色），其後各直接子迴路；欄位：本期 kWh、去年同期 kWh、差異 kWh、增減 %
- 去年同期換算：pivot 起點 `AddYears(-1)`（2/29 天然落到 2/28），終點依同粒度公式重建
- 去年同期為 0 或無資料時增減 % 顯示 `--`；增（▲ 紅）/ 減（▼ 綠）
- 期間與圓餅圖同步，header 右側顯示「本期 vs 去年同期」pivot 字串

### 7.4 未設定主要電表

三張卡片（長條圖 / 圓餅 / 比較表）統一顯示「尚未設定主要電表，請至電表/迴路設定頁勾選」提示並停用切換鈕，不噴錯；既有需量卡片不受影響。

### 7.5 API 一覽（本頁新增）

| 方法 | 路由 | 回應 |
|------|------|------|
| GET | `/EMS/api/main-meter` | `{ hasMainMeter, id, name, hasChildren }` |
| GET | `/EMS/api/main-meter-breakdown?granularity=&pivot=` | `{ hasMainMeter, meterName, items: [{ id, name, kwh }] }` |
| GET | `/EMS/api/main-meter-yoy?granularity=&pivot=` | `{ hasMainMeter, rows: [{ id, name, isMainMeter, currentKwh, lastYearKwh, diffKwh, pctChange }] }` |

granularity / pivot 協定同 `/EMS/api/circuit-energy`（`hour`→`yyyy-MM-dd`、`day`→`yyyy-MM`、`month`→`yyyy`）。DTO 位於 `Features/Ems/Models/EmsMainMeterDtos.cs`；服務層新增 `EnergyCircuitService.GetMainMeterAsync()` 與 `EnergyReportService.GetTotalKwhAsync()`（bucket 加總公開包裝）。

> ⚠️ 已知限制：主要電表綁 SID 且掛有子迴路時，計算核心 `GetLeavesUnderAsync` 會把主錶自身 + 子孫葉子一併加總（/CircuitInfo、/EnergyReport 亦同此行為）。目前線上僅單一主錶無子迴路，尚未觸發；未來在主錶下掛分錶時需另案處理「主錶量測 vs 分錶加總」的語意。

## 8. 檔案位置

```
ScadaEngine.Web/
├── Features/Ems/
│   ├── Controllers/EmsController.cs        ← +main-meter / breakdown / yoy API
│   ├── Models/EmsMainMeterDtos.cs          ← 三支新 API 的回應 DTO
│   └── Views/Index.cshtml                  ← +三張主要電表用電卡片
├── Services/
│   ├── EnergyCircuitService.cs             ← +GetMainMeterAsync()
│   └── EnergyReportService.cs              ← +GetTotalKwhAsync()
├── wwwroot/js/ems-hub-energy.js            ← 三張新卡片邏輯（IIFE）
├── Resources/
│   ├── Features.Ems.Views.Index.resx
│   └── Features.Ems.Views.Index.en.resx
├── Services/PermissionService.cs        ← +/EMS、+_aEmsChildren 規則
├── Views/Shared/_Layout.cshtml          ← +EmsMode 開關、能源管理 dropdown → 直連
├── Resources/
│   ├── Views.Shared._Layout.resx        ← +layout.brand.ems
│   └── Views.Shared._Layout.en.resx     ← +layout.brand.ems
└── wwwroot/css/ems.css                  ← 淡綠/白主題覆蓋
```
