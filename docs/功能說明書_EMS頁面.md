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

共用 `Views/Shared/_Layout.cshtml` + `ViewData["EmsMode"] = true` 開關（決策 1）。`_Layout` 內：

```csharp
bool isEmsMode = ViewData["EmsMode"] as bool? == true;
```

開關控制四件事：

1. `<body>` 加 `ems-mode` class，吃 `ems.css` 樣式覆蓋
2. `<nav>` class 從 `navbar-dark bg-primary` 換成 `navbar-light`
3. brand href 從 `/ScadaPage` 換成 `/EMS`、icon 從 `fa-industry` 換成 `fa-leaf`、字串走 `layout.brand.ems` 而非 `layout.brand`
4. navbar 主選單清單從「ScadaPage/RealTime/控制邏輯/歷史資料/能源管理/系統設定」整個改成「水系統迴路設定/電表迴路設定/用電報表/冷凍噸報表」4 個直接 nav-link

footer / 語系切換 / 使用者選單 / 登出 modal 完全共用，未做差異化。

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

目前留白 — 僅渲染一個空 `<div class="ems-hub">`，後續決定要放 KPI / dashboard / 跳轉指引時再擴充。CSS 已預留 `min-height: calc(100vh - 200px)` 避免 footer 緊貼 navbar 視覺塌陷。

## 8. 檔案位置

```
ScadaEngine.Web/
├── Features/Ems/
│   ├── Controllers/EmsController.cs
│   └── Views/Index.cshtml
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
