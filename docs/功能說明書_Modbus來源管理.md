# 功能說明書：Modbus 來源管理 (ModbusCoordinator)

## 1. 功能概述

`/ModbusCoordinator` 頁面顯示 Engine 端 Modbus 設備登記（左側清單 + 右側詳情雙欄佈局），並提供兩項編輯能力：

1. **子設備名稱編輯**：多站號（ModbusID 逗號分隔）設備可為每個站號取名（寫 `ModbusCoordinator.DeviceName`，主權在 DB）
2. **點位熱編輯**（限 Admin）：選擇設備後，右側詳情卡片標題列出現「點位設定」按鈕，點擊彈出 Modal 視窗，原地編輯設備 JSON 內點位的 Name / Address / DataType / Ratio / Unit / Min / Max，存檔後 **不需重啟 Engine**，數秒內以新設定採集

### 點位熱編輯核心設計

- **主權在 JSON**：點位欄位的唯一真相是 Engine 執行目錄的 `Modbus/*.json` — Engine 每次重載與每筆控制指令都會重讀 JSON 並整批覆寫 `ModbusPoints` 表，因此 Web **只寫 JSON 檔、不寫 DB**（DB 會被 Engine 自動同步）
- **結構鎖死**：SID 由陣列索引產生（`-S{index+1}`）、控制指令用 TagIndex 定位 — 禁止新增、刪除、排序點位；`IP / Port / ModbusId / ConnectTimeout / 檔名` 唯讀。後端存檔前重讀原檔驗證點位數量未變，不合即回 400。DataType 可改但限 Engine 支援白名單（`INTEGER / UINTEGER / FLOATINGPT / SWAPPEDFP / DOUBLE / SWAPPEDDOUBLE / UINT32BE`，UI 為下拉選單）
- **原子寫檔**：先寫 `*.json.tmp`（不觸發 Engine watcher）→ `File.Replace` 原子替換 → 保留 `*.json.bak` 備份。控制路徑每筆指令都直接讀 JSON 且失敗不重試，原子替換保證任何瞬間讀到完整舊檔或完整新檔
- **保留原檔編碼**：現場檔案為 UTF-16 LE with BOM（Excel 工具產生），寫回時偵測 BOM 沿用原編碼
- **Engine 端配合**（唯二修改）：
  - watcher 補訂 `Renamed` 事件 — `File.Replace` 在 Windows 以 rename 落地，原本只訂 Changed/Created 收不到
  - per-file 去抖 1 秒 — 一次存檔常觸發多個事件，去抖後設備只斷線重連一次

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
