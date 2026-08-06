# CLAUDE.md — ScadaEngine.Web

本檔為 **ScadaEngine.Web 專案專屬規則**，Claude Code 於觸碰本目錄檔案時自動載入，與 root `CLAUDE.md` 併用。
跨專案共用規則（Plan 流程、DB Schema、SID 格式、命名慣例、跨模組設計、Key Config Files）一律在 root，本檔不重複。

## Feature Folder + MVC 架構（必須遵循）

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

## Razor View 前端分離規則

**CSS 和 JavaScript 禁止寫在 `.cshtml` 內**，必須抽離為獨立靜態檔案：

```
wwwroot/
├── css/{feature}.css    ← 該功能的樣式
└── js/{feature}.js      ← 該功能的邏輯（使用 IIFE 封裝，暴露至 window 供 View 呼叫）
```

- `.cshtml` 只保留 HTML/Razor 結構、`<link>` 引用 CSS、`@section Scripts { <script src> }` 引用 JS
- JS 中如有 HTML 實體（`&le;`、`&times;` 等），須轉換為 Unicode 跳脫（`\u2264`、`\u00d7`）
- JS 使用 IIFE `(function(){ ... })();` 封裝，對外介面掛在 `window._xx` 供 `onclick` 等屬性呼叫

## 時間輸入一律 24 小時制 — 用 flatpickr

**新增** datetime / time 控制項**禁止**用原生 `<input type="datetime-local">` / `<input type="time">`（zh-TW Windows Chromium 會強制吃 OS locale 顯示「下午 01:34」）。改用 flatpickr + 共用 helper `window._fpInit`（date-only 純日期可續用原生 `<input type="date">`）。用法、載入順序、setDate 寫值範例見 [docs/設計規範.md](../docs/設計規範.md) §時間輸入。

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

## Key Patterns & Pitfalls（Web 端）

### MQTT JSON Parsing
The Web subscriber uses case-insensitive dictionary parsing to handle PascalCase/camelCase variations in the payload:
```csharp
var props = jsonDoc.RootElement.EnumerateObject()
    .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
```

### MqttRealtimeSubscriberService
Registered as both `AddSingleton` and `AddHostedService` so it can be injected into controllers by type and also run as a background service. Pre-fills cache from `ModbusPoints` table with `hasData=false` placeholders on startup so all configured points appear in the UI even before MQTT data arrives.

## UI 設計系統

改動 `.cshtml` / `.css` / `wwwroot/` 前先讀 [docs/設計規範.md](../docs/設計規範.md)，含框架、元件模式、色彩、字體、間距、圓角、陰影、動畫、Z-Index、圖示慣例。

**SCADA / EMS 雙主題**（新頁面必讀）：全站依路由分兩套色系（SCADA 深藍 / EMS 淡綠）。新增頁面前先判斷歸屬 — **頁面內一律用 Bootstrap primary class（`btn-primary` / `text-primary` / `bg-primary`），不在 `.cshtml` inline 硬寫色號**，EMS 模式由 `ems.css` 自動轉綠。EMS 子頁掛載 4 步驟 SOP + 完整色票 → [docs/設計規範.md](../docs/設計規範.md) §色彩系統 §SCADA / EMS 雙主題。

## i18n 規則（zh-TW + en，僅指定頁面）

僅以下頁面已導入 i18n，新增/修改其字串時：

- **已 i18n 範圍**：ScadaPage、Realtime、EnergyReport、EnergyBaseline、History/Trend、EventLog、AccountSetting、ScheduleSetting、ConditionCtrl、LogicFlow、ModbusCoordinator、DbCoordinator、CalcPoint、WeatherSetting、EmsCardSetting、WaterMeterSetting、WaterUsageReport、WaterTariffSetting、WaterCostReport、WaterBillingPeriodSetting、GasMeterSetting、GasUsageReport、GasTariffSetting、GasCostReport、GasBillingPeriodSetting、共用 `_Layout`
- **.cshtml 字串**：`@Localizer["key"]`（key 命名 `feature.section.purpose` 全小寫底線分）
- **JS 字串**：`window.i18n.t('key', {args})`，IIFE 內可宣告 `function t(key, args) { return window.i18n.t(key, args); }` 簡化
- **Controller / Service / Excel exporter**：建構子注入 `IStringLocalizer<T>`，走 `_l["key"].Value`。Singleton 服務若依賴此須改 Scoped
- **resx 檔位於 `ScadaEngine.Web/Resources/`**：中性 `.resx`（內容＝zh-TW）+ `.en.resx`，搭配 `[assembly: NeutralResourcesLanguage("zh-TW")]`。zh-TW 與 en 必須同步補
- **SCADA 專業詞先查 [`docs/i18n-glossary.md`](../docs/i18n-glossary.md)**，新詞要先加進 glossary 再用

新增功能時，未在 i18n 範圍的頁面字串不需走 IStringLocalizer，但若**新增 Layout 側欄選單** 則該選單字必須走 `Views.Shared._Layout.{,en}.resx`。

**訊息結構化**（警報 Engine 跨 Web + 控制操作 Web 內部）：EventLog / MQTT 除人類可讀 `Message` 外帶 `MessageKey` + `MessageArgs`（JSON），Web 顯示時經 `AlarmMessageLocalizer` 依 culture 翻譯。三類警報固定 key、10 種 `control.action.*` key、新增動作要改的 switch 一覽 → 見 [docs/功能說明書_多語系.md](../docs/功能說明書_多語系.md) §警報訊息結構化 / §控制操作訊息結構化。

DB 內容（點位名、迴路名、警報規則的 DiOnLabel/DiOffLabel）為使用者輸入，**不在 i18n 範圍** — 切英文時若 user input 為中文，這是運維責任而非系統責任。

詳細架構見 [docs/功能說明書_多語系.md](../docs/功能說明書_多語系.md)。
