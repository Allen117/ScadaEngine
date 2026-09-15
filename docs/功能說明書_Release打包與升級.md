# Release 打包與升級說明

`BuildRelease.ps1`（開發機執行）→ 產出可攜的 `Release\SCADA_Release_yyyyMMdd_HHmm\` 資料夾 → 拷到 USB → 現場 `Install.bat`（系統管理員）安裝或升級。本文說明**哪些檔案會進包、升級時哪些會被保留、哪些會被換成新版**。

## 一、打包產物結構

```
SCADA_Release_yyyyMMdd_HHmm\
├── Install.bat              <- 現場安裝/升級 Engine + Web + LicenseBridge（右鍵以系統管理員執行）
├── InstallModbusServer.bat  <- 對外 Modbus Gateway 獨立安裝（不由 Install.bat 帶）
├── reset-engineer-password.ps1  <- 工程師工具，隨 USB 攜帶，不落地伺服器
├── Engine\App\             <- Engine 執行檔 + 設定
├── Web\App\                <- Web 執行檔 + wwwroot + 設定
├── LicenseBridge\App\      <- net48 HASP 授權橋接（需 USB 加密狗）
└── ModbusServer\App\       <- 對外 Modbus TCP Gateway
```

四個專案各自 `dotnet publish -c Release --self-contained`（LicenseBridge 為 net48，靠目標機內建 .NET Framework 4.8）。

## 二、「現場資料夾」只帶產生工具，不帶 dev 裝置定義

Engine 的三個資料夾 `Modbus` / `DBPoint` / `OpcUaPoint` 裝的是**各站台自有的現場資料**（裝置點位定義），**不是產品**。但 `dotnet publish` 會依 [ScadaEngine.Engine.csproj](../ScadaEngine.Engine/ScadaEngine.Engine.csproj) 的 `CopyToOutputDirectory`，把 repo 內開發用的 `*.json` / `*.json.example` 一起帶進 `Engine\App`。若不處理，這些 dev 檔案會隨 release **外流到客戶伺服器**（首裝直接落地；升級因 `xcopy /Y` 只加不刪也會殘留）。

因此 `BuildRelease.ps1` 在 publish 之後，會把這三個資料夾**清空、只回填 Excel 產生工具（`*.xlsm`）**：

| 資料夾 | 保留（進包） | 移除（不進包） |
|--------|-------------|----------------|
| `Modbus/` | `Modbus通訊檔案產生工具.xlsm` | 所有 `*.json` 裝置定義 |
| `DBPoint/` | `DB通訊檔案產生工具.xlsm` | `*.json`、`*.sql` 測試檔 |
| `OpcUaPoint/` | `OPCUA通訊檔案產生工具.xlsm` | `*.json`、`*.json.example` 範例 |

- 保留 `*.xlsm`：現場工程師用它產生裝置定義檔（見 docs/功能說明書_DB來源管理.md、_OPCUA通訊.md）。
- 不帶 `*.json.example`：範例對現場無用，只增雜訊。
- **資料夾本身保留**（含工具）：`Install.bat` 靠「`C:\SCADA\Engine\App\Modbus` 資料夾是否存在」判斷升級與否（見下節），空目錄也必須在，否則第二次安裝會被誤判成首裝。

> Web 端這三個資料夾是 ProjectReference 遞移帶進來的**死檔**（Web 實讀 `C:\SCADA\Engine\App\...`），打包時直接整個刪除，不留。

## 三、Install.bat：首次安裝 vs 升級

Install.bat 以 **`C:\SCADA\Engine\App\Modbus` 資料夾是否存在**為哨兵判斷 `_IS_UPGRADE`。

### 升級流程（`_IS_UPGRADE=1`）：先備份 → 整包覆蓋 → 再還原

1. **備份現場設定**到 `C:\SCADA\_ConfigBackup`：
   - Engine：`Modbus`、`Setting`（含 dbSetting.json）、`MqttSetting`、`DBPoint`
   - Web：`Setting`、`MqttSetting`
2. **停服務** → `xcopy /Y` 用 release 包整包覆蓋 `Engine\App` / `Web\App` / `LicenseBridge`
3. **還原現場設定**蓋回步驟 2 的新版設定 → **現場設定最後勝出**

淨結果：

| 項目 | 升級後 |
|------|--------|
| Modbus / DBPoint 現場裝置定義 | **保留現場版**（release 空資料夾不會蓋掉；xlsm 工具則更新為新版） |
| OpcUaPoint 現場定義 | **保留現場版**（不在備份清單，但 release 不帶 json、xcopy 不刪，故原封不動） |
| dbSetting.json / MqttSetting.json | **保留現場版** |
| 程式執行檔、wwwroot | 換成 release 新版 |
| `Setting\*.ps1`（install-db 等腳本） | **一律換 release 新版**（腳本屬程式，不還原舊版） |
| `DatabaseSchema.json` | 換 release 新版（schema 唯一真相來源，只加不減） |
| HTTPS 憑證 `certs\*.pfx` | 沿用既有，不重產（見 docs/功能說明書_內網HTTPS部署.md） |

> ⚠️ 因此**升級不會**把你在 `C:\SCADA` 上的 DB 連線與 Modbus 裝置設定換成 release 包裡的預設值。

### 首次安裝（`_IS_UPGRADE=0`）

無備份/還原；release 包內容直接落地。Modbus / DBPoint / OpcUaPoint 只有 `*.xlsm` 工具、無任何裝置定義 → 現場工程師用工具產生 `*.json` 後放入對應資料夾。

## 四、ModbusServer Gateway（獨立）

`InstallModbusServer.bat` 與 Install.bat **解耦**，需另外執行。升級同樣備份/還原現場 `Setting`、`MqttSetting`，並特別保留執行期產生的 **`AddressMap.json`**（對外 Modbus 位址對照，append-only，絕不可丟）。詳見 docs/功能說明書_ModbusServerGateway.md。

## 相關檔案

- [BuildRelease.ps1](../BuildRelease.ps1) — 打包腳本（Install.bat / InstallModbusServer.bat 由它內嵌產生）
- [ScadaEngine.Engine.csproj](../ScadaEngine.Engine/ScadaEngine.Engine.csproj) — `CopyToOutputDirectory` 決定 publish 帶哪些設定
- 根目錄 `QuickDeployAll.bat` — 一鍵「打包 → 安裝」
