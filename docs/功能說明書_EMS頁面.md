# 功能說明書：EMS 能源管理 Hub 頁面

## 1. 功能概述

`/EMS` 是「能源管理」模組的進入點 Hub 頁，集中呈現該模組所有子頁功能。

| 子頁 | 路由 |
|------|------|
| 迴路資訊 | `/CircuitInfo` |
| 水系統迴路設定 | `/ChilledWaterSystem` |
| 電表/迴路設定 | `/EnergyMeter` |
| 用電報表 | `/EnergyReport` |
| 電費報表 | `/ElectricityCostReport` |
| 冷凍噸報表 | `/RefrigerationTonReport` |
| 能源申報 | `/EnergyDeclaration` |
| 月結週期設定 | `/BillingPeriodSetting` |

點主 navbar 的「能源管理」會直接進入 `/EMS`（原本 dropdown 形式取消）；進入後 navbar 切換為淡綠/白主題、brand 變成「EMS 能源管理」、主選單只剩 EMS 子頁選單（其餘隱藏），且每個選單項目都以 `canAccess()` 依帳號權限個別過濾。

## 2. 路由 & 權限

| 方法 | 路由 | 說明 | 認證 |
|------|------|------|------|
| GET | `/EMS` | EMS Hub 頁 | 需登入 |

權限規則：

