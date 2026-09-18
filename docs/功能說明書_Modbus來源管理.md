# 功能說明書：Modbus 來源管理 (ModbusCoordinator)

## 1. 功能概述

`/ModbusCoordinator` 頁面顯示 Engine 端 Modbus 設備登記（左側清單 + 右側詳情雙欄佈局），並提供兩項編輯能力：

1. **子設備名稱編輯**：多站號（ModbusID 逗號分隔）設備可為每個站號取名（寫 `ModbusCoordinator.DeviceName`，主權在 DB）
2. **點位熱編輯**（限 Admin）：選擇設備後，右側詳情卡片標題列出現「點位設定」按鈕，點擊彈出 Modal 視窗，原地編輯設備 JSON 內點位的 Name / Address / DataType / Ratio / Unit / Min / Max / **子設備（Device）**，存檔後 **不需重啟 Engine**，數秒內以新設定採集
3. **站號內子設備分群（Tag.Device）**：一顆 PLC（單一站號）內放多台設備時，可為每個點位標註所屬子設備做分群，讓點位選擇器可展開瀏覽（見 §站號內子設備分群）

### 點位熱編輯核心設計

- **主權在 JSON**：點位欄位的唯一真相是 Engine 執行目錄的 `Modbus/*.json` — Engine 每次重載與每筆控制指令都會重讀 JSON 並整批覆寫 `ModbusPoints` 表，因此 Web **只寫 JSON 檔、不寫 DB**（DB 會被 Engine 自動同步）
- **結構鎖死**：SID 由陣列索引產生（`-S{index+1}`）、控制指令用 TagIndex 定位 — 禁止新增、刪除、排序點位；`IP / Port / ModbusId / ConnectTimeout / 檔名` 唯讀。後端存檔前重讀原檔驗證點位數量未變，不合即回 400。DataType 可改但限 Engine 支援白名單（`INTEGER / UINTEGER / FLOATINGPT / SWAPPEDFP / DOUBLE / SWAPPEDDOUBLE / UINT32BE / DEC10K3 / BCD / BIT0`–`BIT15`，UI 為下拉選單）。白名單唯一真相來源為 Engine 的 `ModbusTagModel.SupportedDataTypes`，Web 的 `ModbusConfigFileService.SupportedDataTypes` 直接引用它，前端 `modbuscoordinator.js` 的 `DATA_TYPES` 手動對齊（BIT 以迴圈產生）。各型別語意見 [功能說明書_Engine核心.md](功能說明書_Engine核心.md) §4.3

#### DataType 欄的兩段式下拉

`BIT0`–`BIT15` 攤平會讓下拉長達 26 項，故 UI 拆成兩段（`makeDataTypeCell`）：

- **型別下拉**只放 10 項，BIT 收斂為單一的群組代表值 `BIT`（**僅存在於 UI，本身不是合法 DataType**）
- 選到 `BIT` 才顯示右側 0–15 的**位元索引下拉**，其餘型別隱藏
- 兩者由 `syncDataType()` 合成後寫進同格的隱藏欄位 `[data-field="dataType"]`

**儲存格式不變**，JSON 寫入的仍是 `BIT5` 這種單一字串，後端與 Engine 零改動；`collectAndValidatePoints` / `countChanges` 照舊讀該隱藏欄位。

`parseBitIndex()` 刻意只認大寫精確格式 `^BIT(\d{1,2})$`（與後端白名單同規則）— `Bit5` 這類舊寫法會落到「保留原值為第一個選項」路徑，才不會在使用者未操作時被靜默正規化成 `BIT5` 而多算一筆變更。
- **原子寫檔**：先寫 `*.json.tmp`（不觸發 Engine watcher）→ `File.Replace` 原子替換 → 保留 `*.json.bak` 備份。控制路徑每筆指令都直接讀 JSON 且失敗不重試，原子替換保證任何瞬間讀到完整舊檔或完整新檔
- **保留原檔編碼**：現場檔案為 UTF-16 LE with BOM（Excel 工具產生），寫回時偵測 BOM 沿用原編碼
- **Engine 端配合**：
  - watcher 補訂 `Renamed` 事件 — `File.Replace` 在 Windows 以 rename 落地，原本只訂 Changed/Created 收不到
  - per-file 去抖 1 秒 — 一次存檔常觸發多個事件，去抖後設備只斷線重連一次
  - **副檔名守衛**（`IsJsonConfigFile`）— watcher 的 `*.json` filter 對 rename 是「舊名或新名任一符合就觸發」，`File.Replace` 把 `X.json → X.json.bak`（舊名符合）會以 `e.FullPath=X.json.bak` 觸發 Renamed；若不擋，`GetFileNameWithoutExtension("X.json.bak")="X.json"` 會被當 Coordinator 名寫進 DB 生幽靈。三個 handler 與 `ReloadDeviceConfigAsync` 皆先檢查副檔名為 `.json` 才放行

