# Modbus 點位站號內設備分群（Tag.Device）+ 分群解析共用層

**狀態**: 進行中 <!-- 進行中 | 已完成 | 廢棄 -->
**建立**: 2026-09-18
**最後更新**: 2026-09-18
**相關 commit**: `9119ade`（階段 A–E 核心）；歷史/即時頁 Device 分群另 commit

---

## 目標 & 背景

**現場痛點**：設定人員在點位選擇器找點位時，若設備排在後面要滾很久。

**根因（第一性原理）**：Modbus 缺「站號內子設備」這一層分群。一顆 PLC（單一站號）內放 5 種設備、每設備 5 個點時，25 個點平鋪在同一層，除了點位名字串外沒有任何資訊可分群。

四種資料來源的分群現況盤點：

| 來源 | 站號/Server 內的子設備分群 | 存在哪 |
|---|---|---|
| 計算點位 | ✅ `GroupName` | `CalculatedPoints.GroupName` |
| DB 來源 | ✅ Coordinator 群組 | `DBCoordinator` |
| OPC UA | ✅ `Devices` 分組 | JSON 一檔一 Server 含 Devices，攤平時帶 `DeviceName` 進 `OpcUaPoints` |
| **Modbus** | ❌ **無** | 只有 `Tags[]` 平鋪 |

**OPC UA 已經解決了完全相同的問題**（`OpcUaConfigLoader.cs` 攤平 `Devices` → 點位帶 `DeviceName`），Modbus 比照即可，不是新發明。

**第二層缺口**：「SID → 子設備名」的解析邏輯在前端被複製了至少 5 份，各自 `split(',')` + `nId * 65536 + mid * 256` 硬算，且 fallback 寫法已經不一致（有的退到 `d.szName`，有的退到站號數字）。不先收斂，本次改動要在 10+ 個檔案重複做一遍。

---

## 驗收條件

- [ ] `Modbus.json` 的 Tag 可填 optional `Device` 欄位，Engine 載入後寫進 `ModbusPoints.DeviceGroup`
- [ ] 未填 `Device` 的既有設定檔**行為完全不變**（不需一次補完才能上線）
- [ ] 點位選擇器：單站號 + 有 Device 的 Coordinator 變成可展開群組，展開後列出各 Device
- [ ] **多站號 Coordinator 的分群行為與改動前完全一致**（站號優先，`Tag.Device` 靜默忽略）
- [ ] 點位熱編輯 Modal 在多站號 Coordinator 下，Device 欄位 disable 並顯示不適用提示
- [ ] 未分群的點位顯示成一個**看得見的「未分群」桶**，並顯示點數，不隱藏
- [ ] `/ModbusCoordinator` 點位熱編輯 Modal 可編輯 Device 欄位，存檔後不需重啟 Engine
- [ ] 全部使用點位清單的頁面走同一支共用解析模組，分群顯示一致
- [ ] 點位選擇器的**設備層（Step 1）有搜尋框**（現況只有點位層 Step 2 有）
- [ ] 現場用 `Modbus通訊檔案產生工具.xlsm` 重產設定檔後，Device 分群不會掉

---

## 檔案異動清單

> 路徑分兩類：**已驗證存在**（本次調查中實際讀過/grep 命中）與 **(待確認)**（動工時先確認再改，不可盲勾）。

### 新增

- [x] `ScadaEngine.Web/wwwroot/js/common/point-grouping.js` — 分群解析單一真相
  - 沿用既有 `js/common/` 模式（已有 `schedule-eval.js` / `motor-equip-svg.js` / `pipe-svg.js`）
  - **階段 A 實際介面**（比原計畫更貼近現況）：`parseCoord(d)`（吸收 Hungarian / camelCase）、`getSidPrefix` / `isCalcSid` / `isDbSid` / `isOpcSid`、`isMultiId(coord)`、`coordContainsSid(sid,c)`、`subOfSid(sid,coord)→{mid,idx,subName}`、`subRangeBase(id,mid)`
  - ⚠️ 原計畫的 `resolveGroup(...)` 四級 fallback 版留待**階段 C** 再加（階段 A 只收斂「算」的部分，顯示用預設標籤/i18n key 各頁不同，仍由各頁自理，確保行為等價）

### 修改 — Engine 側