- `/EMS` 與全部 EMS 子頁都已加入 `PermissionService.ConfigurablePages`，帳號管理 UI 可逐頁勾選；Admin 角色不走勾選、預設全部可看（`IsAdmin` 直接放行）。
- 額外規則：使用者**未勾 /EMS** 但**勾了任一個子頁**時，`CanAccessPage("/EMS")` 仍回 true（`CanAccessPage` 內對 `/EMS` 的特例，遍歷 `EmsRoutes`）。這保證使用者一定能從主 navbar 走進子頁。
- 主 navbar「能源管理」入口同樣以 `canAccess("/EMS")` 判斷（等價於「勾了 /EMS 或任一子頁」），與上述特例單一來源，不另列子頁清單。
- 所有子頁皆無權限的使用者進 `/EMS` 會被 `EmsController.Index` redirect 回 `/ScadaPage`，避免進空殼頁。

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
    "/CircuitInfo",
    "/EnergyMeter",
    "/EnergyReport",
    "/RefrigerationTonReport",
    "/EnergyDeclaration",
    "/BillingPeriodSetting",
];
```

只要 `Context.Request.Path` 命中清單，`_Layout` 就會自動套淡綠主題、改 brand、隱藏其他模組選單。**未來新增 EMS 體系的頁面，只要把路由加進這個陣列即可**，不需要動 Controller 或 Layout。

EmsMode 開關控制五件事：

1. `<body>` 加 `ems-mode` class，吃 `ems.css` 樣式覆蓋（navbar + footer）
2. `<nav>` class 從 `navbar-dark bg-primary` 換成 `navbar-light`
3. brand href 從 `/ScadaPage` 換成 `/EMS`、icon 從 `fa-industry` 換成 `fa-leaf`、字串走 `layout.brand.ems` 而非 `layout.brand`
4. navbar 主選單清單從「ScadaPage/RealTime/控制邏輯/歷史資料/能源管理/系統設定」整個改成 EMS 子頁選單（迴路資訊 + 報表/EMS 設定/歷史 dropdown），每項依 `canAccess()` 過濾
5. navbar 右側「語系」左邊加上「← 回 SCADA」連結（`layout.ems.back_scada`，直連 `/ScadaPage`），讓使用者隨時跳回主模組

footer 在 EmsMode 下背景換成 `linear-gradient(135deg, #66bb6a → #43a047)` 配白字；語系切換 / 使用者選單 / 登出 modal / 版本資訊完全共用。

## 5. 主 navbar 對 /EMS 的入口改造

原本 `_Layout.cshtml` 的「能源管理」是一個 dropdown，內含 dropdown-item 直連各子頁。後改為左側單一 nav-link；目前再改為**右側返回連結**，與 EMS 模式的「回 SCADA」完全對稱 — 位置在 navbar 右側「語系」左邊：

```cshtml
else if (!isEmsMode && canAccess("/EMS"))
{
    <li class="nav-item">
        <a class="nav-link" href="/EMS">
            <i class="fas fa-arrow-left me-1"></i>
            @Localizer["layout.scada.back_ems"]
        </a>
    </li>
}
```

（與 `isEmsMode && canAccess("/ScadaPage")` 的「回 SCADA」是同一組 if/else — 兩模式各只顯示自己的返回連結。）

`canAccess("/EMS")` 內含「勾了任一 EMS 子頁即放行」特例，因此新增 EMS 子頁時**不需要**回來改這個判斷式。

新流程：SCADA navbar 右側「← 回 EMS」 → `/EMS` → EMS 模式 navbar 子頁選單 → 子頁。單一動線，使用者不會被 dropdown 與 hub 兩種入口混淆。

## 6. i18n

新增 key 在 `Resources/Views.Shared._Layout.{,en}.resx`：

| Key | zh-TW | en |
|-----|-------|----|
| `layout.brand.ems` | EMS 能源管理 | EMS Energy Management |
| `layout.scada.back_ems` | 回 EMS | Back to EMS |

（原左側選單用的 `layout.menu.energy` 已隨入口右移移除。）

`/EMS` 頁面 ViewLocalizer 走 `Resources/Features.Ems.Views.Index.{,en}.resx`，目前只有：

| Key | zh-TW | en |
|-----|-------|----|
| `ems.title` | 能源管理 | Energy Management |

主選單的「水系統/電表/用電報表/冷凍噸」字串走原本就有的 `layout.menu.*`，這次不動。

## 7. 主內容區

Hub 主內容為卡片式 dashboard（`container-fluid` + Bootstrap grid，單一 `.row.g-3` 自動換行）：

| 卡片 | 欄寬 (xl) | 說明 |
|------|-----------|------|
| 今日即時需量 | col-xl-4 | 迴路下拉 + 即時 kW + 今日最高需量 + 全日趨勢折線圖，60s 自動刷新（`ems.js`）；下拉**預設優先選主要電表**（若主錶本身有設定需量、在選單內），否則退回清單第一筆 |
| 主要電表用電長條圖 | col-xl-8 | 主要電表的日/月/年用電長條圖（`ems-hub-energy.js`），詳見 §7.1 |
| 子迴路用電圓餅圖 | col-xl-4 | 主要電表直接子迴路用電占比（`ems-hub-energy.js`），詳見 §7.2 |
| 去年同期比較表 | col-xl-8 | 主要電表 + 直接子迴路本期 vs 去年同期比較（`ems-hub-energy.js`），詳見 §7.3 |
| 電費狀態 | col-xl-4 | 本期（月結期別）各時段 kWh 與流動電費，依採用方案型態自適應（`ems-hub-cost.js`），詳見 §7.6 |

### 7.1 主要電表用電長條圖卡片

- 「主要電表」= `EnergyCircuit.IsMainMeter = 1`（全系統唯一，於 /EnergyMeter 頁勾選）。前端先打 `GET /EMS/api/main-meter` 拿 id，再走既有 `GET /EMS/api/circuit-energy` 取資料 — 與 /CircuitInfo 三張長條圖同一計算核心，數字完全一致
- header 內「日 / 月 / 年」切換鈕（**預設「日」**）+ 對應 pivot 選擇器：日→`<input type="date">`、月→`<input type="month">`、年→年份數字。UI 粒度對應後端 granularity：日→`hour`、月→`day`、年→`month`
- 「日」模式且選的是今天時，每 60 秒自動刷新（比照 /CircuitInfo 日卡片）；其他模式/日期不輪詢

### 7.2 子迴路用電圓餅圖卡片

- `GET /EMS/api/main-meter-breakdown?granularity=&pivot=` — 後端自行解析主要電表（前端不帶 circuitId），回傳各**直接子迴路**在所選期間的用電量（已乘子迴路 Sign）；**無子迴路時回主要電表自己一筆**（圓餅顯示自己 100%）
- 期間總量 = 該粒度所有 bucket 加總（重用 `EnergyReportService` 同一計算核心，staleness window / 溢位規則與長條圖一致）
- **負值處理**：期間總量為負的子迴路（Sign=-1 回饋/發電）不進圓餅，改在卡片下方以小字列出「XX：-N kWh（未列入占比）」；全部 ≤ 0 時顯示「期間內無用電資料」
- tooltip 顯示 `名稱: N kWh (P%)`，圖例置底
- 色盤 `PIE_COLORS` 首色維持 EMS 綠（識別度），其餘**跨全色相分佈**（藍/橙/紫/紅/青/黃/棕/粉…）以利分辨相鄰扇形
- 日/月/年切換與時間選擇器**與比較表共用同一組**（長條圖獨立一組）

### 7.3 去年同期比較表卡片

- `GET /EMS/api/main-meter-yoy?granularity=&pivot=` — 首列為主要電表（★ 標示 + 底色），其後各直接子迴路；欄位：本期 kWh、去年同期 kWh、差異 kWh、增減 %
- 去年同期換算：後端重建去年 pivot 字串（年 −1，hour 粒度 2/29 → 2/28）再走同一期別解析 — 月/日粒度會取**去年**的期別設定
- 去年同期為 0 或無資料時增減 % 顯示 `--`；增（▲ 紅）/ 減（▼ 綠）
- 期間與圓餅圖同步，header 右側顯示「本期 vs 去年同期」pivot 字串
- 列數多時 `#yoyTableWrap` 自帶捲軸（`max-height:250px; overflow-y:auto`，表頭 sticky）— 避免撐高整個 EMS 頁產生外層捲軸

### 7.4 未設定主要電表

三張卡片（長條圖 / 圓餅 / 比較表）統一顯示「尚未設定主要電表，請至電表/迴路設定頁勾選」提示並停用切換鈕，不噴錯；既有需量卡片不受影響。

### 7.5 API 一覽（本頁新增）

| 方法 | 路由 | 回應 |
|------|------|------|
| GET | `/EMS/api/main-meter` | `{ hasMainMeter, id, name, hasChildren }` |
| GET | `/EMS/api/main-meter-info` | `{ hasMainMeter, mode, name, voltage, current, power, powerFactor }` — `mode='realtime-by-sid'`（實體主表：`voltage={sid,pointName,unit}`）或 `mode='aggregated'`（虛擬主表：`voltage={unit}`）。點位未設定 `unit` 時前端 `MM_DEFAULT_UNITS` 補預設（V/A/kW，PF 無因次不顯示）供每個數值顯示單位 |
| GET | `/EMS/api/main-meter-values` | `{ voltage, current, power, powerFactor }` 皆 `number\|null` — 僅虛擬主表用；由 `MainMeterAggregationService.ComputeAsync` 現算 |
| GET | `/EMS/api/main-meter-breakdown?granularity=&pivot=` | `{ hasMainMeter, meterName, items: [{ id, name, kwh }] }` |
| GET | `/EMS/api/main-meter-yoy?granularity=&pivot=` | `{ hasMainMeter, rows: [{ id, name, isMainMeter, currentKwh, lastYearKwh, diffKwh, pctChange }] }` |
| GET | `/EMS/api/electricity-cost?circuitId=` | `EmsElectricityCostDto`（`{ hasPlan, hasCircuit, planId, planType, totalKwh, totalCost, periods[], progressive, flat, surcharge, ... }`）— 電費狀態卡，見 §7.6 |

granularity / pivot 協定同 `/EMS/api/circuit-energy`（`hour`→`yyyy-MM-dd`、`day`→`yyyy-MM`、`month`→`yyyy`）。DTO 位於 `Features/Ems/Models/EmsMainMeterDtos.cs`；服務層新增 `EnergyCircuitService.GetMainMeterAsync()` 與 `EnergyReportService.GetTotalKwhAsync()`（bucket 加總公開包裝）。

**pivot 展開套用月結期別**（`EmsController.ParsePivotAsync`，見 [功能說明書_能源管理.md](功能說明書_能源管理.md) §12）：

- `month`（年檢視）：pivot = 年份 → 該年 **1～12 月期別** 12 根柱（期別非自然月時柱標籤顯示完整期間）
- `day`（月檢視）：pivot = YYYY-MM → 該**期別**實際起訖日逐日展開（非 1 號～月底）；日圖加總 = 年檢視該期柱值
- `hour`（日檢視）：pivot = YYYY-MM-DD → 該自然日，不受期別影響

> ⚠️ 已知限制：主要電表綁 SID 且掛有子迴路時，計算核心 `GetLeavesUnderAsync` 會把主錶自身 + 子孫葉子一併加總（/CircuitInfo、/EnergyReport 亦同此行為）。目前線上僅單一主錶無子迴路，尚未觸發；未來在主錶下掛分錶時需另案處理「主錶量測 vs 分錶加總」的語意。

### 7.6 電費狀態卡（2026-07 新增）

- `GET /EMS/api/electricity-cost?circuitId=`（未帶 circuitId = 主要電表；未設主要電表退回第一個根節點），60s 輪詢（`ems-hub-cost.js`）
- 「本期」= `BillingPeriodService.GetCurrentPeriodAsync(今天)` 的月結期別；資料源 `ElectricityCostHourly`（Web 背景服務逐時計價，見 [功能說明書_電費設定.md](功能說明書_電費設定.md) §電費計算）
- 依採用方案型態自適應：`tou` 各時段（尖峰/半尖峰/離峰）kWh + 電費明細表（含簡易型超額加價列）；`progressive` 累計 kWh + 落點級距；`flat` 累計 kWh + 當季單價
- 迴路下拉（`/EMS/api/circuit-tree` 組樹縮排）可切子迴路 — kWh/電費依葉子 × EffectiveSign 加總；**級距/加價金額在子迴路為 kWh 占比分攤估算**（顯示「估算」badge + 底部註記）
- 固定註記「不含基本電費」；底部顯示「資料計算至 yyyy-MM-dd HH:00」
- 未選採用方案 → 顯示「尚未選擇電費方案」+ /TariffSetting 連結；未建迴路 → 引導至 /EnergyMeter

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
├── wwwroot/js/ems-hub-cost.js              ← 電費狀態卡（IIFE；DTO：Models/EmsElectricityCostDtos.cs）
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
