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
5. 實作完成後先停下，等使用者驗證
6. 使用者明確說「沒問題」「OK」「可以」「過了」之後，自動執行 `git add` 相關檔案 → `git commit`（訊息走專案風格）→ `git push`，再回填 commit hash 並把 plan 搬到 `docs/plans/_archive/`
   - ✅ 授權 scope 僅限「plan 流程收尾 + 使用者明確確認」，不代表其他情境也預先授權 push
   - ⚠️ 若 pre-commit hook 失敗，修好後建**新** commit，禁止 `--no-verify`
   - ⚠️ 若 push 被 reject（非 fast-forward 等），停下回報，不可 force push

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

### 時間輸入一律 24 小時制 — 用 flatpickr

**新增** datetime / time 控制項**禁止**用原生 `<input type="datetime-local">` / `<input type="time">`（zh-TW Windows Chromium 會強制吃 OS locale 顯示「下午 01:34」）。改用 flatpickr + 共用 helper `window._fpInit`（date-only 純日期可續用原生 `<input type="date">`）。用法、載入順序、setDate 寫值範例見 [docs/設計規範.md](docs/設計規範.md) §時間輸入。

## Build & Run

兩個專案各自 `dotnet run`，Web 跑在 **5038** (HTTP) / **7189** (HTTPS)。
Engine 是背景服務，無 HTTP endpoint。

> ⚠️ Razor views 是 **precompiled**，改 .cshtml 必須 `dotnet build` 才生效。

卡住的 Web 進程：`Get-Process -Name 'ScadaEngine.Web' | Stop-Process -Force`

---

## Solution Architecture

```
ScadaEngine.sln
├── ScadaEngine.Common        — Shared models & DB config service (class library)
├── ScadaEngine.Algorithm     — Algorithm utilities (class library, currently minimal)
├── ScadaEngine.Engine        — .NET 8 Worker Service (Modbus → MQTT publisher)
├── ScadaEngine.Web           — .NET 8 ASP.NET Core MVC (dashboard, http://localhost:5038)
└── ScadaEngine.LicenseBridge — net48 Windows Service，HASP USB 加密狗授權驗證，靠 Named Pipe 供 Engine 呼叫
```

### 跨模組設計

任務牽涉以下任一主題，先讀 **[docs/架構.md](docs/架構.md)**（含 TOC）：

- **資料流**：Modbus / DB 來源 / OPC UA 來源 → HistoryData / LatestData / MQTT → Web
- **警報系統**：Alarm MQTT 推播 + 規則熱重載（Engine ↔ Web）
- **通知系統**：Line / Email 推播（每群組可選 zh-TW / en，觸發 + 恢復皆通知；寄送結果寫 EventLog 摘要，EventType=3）。Engine 端訊息字典 `Resources/notification.{zh-TW,en}.json`，Web UI 在 `AlarmSetting` 第三個 tab 管理 Email 群組與規則路由。SMTP 走 **MailKit** PackageReference。
- **用電報表**：On-demand 計算 + 葉子層 Hourly 預聚合 + Staleness Window
- **電費計算**：Web 逐時計價（EnergyLeafHourly → ElectricityCostHourly，XX:05 觸發）+ EMS 電費狀態卡 + /HolidaySetting 假日（TOU 落 sun_offday）
- **用水報表與水費**：累積式水表 `WaterMeter*` 體系（boundary 相減 + MaxVolume 溢位 + UnitScale 換算 m³）+ 台水流動水費分段累進 on-demand（無逐時表）；與冷凍噸 WaterCircuit 無關
- **資料庫自動建立與備份**：DB 不存在時啟動安全網自建（無權限優雅降級）+ install-db.ps1 一次性安裝 + Engine 每週 BACKUP（A/B 輪替、結果寫 EventLog）
- **資料表用途對照**：各表 Key 欄位 + 用途一覽 + SID 格式（完整定義以 DatabaseSchema.json 為準）
- **演算法 status 協定**：LogicFlow 節點回傳結構 + per-output port 錯誤傳遞
- **HASP 授權守衛（LicenseBridge）**：net48 bridge 服務靠 Named Pipe 驗加密狗；Engine 每 30 分鐘驗，失敗即暫停 Modbus 採集 + 發 MQTT `SCADA/Sys/License/Status`

---

## Key Configuration Files