## 站號內子設備分群（Tag.Device）

**痛點**：一顆 PLC（單一站號）放 5 種設備、每設備 5 個點時，25 個點在點位選擇器同一層平鋪、找設備要滾很久。Modbus 原本缺「站號內子設備」這一分群層（計算點位有 `GroupName`、DB 來源有 Coordinator 群組、OPC UA 有 Devices 分組，只有 Modbus 沒有）。

### 資料鏈與主權

- **主權在 `Modbus.json` 的 `Tag.Device`**（optional 字串欄）。Engine 載入 JSON → 寫進 `ModbusPoints.DeviceGroup`（每次重載重寫的投影，與其他欄位同機制；DB 只是投影，改 JSON 才是改分群）
- 未填 `Device` 的既有設定檔**行為完全不變**（留白 = 未分群，可一台一台慢慢補）
- 熱編輯 Modal 的「子設備」欄寫回 JSON 的 `Tag.Device` → Engine watcher 重載 → `DeviceGroup` 更新，**免重啟**

### 決策 4：站號 / Device 互斥

現場的多站號 Coordinator，一個站號本來就是一台設備，不會再有站號內分設備需求。因此兩種分群來源**互斥**，由站號數決定走哪條、永不疊加：

| Coordinator 情況 | 分群依據 | Device 欄 |
|---|---|---|
| 多站號（ModbusId 逗號分隔） | 站號 → `DeviceName`（現況邏輯不變） | **忽略**：Modal 內 disable + 提示；Engine `ResolveDeviceGroup` 一律寫 null |
| 單站號 + 有 Device | `Tag.Device` | 可編輯 |
| 單站號 + 無 Device | Coordinator 名（直接可點） | 可編輯（留白） |

互斥規則的單一真相 = `ModbusPointModel.ResolveDeviceGroup(device, isMultiStation)`（Engine 載入 JSON 與 Web 熱編輯共用）。

### 四級 fallback 鏈（點位設備標籤）

點位在選擇器顯示的設備標籤走：`Tag.Device` → 多站號的站號子設備名 → Coordinator 名 →（設備清單的「未分群」桶）。

### 前端共用層 `point-grouping.js`

「SID → 站號內子設備」的解析（`split(',')` + `CoordinatorId*65536 + ModbusId*256` 落點）原本被複製 5+ 份、fallback 各自走偏。已收斂為 `wwwroot/js/common/point-grouping.js`（`window.PointGrouping`），對外提供 `parseCoord` / `subOfSid` / `pointDeviceLabel`（四級鏈）/ `coordDeviceGroups`（單站號盤點 Device、多站號回 null）等。designer / logicflow / calcpoint 點位選擇器據此讓「單站號有 Device」的 Coordinator 可展開子選單 + 「未分群」桶；eventlog / energy-baseline / history 的設備標籤亦走同一支。

> ⚠️ **現場 `Modbus通訊檔案產生工具.xlsm` 需同步**：巨集若不加 `Device` 欄輸出，現場重產設定檔會讓分群整批消失（VBA 需人工改，見 docs/plans 對應 plan）。

## 2. 路由

| 方法 | 路由 | 說明 | 認證 |
|------|------|------|------|
| GET  | `/ModbusCoordinator` | 設備清單頁 | 需登入 |
| POST | `/ModbusCoordinator/UpdateDeviceName` | 更新子設備名稱（寫 DB） | 需登入 |
| GET  | `/ModbusCoordinator/Points/{name}` | 讀取設備 JSON 點位清單 | **Admin** |
| POST | `/ModbusCoordinator/UpdatePoints` | 原地更新點位欄位（寫 JSON） | **Admin** |

`{name}` = Coordinator 名稱 = JSON 檔名（不含副檔名）。非 Admin 直接呼叫回 403，頁面上不渲染編輯卡片。

## 3. 設定（Web appsettings.json）

