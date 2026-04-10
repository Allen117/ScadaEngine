# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

始終用繁體中文說明

依照 馬斯克 第一性原理

## Documentation Rules

新增或修改功能後，須同步更新 `docs/` 下對應的功能說明書。

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

---

## Key Configuration Files

| File | Purpose |
|------|---------|
| `ScadaEngine.Engine/Setting/dbSetting.json` | SQL Server connection (host, DB, user, pass) |
| `ScadaEngine.Engine/MqttSetting/MqttSetting.json` | MQTT broker IP/port/topic/retain |
| `ScadaEngine.Engine/Modbus/Modbus.json` | Modbus device definitions (IP, port, tags) |
| `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` | DB table schema for auto-init |

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

SID format: `{ModbusID}-S{SequenceNumber}` e.g. `196865-S1`.

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

---

## Authentication

- Cookie-based auth (`ScadaAuth` cookie, 4-hour expiry with sliding)
- Login at `/Login` → on success redirects to `/RealTime`
- Root `/` redirects to `/Login`
- Logout: POST to `/Login/Logout` (requires AntiForgeryToken — use a hidden form, not `<a href>`)
- Default credentials when `Users` table is **empty**: `ITRI / ITRI` (plain text comparison)
- When `Users` has rows: password validated as `SHA256(plaintext).ToLower()` compared against `PasswordHash` column

---

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

## UI Design System

### 框架與函式庫
- **CSS 框架**: Bootstrap 5.1.0（本地安裝）
- **圖示庫**: Font Awesome 6（`fas` 前綴）
- **圖表庫**: Chart.js + date-fns adapter
- **語系**: zh-TW（繁體中文）

### 色彩系統

#### 主要色彩（Bootstrap 語意色）
| 用途 | 色碼 | 場景 |
|------|------|------|
| Primary | `#0d6efd` | 主要按鈕、連結、active 狀態、側欄左邊框 |
| Success | `#198754` | 品質 Good、正向狀態 |
| Danger | `#dc3545` | 品質 Bad、錯誤、高嚴重度警報 |
| Warning | `#ffc107` | 中嚴重度、比較運算子 |
| Orange | `#fd7e14` | 中高嚴重度 |
| Info | `#0dcaf0` | 資訊提示 |
| Secondary | `#6c757d` | 停用狀態、次要文字 |

#### 中性色
| 用途 | 色碼 |
|------|------|
| 頁面背景 | `#f8f9fa` |
| 卡片背景 | `#ffffff` |
| 邊框/分隔線 | `#dee2e6` |
| 輸入框邊框 | `#e1e5e9` |
| Hover 背景 | `#e9ecef` |
| Active 背景 | `#cfe2ff`、`#e8f0fe` |
| 主要文字 | `#212529` |

#### 登入頁專屬
- 標題色: `#2c5aa0`
- 按鈕漸層: `linear-gradient(135deg, #4a90e2, #357abd)`
- Focus 光暈: `rgba(74, 144, 226, 0.25)`

#### 頁尾漸層
```css
background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
```

#### 警報嚴重度
| 等級 | 色碼 |
|------|------|
| 0 Critical | `#dc3545` (red) |
| 1 High | `#fd7e14` (orange) |
| 2 Medium | `#ffc107` (yellow) |
| 3 Low | `#6c757d` (gray) |

### 字體
- **主要字體**: `'Segoe UI', Tahoma, Geneva, Verdana, sans-serif`
- **等寬字體**: `monospace`（SID、數值顯示）
- **基準大小**: 14px（手機）、16px（≥768px）
- **表格內容**: `0.85rem`
- **小字**: `0.80rem`、`0.75rem`
- **字重**: 400（正文）、500（標籤）、600（強調/active）、700（粗體符號）

### 間距
- **卡片內距**: 12px–16px
- **輸入框內距**: `12px 16px`
- **按鈕內距**: `12px 24px`（主要）、`0.15rem 0.55rem`（btn-xs）
- **區塊間距**: Bootstrap `mb-3`、`mb-4`
- **Flexbox gap**: 6px–8px