| File | Purpose |
|------|---------|
| `ScadaEngine.Engine/Setting/dbSetting.json` | SQL Server connection (host, DB, user, pass) |
| `ScadaEngine.Engine/MqttSetting/MqttSetting.json` | MQTT broker IP/port/topic/retain |
| `ScadaEngine.Engine/Modbus/Modbus.json` | Modbus device definitions (IP, port, tags) |
| `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` | 建表 + 欄位自動同步的**唯一真相來源** — 加欄位只改此檔，Engine 與 Web 啟動時自動補缺欄位（只加不減不改，詳見 docs/架構.md §資料庫結構初始化與欄位同步） |
| `ScadaEngine.Engine/DBPoint/*.json` | DB 來源 Coordinator 點位定義（`DB通訊檔案產生工具.xlsm` 巨集產生；Web「DB 來源」頁可編輯 Name/Unit 回寫）。細節見 docs/架構.md §資料流 + docs/功能說明書_DB來源管理.md |
| `ScadaEngine.Engine/OpcUaPoint/*.json` | OPC UA 來源定義（一檔一 Server 含 Devices 分組；Web「OPC UA 來源」頁全欄位動態編輯回寫，免重啟）。細節見 docs/架構.md §資料流 + docs/功能說明書_OPCUA通訊.md |
| `ScadaEngine.Engine/Setting/DbMaintenanceSetting.json` | 自動建 DB 路徑 + 每週備份排程。同資料夾 `install-db.ps1` 為安裝腳本（idempotent，已綁入部署流程）。細節見 docs/架構.md §資料庫自動建立與每週備份 |
| `ScadaEngine.Engine/Setting/LineSetting.json` | Line Messaging API token + rate limit |
| `ScadaEngine.Engine/Setting/EmailSetting.json` | SMTP host/port/帳密 + rate limit（MailKit）|
| `ScadaEngine.Engine/Resources/notification.{zh-TW,en}.json` | Engine 通知訊息字典（Line + Email 共用，依群組 Language 切換）|

Web reads Engine's `dbSetting.json` via a relative path `../ScadaEngine.Engine/Setting/dbSetting.json` — both projects must run from their own directories.

---

## Database Schema (SQL Server: `wsnCsharp`)

欄位定義唯一真相來源 = `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json`（加欄位只改此檔，Engine / Web 啟動自動補缺欄位，只加不減不改）。**各表 Key 欄位與用途對照 + SID 格式** → [docs/架構.md](docs/架構.md) §資料表用途對照。

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

**SCADA / EMS 雙主題**（新頁面必讀）：全站依路由分兩套色系（SCADA 深藍 / EMS 淡綠）。新增頁面前先判斷歸屬 — **頁面內一律用 Bootstrap primary class（`btn-primary` / `text-primary` / `bg-primary`），不在 `.cshtml` inline 硬寫色號**，EMS 模式由 `ems.css` 自動轉綠。EMS 子頁掛載 4 步驟 SOP + 完整色票 → [docs/設計規範.md](docs/設計規範.md) §色彩系統 §SCADA / EMS 雙主題。

## i18n 規則（zh-TW + en，僅指定頁面）

僅以下頁面已導入 i18n，新增/修改其字串時：

- **已 i18n 範圍**：ScadaPage、Realtime、EnergyReport、EnergyBaseline、History/Trend、EventLog、AccountSetting、ScheduleSetting、ConditionCtrl、LogicFlow、ModbusCoordinator、DbCoordinator、CalcPoint、WeatherSetting、EmsCardSetting、WaterMeterSetting、WaterUsageReport、WaterTariffSetting、WaterCostReport、共用 `_Layout`
- **.cshtml 字串**：`@Localizer["key"]`（key 命名 `feature.section.purpose` 全小寫底線分）
- **JS 字串**：`window.i18n.t('key', {args})`，IIFE 內可宣告 `function t(key, args) { return window.i18n.t(key, args); }` 簡化
- **Controller / Service / Excel exporter**：建構子注入 `IStringLocalizer<T>`，走 `_l["key"].Value`。Singleton 服務若依賴此須改 Scoped
- **resx 檔位於 `ScadaEngine.Web/Resources/`**：中性 `.resx`（內容＝zh-TW）+ `.en.resx`，搭配 `[assembly: NeutralResourcesLanguage("zh-TW")]`。zh-TW 與 en 必須同步補
- **SCADA 專業詞先查 [`docs/i18n-glossary.md`](docs/i18n-glossary.md)**，新詞要先加進 glossary 再用

新增功能時，未在 i18n 範圍的頁面字串不需走 IStringLocalizer，但若**新增 Layout 側欄選單** 則該選單字必須走 `Views.Shared._Layout.{,en}.resx`。

**訊息結構化**（警報 Engine 跨 Web + 控制操作 Web 內部）：EventLog / MQTT 除人類可讀 `Message` 外帶 `MessageKey` + `MessageArgs`（JSON），Web 顯示時經 `AlarmMessageLocalizer` 依 culture 翻譯。三類警報固定 key、10 種 `control.action.*` key、新增動作要改的 switch 一覽 → 見 [docs/功能說明書_多語系.md](docs/功能說明書_多語系.md) §警報訊息結構化 / §控制操作訊息結構化。

DB 內容（點位名、迴路名、警報規則的 DiOnLabel/DiOffLabel）為使用者輸入，**不在 i18n 範圍** — 切英文時若 user input 為中文，這是運維責任而非系統責任。

詳細架構見 [docs/功能說明書_多語系.md](docs/功能說明書_多語系.md)。