- [x] `ScadaEngine.Engine/Communication/Modbus/Models/ModbusModels.cs` — `ModbusTagModel` 加 `szDevice`（optional，預設空字串）
- [x] `ScadaEngine.Engine/Communication/Modbus/Models/ModbusPointModel.cs` — 加 `szDeviceGroup` + **`ResolveDeviceGroup(device, isMultiStation)` 靜態方法（決策 4 互斥規則單一真相，Engine/Web 熱編輯共用）** + `FromTag` 加 optional `szDeviceGroup` 參數
- [x] `ScadaEngine.Engine/Communication/Modbus/Services/ModbusConfigService.cs` — `MapJsonToConfigModel` 讀 `tagJson.Device`；`InsertTagsToModbusPointsAsync` 依 `ResolveDeviceGroup` 投影（多站號 → null）
- [x] `ScadaEngine.Engine/Data/Repositories/SqlServerDataRepository.cs`
  - INSERT 欄位清單 + 參數加 `DeviceGroup`
  - `GetAllModbusPointsAsync` 的 SELECT 加 `DeviceGroup AS szDeviceGroup`
  - ⚠️ `GetModbusPointsByCoordinatorAsync`（另一 SELECT）欄位無 alias、本就與 model 不對映（既有狀態），未在 plan 範圍，不動
- [x] `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` — `ModbusPoints` 加 `DeviceGroup`（nvarchar 100, Nullable）
- [~] `ScadaEngine.Engine/Modbus/Modbus.json`、`Modbus2.json` — **不補範例**（留白即行為不變；且現場檔為 UTF-16、多站號 `1,2,3` 本就會被決策 4 忽略，補了也不生效，反易誤導）

### 修改 — Web 側（全部改走共用層）

**點位選擇器（本次主要 UX 改動）**
- [x] `ScadaEngine.Web/wwwroot/js/designer/picker.js` — enrich / `_showPickerForBoundSid` / `renderDeviceList` 三處 SID 數學已改走共用層（helper 委派 + `subOfSid`/`parseCoord`）。⚠️ 展開條件改「多站號 **或** 有 Device」留待階段 C
- [x] `ScadaEngine.Web/wwwroot/js/logicflow/picker.js` — 同上三處（保留其 sub 列 fallback 退 `String(mid)` 的差異）
- [x] `ScadaEngine.Web/wwwroot/js/calcpoint.js` — enrich + `_pkRenderDeviceList` 已改走共用層

**其餘使用 `szDeviceName` / `deviceName` 的前端（grep 命中，改走共用層）**
- [x] `ScadaEngine.Web/wwwroot/js/alarmsetting.js` — `isCalcSid` 委派 + `getSubDevices` 改走 `parseCoord`（B型 camelCase；直接算術 helper 保留）
- [~] `ScadaEngine.Web/wwwroot/js/chilledwater.js` — **C型，後端已解析 `deviceName`，無 SID 數學可收斂 → 階段 A 不動**
- [~] `ScadaEngine.Web/wwwroot/js/watermeter.js` — 同上（C型）
- [~] `ScadaEngine.Web/wwwroot/js/gasmeter.js` — 同上（C型）
- [~] `ScadaEngine.Web/wwwroot/js/energymeter.js` — 同上（C型，`optgroup`）
- [x] `ScadaEngine.Web/wwwroot/js/conditionctrl.js` — `isCalcSid`/`isDbSid` 委派 + `getSubDevices` 改走 `parseCoord`
- [x] `ScadaEngine.Web/wwwroot/js/energy-baseline.js` — `sidNumericPrefix` 委派、`modbusSubIdOf`/`deviceScopeLabel`/`pkPointDeviceLabel`/`showModbusDevices` 改走共用層（`modbusSubIdOf` 保留「單站號也回 mid」的範圍語意）
- [x] `ScadaEngine.Web/wwwroot/js/history.js` — enrich 改走共用層（混合形狀：coord Hungarian + point camelCase，`parseCoord`/`subOfSid` 皆吸收）
- [x] `ScadaEngine.Web/wwwroot/js/eventlog.js` — enrich + `ppRenderDevices` 改走共用層（保留「找不到設備留空字串、不退 groupName」的原行為）
- [~] `ScadaEngine.Web/wwwroot/js/designer/row-template.js`、`prop-panel.js` — 已確認**無 SID→device 解析邏輯**，無需改

**點位熱編輯 UI（讓現場能填 Device）**
- [x] `ScadaEngine.Web/Features/ModbusCoordinator/Views/Index.cshtml` — 點位設定 Modal 加「子設備」欄（th + colspan 8→9）
- [x] `ScadaEngine.Web/wwwroot/js/modbuscoordinator.js` — `makeDeviceCell`（多站號 disable + 提示）、renderPoints/collect/countChanges 帶 device
- [x] `ScadaEngine.Web/Services/ModbusConfigFileService.cs` — 讀/寫/BuildChangeSummary 帶 `Device`（`UpdatePoints` 走此服務，Controller 無需改）
- [x] `ScadaEngine.Web/Features/ModbusCoordinator/Models/ModbusPointEditDtos.cs` — `ModbusPointDto` 加 `Device`