### 圓角
| 大小 | 值 | 用途 |
|------|-----|------|
| 大 | `10px` | 卡片、卡片 header |
| 中 | `8px` | 表單控件、按鈕 |
| 小 | `6px` | 右鍵選單、Flow 節點 |
| 迷你 | `4px` | 樹狀列表項、小 badge |
| 圓形 | `50%` | 色點指示器 |
| 膠囊 | `999px` | 模式 badge |

### 陰影
| 層級 | 值 | 用途 |
|------|-----|------|
| 卡片 | `0 4px 6px rgba(0,0,0,0.1)` | 標準卡片 |
| 導覽列 | `0 2px 4px rgba(0,0,0,0.1)` | Navbar |
| 右鍵選單 | `0 4px 16px rgba(0,0,0,0.15)` | Context menu |
| 按壓 | `inset 0 2px 4px rgba(0,0,0,0.3)` | 按鈕 active |

### 元件模式

#### 卡片
```html
<div class="card shadow-sm">
  <div class="card-header bg-dark text-white py-2">
    <h6 class="mb-0"><i class="fas fa-[icon] me-2"></i>標題</h6>
  </div>
  <div class="card-body p-0"><!-- 表格用 p-0，表單用 p-3 --></div>
  <div class="card-footer bg-light text-muted"><!-- 統計/時間戳 --></div>
</div>
```

#### 表格
- 類別: `table table-striped table-hover table-bordered`
- 表頭: `thead class="table-dark"` 或 `table-light`
- 外層: `table-responsive` wrapper
- 表頭 sticky: `position: sticky; top: 0; z-index: 30`

#### 側欄（設備清單）
- 寬度: `col-md-2`
- 使用 `list-group list-group-flush`
- Active: 左邊框 3px `#0d6efd` + 背景 `#e8f0fe`
- 子項: `ps-4` 縮排 + 背景 `#f8f9fa`

#### 按鈕
- 格式: `<i class="fas fa-[icon]"></i> 文字`（圖示+文字，禁止只有圖示）
- Hover: `translateY(-1px)` 上浮
- Active: `translateY(1px)` 下壓

#### 表單
- 標籤: `form-label fw-semibold` + 前置圖示
- 輸入: `form-control form-control-sm`
- 下拉: `form-select form-select-sm`
- 排列: `row g-3` + `col-md-*`

#### Modal
- 標準結構: `.modal-header` + `.modal-body` + `.modal-footer`
- 取消: `btn btn-secondary`（左）、確認: `btn btn-primary`（右）

#### 空狀態
```html
<div class="text-center text-muted py-4">
  <i class="fas fa-inbox fa-3x mb-2 d-block"></i>
  <div>尚無資料</div>
</div>
```

### 動畫
- **Hover 過渡**: `transition: all 0.3s ease`
- **邊框變色**: `transition: border-color 0.15s`
- **資料更新閃爍**: `@keyframes cell-flash`（黃色 0.8s）
- **新資料脈動**: `@keyframes pulse-green`（綠色 1s）
- **載入旋轉**: `spinner-border text-primary`

### Z-Index 層級
| Z-Index | 元素 |
|---------|------|
| 10 | Flow 節點 |
| 20 | Flow port |
| 25 | Tooltip |
| 30 | Sticky header |
| 9999 | 右鍵選單 |

### 圖示慣例（Font Awesome 6 `fas`）
- **導覽**: `fa-desktop`（監控）、`fa-tachometer-alt`（數據）、`fa-chart-area`（趨勢）、`fa-bell`（警報）
- **設備**: `fa-plug`（協調器）、`fa-microchip`（子設備）、`fa-server`（設備）
- **操作**: `fa-plus`（新增）、`fa-edit`（編輯）、`fa-trash-alt`（刪除）、`fa-save`（儲存）、`fa-sync-alt`（刷新）
- **狀態**: `fa-wifi`（連線）、`fa-exclamation-triangle`（警告）、`fa-inbox`（空狀態）
- **資料**: `fa-calendar`（日期）、`fa-clock`（時間）、`fa-database`（資料庫）
- **間距**: 圖示後加 `me-1` 或 `me-2`