```json
"EngineModbusConfig": {
  "WatchedFolder": "../ScadaEngine.Engine/bin/Debug/net8.0/Modbus",
  "MirrorFolder": "../ScadaEngine.Engine/Modbus"
}
```

| 鍵 | 說明 |
|----|------|
| `WatchedFolder` | Engine watcher 監控的資料夾 = Engine **執行目錄**的 `Modbus\`。dev 為 `bin/Debug/net8.0/Modbus`；**publish 部署時填 Engine exe 同層 `Modbus\` 的絕對路徑** |
| `MirrorFolder` | 可選。dev 時鏡像寫回原始碼資料夾，避免 rebuild 後設定倒退；**正式環境留空** |

## 4. 存檔流程

```
Web UI 存檔（Admin）
  → 後端重讀原檔驗證（數量一致、DataType 未變、Address/Ratio/Min/Max 格式）
  → 只改 Tags[i] 可編輯欄位 → 寫 *.json.tmp → File.Replace 原子替換（留 .bak）
  → Engine watcher（Renamed）→ 去抖 1 秒 → 停舊採集 → 起新採集 → ModbusPoints 刪+插
  → 每個有變更的點位寫一筆 EventLog 稽核（EventType=3，key control.action.point_config_changed，
     args {username, name, value=變更摘要如 "Address: 30513 → 30514"}，zh-TW/en 自動翻譯）
```

驗證規則（前後端一致）：

- Name 必填；Address 支援兩種慣例（與 Engine `ParseAddress` 一致，靠**字串長度含前導 0** 區分）：
  - 5 位數：`1–9999`（Coil）、`1xxxx`（Discrete）、`3xxxx`（Input）、`4xxxx`（Holding）
  - 6 位數擴充（offset 可達 65535）：`000001–065536`（Coil）、`1xxxxx`（Discrete）、`3xxxxx`（Input）、`4xxxxx`（Holding），如 `430001` = Holding offset 30000
  - ⚠️ 兩慣例數值重疊但意義不同：`045000` = Coil offset 44999，`45000` = Holding offset 4999 — 前導 0 有語意，不可省略
- DataType 需在 Engine 支援白名單內（大小寫不拘，Engine 端 ToUpper 比對）；變更會影響暫存器讀取長度與數值轉換，確認框有註明
- Ratio 必須為數字；Min / Max 為數字或留空
- 無任何欄位變更時不寫檔（不觸發重連）

## 5. 已知限制與運維注意

- **改 Address 後 SID 不變**：歷史資料無縫接續 — 同一 SID 前後量測不同實體暫存器屬運維語意責任（存檔確認框已註明）
- **點位改名傳播範圍是「部分」**：只存 SID、顯示時現查的功能會自動更新（警報規則、ConditionCtrl、History/Trend、Realtime、EnergyMeter 副標）；抄存名稱副本的不會（ScadaPage/Designer 元件標籤、LogicFlow 節點 `pointName`、EventLog 既有事件） — 需至該頁重新綁定
- **重載空窗 1~3 秒**：該設備畫面值短暫凍結（非 Bad Quality）
- **存檔瞬間在途的控制指令**會用舊位址成功寫入（一筆）；位址變更屬排程性操作，接受此窗口
- **手動改檔仍有效**：直接編輯 JSON（Excel 工具流程）同樣觸發熱重載，去抖同樣生效
- Engine csproj 已改 `Modbus\*.json` 萬用字元 — **部署新版後所有 `Modbus\` 下的 JSON（含 Modbus2.json）都會生效**，Engine 會開始採集這些設備（DB 建新 Coordinator、嘗試連線），部署時需知會運維

## 6. 相關元件

| 元件 | 用途 |
|------|------|
| `ScadaEngine.Web/Services/ModbusConfigFileService.cs` | 讀寫 Modbus JSON（驗證、原子替換、鏡像）— Singleton |
| `Features/ModbusCoordinator/Models/ModbusPointEditDtos.cs` | 點位 DTO + 更新請求/結果 |
| `ScadaEngine.Web/Services/ControlEventLogger.cs` | `LogPointConfigChangedAsync` 稽核寫入 |
| `wwwroot/js/modbuscoordinator.js` | 點位表格載入 / 驗證 / 存檔（IIFE） |
| `ScadaEngine.Engine/.../ModbusCollectionManager.cs` | watcher 去抖重載 |
| `ScadaEngine.Engine/.../ModbusConfigService.cs` | watcher（Changed / Created / Renamed） |