**其他**
- [ ] 各頁 `.cshtml` 加 `<script src="~/js/common/point-grouping.js">`（載入順序須在各頁主 JS 之前）
- [ ] 回傳點位清單的 Controller / DTO 補 `szDeviceGroup` **(待確認實際檔案)** — `DesignerController.cs`、`EnergyMeterController.cs`、`ChilledWaterSystemController.cs`、`WaterMeterSettingController.cs`、`GasMeterSettingController.cs`、`EnergyBaselineController.cs` 均 grep 命中 `DeviceName`
- [ ] i18n resx：新增「未分群」等字串（zh-TW + en 同步）；SCADA 專業詞先查 `docs/i18n-glossary.md`

### 修改 — 現場工具

- [ ] `ScadaEngine.Engine/Modbus/Modbus通訊檔案產生工具.xlsm` — 巨集加 Device 欄輸出
  - ⚠️ **本方案最容易被忽略的隱藏成本**：不改的話，現場用工具重產設定檔時分群會整批掉

### 資料庫

- [ ] `ModbusPoints` 加欄位 `DeviceGroup`（`nvarchar(100)`, Nullable）
  - 只改 `DatabaseSchema.json`，Engine / Web 啟動自動補欄位（只加不減不改）
  - 依專案規則（上線前不累積 migration）不寫 migration script

---

## 關鍵設計決策

### 決策 1：分群主權放 `Modbus.json` 的 `Tag.Device`，DB 只是投影
- **選擇**：JSON 為唯一真相來源，`ModbusPoints.DeviceGroup` 是每次重載時重新寫入的投影
- **理由**：`SqlServerDataRepository.cs:1030` 顯示 `ModbusPoints` 是 **delete-then-insert 從 JSON 重建**（按 Coordinator 的 SID 範圍整段刪除再插入）。分群若只存在 DB，Engine 下次重載設定就會被整批洗掉
- **放棄的選項**：只在 `ModbusPoints` 加 `DeviceGroup` 欄位、讓使用者在 Web UI 直接分類 —— 資料會靜默消失，屬於最糟糕的失敗模式

### 決策 2：`Tag` 上平鋪一個 `Device` 字串，不改成巢狀 `Devices[].Tags[]`
- **選擇**：平鋪 optional 字串欄位
- **理由**：同樣達到分群效果，且與現有「點位熱編輯 Modal」欄位式 UI 天然相容；改動面小一個量級
- **放棄的選項**：比照 OPC UA 的巢狀 `Devices[].Tags[]` —— 結構上更正確，但 `Modbus.json` 格式大改會波及 `.xlsm` 巨集重寫與所有既有設定檔的相容處理，代價與效益不成比例

### 決策 3：層級數不變（仍是三層），沿用既有折疊 UI
- **選擇**：`renderDeviceList()`（picker.js:657）的展開條件由 `modbusIds.length > 1` 改為「多站號 **或** 有 Device」，沿用同一套 `dev-sub-menu` 折疊模式
- **理由**：「站號拆出的子設備」與「Tag.Device」在語意上是**同一個概念**（一台實體設備）的兩種取得方式，不應該變成兩層。層級不變 = 點擊數不變 = 現場習慣不變
- **四種情境對照**：

  | Coordinator 情況 | 現況 | 改後 |
  |---|---|---|
  | 單站號、無 Device | 直接可點 → 點位 | **不變** |
  | 多站號、無 Device | 展開 → 北101…北108 → 點位 | **不變** |
  | 單站號、有 Device | 直接可點 → 25 點平鋪 | 展開 → 5 個 Device → 各 5 點 |
  | 多站號 **且** 站號內有 Device | — | **現實中不存在**，見決策 4 |

### 決策 4：站號與 Device 互斥 —— 多站號時忽略 `Tag.Device`
- **前提（使用者 2026-09-18 確認）**：現場的多站號 Coordinator，**一個站號本來就是一台設備**，不會再有站號內分設備的需求。因此「多站號 × 站號內多 Device」的混合情境**現實中不存在**
- **選擇**：兩種分群來源**互斥**，由站號數決定走哪一條，永不疊加
  - 多站號（`modbusIds.length > 1`）→ 站號 = 設備，**忽略 `Tag.Device`**（現況邏輯，完全不動）
  - 單站號 + 有 Device → Device = 設備（本次新增）
- **理由**：不為不存在的情境設計機制。互斥規則讓層級永遠是三層、沒有複合標籤、沒有第四層，共用層的解析邏輯也少一個分支
- **放棄的選項**：
  - 複合標籤扁平化（`北101 / 冰水主機`）—— 為不存在的情境增加標籤複雜度
  - 加第四層（Coordinator → 站號 → Device → 點位）—— 同上，且會切斷搜尋
- **防呆**：規則雖然互斥，但若有人在多站號 Coordinator 的 Tag 上誤填了 `Device`，行為必須**明確而非未定義** ——
  - 解析層：站號優先，`Tag.Device` 靜默忽略（不報錯、不影響採集）
  - 點位熱編輯 UI：多站號 Coordinator 的 Device 欄位 **disable 並附提示**「多站號設備以站號分群，此欄不適用」，從源頭避免誤填

