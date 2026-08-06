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

## Git Commit 訊息格式

- **Subject（第一行）**：≤ 72 字元（約 30 個中文字），一句話講清楚改了什麼，讓 `git log --oneline` 可掃
- 空一行後接 **body**：詳細內容照舊全寫（動機、設計決策、相容性、bug 修正、i18n / docs 同步），資訊量不減，只是從單行 subject 搬進 body，建議 `-` 條列分項
- 歷史 commit 為千字單行 subject 舊格式，**不回改**；讀舊 history 時知悉即可

## Project Overview

.NET 8 SCADA 工業監控系統，包含 Engine（Modbus 資料採集 + MQTT 發布）與 Web（ASP.NET Core MVC 儀表板）。

## 專案專屬規則檔

**Web 專屬規則已移至 [ScadaEngine.Web/CLAUDE.md](ScadaEngine.Web/CLAUDE.md)**（Feature Folder + MVC 架構、Razor 前端分離、flatpickr 時間輸入、Web Project Structure、UI 設計系統、i18n 規則）。觸碰該目錄檔案時會自動載入；若在規劃／討論 Web 功能而尚未讀取該目錄檔案，先讀該檔。

## Build & Run

兩個專案各自 `dotnet run`，Web 跑在 **5038** (HTTP) / **7189** (HTTPS)。
Engine 是背景服務，無 HTTP endpoint。

> ⚠️ Razor views 是 **precompiled**，改 .cshtml 必須 `dotnet build` 才生效。

### 卡住的 Web 進程（鎖住 bin 導致 build 失敗）

⚠️ 這台機器上**同時會有兩個 `ScadaEngine.Web` 進程**：
`C:\SCADA\Web\App\` 是**部署站台**、`...\Desktop\ScadaEngine\ScadaEngine.Web\bin\Debug\` 才是開發 build。
只有後者會鎖住 build 輸出，**不分路徑盲殺會一併關掉正式站台**，務必用路徑過濾：

```powershell
Get-Process -Name 'ScadaEngine.Web' -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like '*\Desktop\ScadaEngine\ScadaEngine.Web\bin\*' } |
  Stop-Process -Force
```

停之前先 `Get-Process -Name 'ScadaEngine.Web' | Select-Object Id, Path` 確認打到的是哪一個。
另：為驗證而啟動的開發 Web 進程，**驗完必須在同一輪關掉**，否則下次 build 會被自己鎖住。

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
- **用氣報表與氣費**：累積式天然氣表 `GasMeter*` 體系（與水表平行複製、各自獨立；Engine XX:03 聚合）。兩處刻意差異：點位判定走**單位 + 點位名稱關鍵字雙條件**（單位納入「度」但靠名稱擋掉電表點位，水表僅看單位）；氣費期別多 `IsSkipped` 支援**兩月一期**
- **月結期別四分**：電費 `BillingPeriods` / 水費 `WaterBillingPeriods` / 氣費 `GasBillingPeriods`（各自獨立設定頁，氣費多 IsSkipped）/ 曆月（冷凍噸、能源申報、能源基線刻意不走期別）— 對照表見 docs/架構.md §用電報表
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

### MQTT Retain Flag
Engine publishes with `Retain=true`. When restarting Engine, old retained messages (without `name` field) remain on the broker. A full restart of both Engine and broker clears stale retained messages.

### IDataRepository (Scoped)
Defined in `ScadaEngine.Engine` but used by both Engine and Web. Web registers `SqlServerDataRepository` as Scoped. `MqttRealtimeSubscriberService` (Singleton) accesses it via `IServiceProvider.CreateScope()`.