### 決策 5：留白是必須的，走四級 fallback 鏈
- **選擇**：`Device` 一律 optional，取不到就逐級退回

  | 優先序 | 來源 | 情境 |
  |---|---|---|
  | 1 | `Tag.Device` | 人工指定（一顆 PLC 多設備） |
  | 2 | 站號 → `DeviceName` | 多站號（純算術查表，SID 可逆） |
  | 3 | Coordinator 名 | 單站號、未分群 |
  | 4 | 「其他 / 未分群」 | 都取不到 |

- **理由**：既有 `Modbus.json` 全都沒這欄位，`.xlsm` 產出的也不會有。**不能要求現場一次補完才開始有用**。留白的語意是「不變差」而非「變好」—— 沒填的維持現狀，填了的立刻受益，可一台一台慢慢補
- **「未分群」要顯示成看得見的桶**（照 `picker.js:361-363` 計算點位的 `hasUngrouped` 分支），並顯示點數。藏起來的話永遠沒人補；看得到「還有 40 點沒分」才形成補完誘因

### 決策 6：先抽共用層，再改頁面
- **選擇**：Step 1 先做 `point-grouping.js` 並讓所有頁面改走它（行為不變的純重構），Step 2 才加 Device 分群
- **理由**：同一段解析邏輯目前散在 5+ 份複製中。若先加功能再抽共用，等於同一件事改十幾遍，且任一處漏改就出現「這頁看得到分群、那頁看不到」的不一致
- **順帶修掉**：5 份複製的 fallback 行為目前已經不一致

### 決策 7：設備層（Step 1）補搜尋框
- **選擇**：Step 1 加搜尋，與 Step 2 既有的 `ppPointSearch` 對齊
- **理由**：現況搜尋**只存在於選完設備之後的點位層**，現場人要找某台設備時第一步只能滾 —— 這是「滾很久」的另一半原因。決策 4 定案為互斥後，Step 1 項目數不會因 Device 而暴增，但設備本身就多（多站號各自展開），搜尋仍是直接命中痛點的一環

---

## 實作步驟

**階段 A — 共用層（地基，一次到位，不可分批）**

1. [x] 建 `js/common/point-grouping.js`，把現有解析邏輯收斂為一份（先不含 Device，**行為完全等價**）
2. [x] 各頁 `.cshtml` 引入該檔（8 頁：Designer / LogicFlow / CalcPoint / EventLog / EnergyBaseline / AlarmSetting / ConditionCtrl / History；載入順序在各頁主 JS 之前）
3. [x] 逐檔改走共用層：designer/picker、logicflow/picker、calcpoint、eventlog、energy-baseline、history、alarmsetting、conditionctrl（**C型 chilledwater/watermeter/gasmeter/energymeter 後端已解析，無 SID 數學可收斂，不動**）
4. [x] 驗證：以 **Node 等價性 harness**（`scratchpad/equiv.js`）把舊內嵌解析 vs 新共用層跑同批 fixture 逐一比對 → **266 項全數位元相同**（涵蓋單站號/多站號對齊/逗號數兩方向不對齊/中間空名/子站號 gap/calc-db-opc/界外）+ `dotnet build` 全綠。純函式等價已證，此階段設計上無可見變化

**階段 B — Engine 資料鏈**

5. [x] `DatabaseSchema.json` 加 `ModbusPoints.DeviceGroup`（nvarchar 100 Nullable；schema-sync 於 Engine/Web 啟動時 `ALTER TABLE ADD` 自動補，已確認 `SyncMissingColumnsAsync` 涵蓋此 nullable 欄）
6. [x] `ModbusTagModel` 加 `szDevice`、`ModbusPointModel` 加 `szDeviceGroup` + `ResolveDeviceGroup` 靜態互斥規則
7. [x] `ModbusConfigService` 載入時帶入（多站號忽略）；`SqlServerDataRepository` INSERT / `GetAllModbusPointsAsync` SELECT 同步
8. [~] 驗證：**改以單元測試鎖規則**（`ResolveDeviceGroupTests` 10 項全過：決策 4 多站號一律 null + 決策 5 留白 null + trim + FromTag 帶入）+ 全 solution `dotnet build` 0 error。
   - ⚠️ **未跑實機重載 round-trip**：`ScadaEngineService` **正在運行**，會持續 delete-then-insert 同一張 `ModbusPoints`，再起一個 dev Engine 會兩邊搶寫、有資料風險 → 不做。真正的「重載後 DB 有 DeviceGroup」會在**下次服務重啟（部署）自然發生**（schema-sync 補欄位 + Engine 重建點位）

**階段 C — 分群生效（UX 改動）**

9. [x] `point-grouping.js` 加四級 fallback（`pointDeviceLabel`）+ 「未分群」桶盤點（`coordDeviceGroups`/`hasDeviceGroups`/`getDeviceGroup`）
10. [x] `renderDeviceList()` 展開條件改「多站號 **或** 單站號有 Device」互斥擇一（決策 4）— designer / logicflow / calcpoint 三支 picker 皆已加 Device 子選單 + 未分群桶 + 點位篩選 + selectDeviceGroupItem
11. [x] Step 1 加搜尋框（決策 7）— **designer 已加**（比對 Coordinator 名 / 站號子設備 / Device 名）；⚠️ **logicflow / calcpoint 待補**：其設備層渲染函式未參數化 keyword，補搜尋需較深重構且本機無法視覺驗，暫緩（主頁 designer 已覆蓋痛點）
12. [~] 驗證：`dotnet build` 0 error + Node harness（`stagec.js` 四級/分群盤點 **19 項全過**、`equiv.js` 階段 A 回歸 **266 項仍全過**）；四種情境（決策 3 表格）的實際畫面待服務重啟有 DeviceGroup 資料後人工看
    - **Controller/DTO 已補 `szDeviceGroup`**：`/Designer/Points`（designer/logicflow/calcpoint/eventlog 共用）、`/EnergyBaseline/api/points`、History `__historyConfig.points`
    - **Device 標籤已全頁一致**：designer / logicflow / calcpoint / eventlog / energy-baseline / history 的 enrich 都改走 `pointDeviceLabel`（四級鏈）
    - ⚠️ **暫緩（groundwork 已備，屬 picker 展開 UI 或周邊頁）**：eventlog 設備層 Device 展開（標籤已 Device-aware）；B型（alarmsetting/conditionctrl）與 C型（watermeter/gasmeter/energymeter/chilledwater）的 Device 分群顯示

**階段 D — 讓現場能填**

13. [x] `/ModbusCoordinator` 點位熱編輯 Modal 加「子設備」欄 + DTO + `ModbusConfigFileService` 讀寫/變更偵測同步；多站號 disable（決策 4 防呆）
14. [~] 驗證：程式路徑打通 + build 0 error。**未跑實機**（prod `ScadaEngineService` 運行中，實測會改動線上 Modbus.json + 觸發線上 Engine 重載）→ 熱編輯免重啟機制本就存在（其他欄位既有），Device 只是加進同一 round-trip，走既有 watcher 生效
15. [ ] `.xlsm` 巨集加 Device 欄輸出 — **待使用者**（VBA 無法程式化編輯；提供巨集修改指引，或使用者貼巨集碼代改）

**階段 E — 收尾**

16. [x] i18n resx（zh-TW + en）：`modbuscoordinator.points.{col_device,device_multi_hint,device_na}`、`designer.picker.device_search_placeholder`（其餘 ungrouped 等沿用既有 key）；en `col_device`=Sub Device 對齊 glossary
17. [x] 文件同步：`docs/功能說明書_Modbus來源管理.md`（新增 §站號內子設備分群）、`ScadaEngine.Web/CLAUDE.md`（point-grouping.js 單一真相）；glossary 既有「子設備/未分組」沿用
18. [x] `dotnet test` 全綠：**553 通過 / 0 失敗**（含新增 `ResolveDeviceGroupTests` 10 項）

---

## 已知風險 / 待釐清

- ✅ ~~決策 4（複合標籤 vs 第四層）~~ —— 2026-09-18 使用者確認「多站號時每個站號就是一個設備」，混合情境不存在，改為互斥規則（見決策 4）
- ❓ **E2E（Playwright）要不要補？** 依專案規則由本 plan 的 flag 決定，見測試策略段
- ⚠️ **決策 4 的互斥前提是現場慣例而非系統強制**：若未來出現「多站號且站號內要再分設備」的現場，需回頭重新設計（屆時才評估複合標籤或第四層）。本次的防呆設計（UI disable + 解析層靜默忽略）確保該情境下行為明確、不會壞掉，只是不支援
- ⚠️ **`.xlsm` 巨集**：不同步改的話，現場重產設定檔會讓分群整批消失。這是本方案最容易漏掉的一環
- ⚠️ **本機無法跑 dev Web**（埠 5038 被正式站佔用，`--urls` 無效）→ 前端驗證方式需先確定：改用 `mock-render` skill 離線截圖，或直接在生產站台驗證（風險較高）
- ⚠️ **Razor views 是 precompiled**，改 `.cshtml` 必須 `dotnet build` 才生效
- ⚠️ **階段 A 是純重構但觸及 12+ 個檔案**，是本次最可能引入 regression 的一段。必須逐頁比對，不可整批改完才驗
- ⚠️ 調查時發現 `Modbus2` 的 `DeviceName` 是 `"1號系統,2號系統"` 但 `ModbusID` 只有 `"1"`（多出一個沒用到的名字）。共用層要能容忍**逗號數不對齊**兩個方向的情況

---

## 測試策略

- [ ] **本次是否動到核心計算 / 關鍵邏輯？**
  - [x] 是 → 補**單元測試**，要鎖住的規則：
    - SID 前綴 ↔ `(CoordinatorId, 站號)` 的互轉（`CoordinatorId*65536 + 站號*256 + 1`）
    - 四級 fallback 鏈的優先序與邊界（Device 空白 / DeviceName 逗號數不足 / 逗號數過多 / 全空）
  - ⚠️ 共用層是前端 JS，`ScadaEngine.Tests` 為 dotnet test —— **前端單元測試的載體需動工前確認**（目前專案無 JS 測試框架）；Engine 側的 JSON → DB 投影可在 dotnet test 內覆蓋
- [ ] 是否動到 DB 查詢 / 寫入或跨模組資料流？ **是** → `Modbus.json` → `ModbusPoints.DeviceGroup` 的整合測試（含重載後不遺失）
- [x] 是否動到關鍵使用者路徑？ **是** → E2E（Playwright）：**☑ 跳過**（2026-09-18 使用者決定）
  - 理由：階段 A 為行為等價的純重構，逐頁人工比對足以抓到 regression；且本機 dev Web 跑不起（5038 被正式站佔用，`--urls` 無效），補 E2E 需先解環境問題，代價與效益不成比例。待站台環境理順後再議
- [ ] 手動驗證（瀏覽器操作）：四種 Coordinator 情境 × 分群顯示；未分群桶點數正確；熱編輯存檔後免重啟生效
- [ ] 收尾：`dotnet test` 全綠

---

## 文件同步

- [ ] 更新 `docs/功能說明書_Modbus來源管理.md` — Tag.Device 欄位、分群規則、熱編輯新欄位
- [ ] 更新 `docs/架構.md` — §資料流 / §資料表用途對照（`ModbusPoints.DeviceGroup`）、四種來源分群對照表
- [ ] 若 `point-grouping.js` 確立為新共用模式 → 更新 `ScadaEngine.Web/CLAUDE.md`
- [ ] `docs/i18n-glossary.md` — 「分群」「未分群」等新詞先進 glossary 再用

---

## 進度日誌

### 2026-09-18（對話 1，收尾）
- 完成調查與 plan 撰寫；決策 4 依使用者回覆改為「站號 / Device 互斥」；E2E 定案跳過
- **尚未動工**，實作從階段 A 步驟 1 開始
- ⚠️ 本檔依 `docs/plans/.gitignore` 的 `*.md` 本應排除，為了跨裝置接手以 `git add -f` 強制納入版控
- **換裝置接手提示**：先讀本 plan 的「關鍵設計決策」7 條再動工；階段 A 是行為等價的純重構，逐頁比對不可省

### 2026-09-18（對話 2）— 階段 A 完成（純重構），待驗證
- **調查修正**：原 plan 假設「5 份相同複製」，實際盤點為 **3~4 種資料形狀**：
  - **A型**（前端 SID 數學，Hungarian 欄位）：designer/picker、logicflow/picker、calcpoint、eventlog、energy-baseline
  - **B型**（camelCase coord，Razor 注入，用 `Math.floor(((num-1)%65536)/256)` 直接算術而非 range-scan）：alarmsetting、conditionctrl
  - **C型**（後端 `/api/sids` 已回傳 `deviceName`，前端**無任何 SID 數學**）：watermeter、gasmeter、energymeter、chilledwater
  - **混合**：history（coord Hungarian + point camelCase）
- **共用層 `point-grouping.js`**：只收斂「算」的部分（`parseCoord` 吸收兩種欄位命名 + SID 判斷 + `subOfSid` 65536/256 定位）；顯示用預設標籤/i18n 各頁自理 → 行為等價
- **改走共用層**：8 個 A/B/混合檔（見檔案異動清單勾選）；C型 4 檔無 SID 數學可收斂，階段 A 不動；row-template/prop-panel 確認無此邏輯
- **刻意保留的各頁差異**（維持等價，不在階段 A 統一，留待階段 C 的四級 fallback 再收斂）：logicflow sub 列 fallback 退 `String(mid)`、eventlog 找不到設備留 `''` 不退 groupName、energy-baseline `modbusSubIdOf` 單站號也回 mid（範圍語意）
- **驗證**：`dotnet build ScadaEngine.Web` 全綠（0 error）+ **Node 等價性 harness 266 項全過**（舊內嵌 vs 新共用層位元相同）。
  - ⚠️ **修正舊假設**：使用者把 `ScadaWebService` 停掉後 5038/7189 即釋放，dev Web **跑得起來**（我實測啟動成功、根路徑正常導向 /Login，DB 連線正常）。原 plan「本機 dev Web 跑不起」不成立 —— 只要使用者先停正式服務即可
  - 未用 Playwright 逐頁點：登入需 DB 真實帳號（admin/admin 後門已於 SEC-08 移除），在生產機上不宜索取/使用真實密碼瀏覽正式資料；且純函式等價已由 harness 嚴格證明，殘留僅「瀏覽器內實跑」一項，需使用者登入才覆蓋
- **未做（等使用者驗證階段 A 後再進）**：階段 B（Engine 資料鏈）、C（分群生效 UX）、D（熱編輯 + xlsm）、E（i18n + 文件）
- ⚠️ 本階段**未 commit**（依 CLAUDE.md，等使用者驗證後才進 commit 流程）

### 2026-09-18（對話 2）— 階段 B 完成（Engine 資料鏈）
- **DeviceGroup 資料鏈打通**：`DatabaseSchema.json` 加欄位 → `ModbusTagModel.szDevice`（讀 JSON `Device`）→ `ResolveDeviceGroup` 決策 4 互斥 → `ModbusPointModel.szDeviceGroup` → Repository INSERT/SELECT
- **決策 4 落在資料層**：`InsertTagsToModbusPointsAsync` 對多站號 Coordinator 一律寫 null，抽成 `ModbusPointModel.ResolveDeviceGroup` 靜態方法當單一真相（階段 D 熱編輯寫回會共用，不重複實作互斥規則）
- **JSON 欄位確認**：現場 `Modbus.json` 為 **UTF-16 LE、PascalCase**（`"Name"`/`"Device"`），`tagJson.Device`（Newtonsoft dynamic）對得上
- **驗證**：全 solution build 0 error；新增 `ScadaEngine.Tests/Modbus/ResolveDeviceGroupTests.cs` **10 項全過**；schema-sync（`DatabaseInitializationService.SyncMissingColumnsAsync`）確認會 `ALTER TABLE ModbusPoints ADD DeviceGroup ... NULL`
- **刻意未做**：實機重載 round-trip（prod `ScadaEngineService` 運行中，不併跑第二個 Engine 搶寫）→ 部署重啟時自然生效
- **未 commit**（等使用者驗證）

### 2026-09-18（對話 2）— 階段 C 核心完成（分群生效）
- **共用層四級鏈**：`pointDeviceLabel`（Device→站號子設備→Coordinator 名）+ `coordDeviceGroups`/`hasDeviceGroups`/`getDeviceGroup`/`pointSid`；單站號才盤點 Device、多站號回 null（決策 4 互斥）
- **三支 picker 加 Device 分群**：designer（含 Step1 搜尋）、logicflow、calcpoint —— 單站號有 Device 的 Coordinator 變可展開子選單 + 「未分群」桶 + 點位依 Device 篩 + `selectDeviceGroupItem`/`ppSelectDeviceGroup`/`pkSelectDeviceGroup` + 狀態 reset
- **Device 標籤全頁一致**：6 頁 enrich 改走 `pointDeviceLabel`；Controller/DTO 補 `szDeviceGroup`（/Designer/Points、/EnergyBaseline/api/points、History config）
- **驗證**：Web build 0 error；`stagec.js` 19 項全過（四級鏈 + 分群盤點 + 排序 + 未分群桶邊界）；`equiv.js` 階段 A 回歸 266 項仍全過
- **暫緩（transparent，非漏做）**：logicflow/calcpoint Step1 搜尋（渲染函式未參數化，需較深重構且無法視覺驗）；eventlog 設備層 Device 展開（標籤已 Device-aware）；B型/C型 頁面 Device 分群
- ⚠️ 實際分群畫面需**服務重啟**讓 DeviceGroup 進 DB 後才看得到（現 live DB 尚無此欄/資料）
- **未 commit**（等使用者驗證）

### 2026-09-18（對話 2）— 追加：歷史資料查詢 + 即時數據頁 Device 分群（使用者要求）
- **History/Trend**：點位選擇側欄的單站號 Coordinator 若有 Device 分群 → 改渲染可展開子選單（Device 子設備 + 未分群桶）；`renderPointList` 加 `szDeviceGroup` 篩選維度、`applySelectionFromEl` 讀 `data-devicegroup`；`__historyConfig.points` 已帶 `deviceGroup`（前一批）
- **Realtime（/RealTime）**：同型側欄展開；`RealtimeMonitorViewModel.DeviceGroupMap`（SID→Device，Controller 由 GetAllModbusPointsAsync 建）→ 前端 `_realtimeDeviceGroupMap`；`filterByCoordinator` 加 Device 分群分支、`applySidebarSelection` 讀 `data-devicegroup`
- 兩頁的「未分群桶」判定：History 用全 PointList、Realtime 用 RealtimeDataList 內「不在 DeviceGroupMap 的點」
- **驗證**：Web build 0 error；`dotnet test` 553 全綠（過程遇一次 EnergyReportExcelExporter.cs 的並行編輯 race，非本次改動，該檔恢復後即綠）
- DeviceGroup 欄未進 DB 前，兩頁側欄自然維持原「直接可點」（map/欄空 → 不展開）

### 2026-09-18（對話 2）— 階段 D + E（收尾）
- **階段 D**：熱編輯 Modal 加「子設備」欄，`ModbusPointDto.Device` + `ModbusConfigFileService` 讀/寫/BuildChangeSummary 同步（走既有原子寫檔 + watcher 重載，免重啟）；多站號 Coordinator 的 Device 欄 disable + 提示（決策 4 防呆）
- **階段 E**：i18n resx（4 新 key，zh/en 同步，en 對齊 glossary）；文件同步（Modbus 來源管理新增 §站號內子設備分群、Web CLAUDE.md 記 point-grouping.js）；`dotnet test` **553/0**
- **.xlsm 待使用者**：VBA 無法程式化改，提供巨集修改指引
- **整體驗證**：全 solution build 0 error；Node harness `equiv.js` 266 + `stagec.js` 19 全過；dotnet test 553 全過
- **未 commit**（等使用者驗證後才進 commit 流程）

## 討論過程與成本記錄

### 討論過程摘要

1. **使用者初始提問**：「用腳本產生一份由設備名稱排列的點位清單是否可行？找不到歸納設備就放其他」
   - 調查發現 `MqttControlSubscribeService.cs:377` 記載 SID 格式 `CoordinatorId*65536 + 站號*256 + 1`，**可逆**；`ModbusCoordinator.DeviceName` 為逗號分隔、與 `ModbusID` 一一對應
   - 連實機 DB（`wsnCsharp`）跑 PoC：77 點全數正確歸入 9 台設備，**0 筆落入「其他」**
   - → 結論修正：設備歸屬**不需要猜**，是純查表；「其他」桶幾乎不會觸發

2. **使用者澄清用途**：不是產文件，而是「現場設定的人去點位清單找，設備排後面要滾很久」；且真正的難題是「**一顆 PLC 放五種設備、每設備五個點**」（單站號內的子設備）
   - → 問題性質從「產清單」轉為「點位選擇器 UX」
   - → 發現 OPC UA 的 `Devices` 分組是同一問題的既有解；四種來源只有 Modbus 缺這層
   - → 發現 `ModbusPoints` 是 delete-then-insert 從 JSON 重建 → 確立決策 1（主權在 JSON）

3. **使用者問「可以套用到沒有這模式的地方嗎？可以留白嗎？」**
   - 調查發現解析邏輯被複製 5+ 份且 fallback 不一致 → 新增決策 6（先抽共用層再改功能）
   - 留白 → 確立決策 5（四級 fallback + 可見的「未分群」桶）
   - 另發現搜尋框只在 Step 2、Step 1 沒有 → 新增決策 7

4. **使用者拍板**：一次全改、要寫 plan；並追問「原本分 ModbusCoordinator，有 Device 後會變怎樣？」
   - → 確立決策 3（層級不變，只改展開條件）與決策 4（混合情境用複合標籤，標為待確認）

5. **使用者確認「多站號不用，通常每個站號就是一個設備了」**
   - → 決策 4 從「複合標籤 vs 第四層」的取捨，簡化為**互斥規則**：多站號走站號、單站號才走 Device，永不疊加
   - → 連帶簡化：無複合標籤、無第四層、共用層少一個分支；決策 7 的理由同步修正（Step 1 項目數不會因 Device 暴增）
   - → 補上防呆設計（多站號時 UI disable Device 欄 + 解析層靜默忽略），確保誤填時行為明確而非未定義

### 成本記錄

| 階段 | 模型 | Input | Output | Cache Read | Cache Write | 備註 |
|------|------|-------|--------|------------|-------------|------|
| Plan 撰寫（調查 + 討論，截至寫檔前） | claude-opus-5 | 52 | 25,667 | 1,936,690 | 245,299 | 28 則 assistant 訊息 |
| **對話 1 收尾（plan commit 前）** | claude-opus-5 | **80** | **48,455** | **4,002,241** | **273,138** | **累計值**（非增量），44 則訊息。含決策 4 修訂、E2E 定案 |
| 實作（commit 後追記，可多列） | | | | | | |

> 取得方式：解析 session transcript `a770f170-3353-4b85-b285-bb2283ac7db1.jsonl`，依 `message.id` 去重後加總 `message.usage`。
> ⚠️ 第二列為**本次 commit 動作之前**的累計，commit / push 本身與其後往返的 token 於下個對話追記時補上。
> ⚠️ 下次接手時請注意：本 plan 跨對話，續作對話的用量需**另行取該對話的 transcript**，不可與上表直接相加（不同 session 檔）。

### Archive 總結（搬進 _archive 前回填）

- **總 token**：input __ / output __ / cache read __ / cache write __
- **模型**：
- **API 費用（當下牌價，附計算式）**：牌價查詢日 YYYY-MM-DD，來源 __
  - 計算式：

## 完成後補充

### 實際做法 vs 原計畫差異
-

### 踩到的雷
-

### 對 memory / CLAUDE.md 的更新建議
-
