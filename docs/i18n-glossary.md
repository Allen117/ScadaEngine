# SCADA i18n 術語表（zh-TW ↔ en）

新詞翻譯前先查表。新增詞彙時於對應分類補列、保持字典序。

## 品牌 / 系統名

| zh-TW | en | 備註 |
|---|---|---|
| SCADA 監控系統 | SCADA Monitoring System | 主標題 |
| SCADA 智慧能源管理平台 | SCADA Smart Energy Platform | footer 主品牌 |
| SCADA | SCADA | brand，不翻 |
| ITRI | ITRI | logo，不翻 |

## 主功能 / 頁面（對應路由）

| zh-TW | en | 路由 / 備註 |
|---|---|---|
| 即時監控 | SCADA Page | `/ScadaPage` |
| 即時數據 | Realtime Data | `/RealTime` |
| 控制邏輯 | Control Logic | top-level menu |
| 條件控制 | Condition Control | `/ConditionCtrl` |
| 流程圖控制 | Logic Flow | `/LogicFlow` |
| 排程設定 | Schedule | `/ScheduleSetting` |
| 歷史資料 | History | top-level menu |
| 趨勢圖 | Trend | `/History/Trend` |
| 事件記錄 | Event Log | `/EventLog` |
| 能源管理 | Energy Management | top-level menu |
| 用電報表 | Energy Report | `/EnergyReport` |
| 電表/迴路設定 | Energy Meter / Circuit | `/EnergyMeter` |
| 系統設定 | System Settings | top-level menu |
| 畫面設計 | Designer | `/Designer` |
| 警報設定 | Alarm Settings | `/AlarmSetting` |
| 點位設定 | Point Settings | submenu |
| Modbus 來源 | Modbus Source | `/CommSetting` |
| DB 來源 | DB Source | `/DbCoordinator` |
| 計算點位 | Calculated Points | `/CalcPoint` |
| 帳號管理 | Account Management | `/AccountSetting` |
| 個人資料 | Profile | `/Account/Profile` |

## 領域名詞

| zh-TW | en | 備註 |
|---|---|---|
| 點位 | Point | 通用詞 |
| 設備 / 協調器 | Device / Coordinator | 設備層 |
| 迴路 | Circuit | 能源管理用，**不**翻成 Loop |
| 計算點 / 計算點位 | Calculated Point | 同義 |
| DB 來源 | DB Source | DBLatestData polling 系列 |
| Modbus 來源 | Modbus Source | Modbus TCP polling 系列 |
| 系統總覽 | System Overview | ScadaPage 樹 |

## 警報相關

| zh-TW | en | 備註 |
|---|---|---|
| 警報 | Alarm | 不用 alert |
| 警告 | Warning | EventType=2 |
| 故障 | Fault | EventType=1 |
| 資訊 | Info | EventType=3 |
| 系統 | System | EventType=4 |
| 觸發 | Triggered | 警報狀態 |
| 已恢復 | Cleared / Recovered | 警報狀態 |
| 未恢復 | Active | 警報狀態 |
| 嚴重程度 | Severity | 通用詞 |
| 緊急 | Critical | severity=0 |
| 高 | High | severity=1 |
| 中 | Medium | severity=2 |
| 低 | Low | severity=3 |
| 已確認 | Acknowledged | 警報已被人員確認 |
| 未確認 | Unack'd | 縮寫，UI 緊湊 |
| 確認警報 | Acknowledge | 動詞 |
| 觸發值 | Trigger Value | 警報觸發當下的數值 |
| 門檻值 | Threshold | 上下限值 |
| 條件 | Condition | 警報判定運算子 |
| 超過上限 | exceeds upper limit | high 警報訊息 |
| 低於下限 | below lower limit | low 警報訊息 |

## 數據品質

| zh-TW | en | 備註 |
|---|---|---|
| 品質 | Quality | Good/Bad |
| 良好 | Good | quality=1 |
| 不良 | Bad | quality=0 |

## 連線狀態

| zh-TW | en | 備註 |
|---|---|---|
| 連線中… | Connecting… | |
| 已連線 | Connected | |
| 已斷線 | Disconnected | |
| 連線正常 | Connected | Realtime 頁 |
| 連線異常 | Disconnected | Realtime 頁 |

## 通用 UI 詞彙

| zh-TW | en | 備註 |
|---|---|---|
| 儲存 | Save | 動詞祈使 |
| 儲存變更 | Save Changes | 動詞祈使 |
| 取消 | Cancel | 動詞祈使 |
| 確認 | Confirm | 動詞祈使 |
| 確定 | OK | 簡短確認 |
| 刪除 | Delete | 動詞祈使 |
| 確認刪除 | Confirm / Delete | 動詞祈使 |
| 新增 | Add | 動詞祈使 |
| 編輯 | Edit | 動詞祈使 |
| 查詢 | Query | 動詞祈使 |
| 匯出 | Export | 動詞祈使 |
| 匯入 | Import | 動詞祈使 |
| 重新整理 | Refresh | 動詞祈使 |
| 重設 | Reset | 動詞祈使 |
| 套用 | Apply | 動詞祈使 |
| 設定 | Settings | 名詞 |
| 載入中… | Loading… | 進行式 |
| 處理中 | Processing | 進行式 |
| 查詢中… | Querying… | 進行式 |
| 儲存中... | Saving... | 進行式 |
| 全選 | Select All | |
| 清除 | Clear | |
| 清除全部 | Clear All | |
| 搜尋 | Search | 動詞 |
| 篩選 | Filter | |
| 關閉 | Close | 動詞 |
| 展開 | Expand | 動詞 |
| 收合 | Collapse | 動詞 |

## 表單欄位

| zh-TW | en | 備註 |
|---|---|---|
| 名稱 | Name | |
| 帳號 | Username | |
| 姓名 | Name | （人名） |
| 密碼 | Password | |
| 確認密碼 | Confirm Password | |
| 角色 | Role | |
| 部門 | Department | |
| 狀態 | Status | |
| 啟用 | Active | |
| 停用 | Disabled | |
| 啟用帳號 | Account active | |
| 部門 | Department | |
| 操作 | Actions | 表格欄位 |
| 說明 / 備註 | Description / Remarks | |

## 時間 / 日期

| zh-TW | en | 備註 |
|---|---|---|
| 起始時間 / 開始時間 | Start Time | |
| 結束時間 | End Time | |
| 時間戳記 | Timestamp | |
| 發生時間 | Occurred At | EventLog |
| 恢復時間 | Cleared At | EventLog |
| 最後登入 | Last Login | |
| 建立時間 | Created At | |
| 最後更新 | Last update | footer |
| 伺服器時間 | Server Time | footer |
| 快速 | Quick | 快速時間範圍 |

## 用電報表 / 能源

| zh-TW | en | 備註 |
|---|---|---|
| 用電報表 | Energy Report | |
| 用電量 | Energy | (kWh) |
| 用電量分布 | Energy Consumption | chart 標題 |
| 數據明細 | Data Detail | 表格區塊 |
| 時段 | Period | 表格欄 |
| 粒度 / 單位 | Granularity | 時 / 日 / 月 / 年 |
| 時 | Hour | granularity |
| 日 | Day | granularity |
| 月 | Month | granularity |
| 年 | Year | granularity |
| 起月 | From Month | |
| 訖月 | To Month | |
| 起年 | From Year | |
| 訖年 | To Year | |
| 合計 | Total | |
| 總計 | Total | 與「合計」同義 |
| 查詢區間 | Query Range | Excel 標籤 |
| 操作者 | Operator | Excel 標籤（不是「比較運算子」） |

## 趨勢圖

| zh-TW | en | 備註 |
|---|---|---|
| 歷史趨勢圖 | Historical Trend | |
| 點位選取 | Select Points | |
| 待查詢清單 | Query List | |
| 加入待查詢清單 | Add to Query List | |
| 數值 | Value | |
| 最小值 | Min | |
| 最大值 | Max | |
| 平均值 | Avg | |
| 標準差 | Std Dev | |
| 筆數 | Count | |
| 折線 | Line | chart type |
| 柱狀 | Bar | chart type |
| 雙軸 | Dual | axis mode |
| 單軸 | Single | axis mode |
| 統計摘要 | Summary | Excel sheet 名 |

## 事件記錄

| zh-TW | en | 備註 |
|---|---|---|
| 事件記錄查詢 | Event Log Query | |
| 事件類型 | Event Type | |
| 嚴重程度 | Severity | |
| 確認狀態 | Ack Status | |
| 確認者 | Acked By | |
| 點位篩選 | Point Filter | |
| 未選擇 | None | placeholder |
| 全部 | All | filter |
| 發生中 | Active | cleared_at 為空 |
| 共 N 筆記錄 | N record(s) found | |

## 帳號管理

| zh-TW | en | 備註 |
|---|---|---|
| 使用者清單 | User List | |
| 共 N 筆 | N record(s) | |
| 新增使用者 | Add User | |
| 編輯使用者 | Edit User | |
| 確認登出 | Confirm Log Out | |
| 登入 | Log In | |
| 登出 | Log Out | |
| 個人資料 | Profile | |
| 頁面存取權限 | Page Access Permissions | |
| 主功能頁面 | Main Pages | |
| 即時監控子頁面 | SCADA Page Subpages | |
| 可檢視 | View | 表頭 |
| 可控制 | Control | 表頭 |
| 尚未登入 | Never logged in | |

## 警報觸發訊息（結構化 i18n key）

| key | zh-TW 模板 | en 模板 |
|---|---|---|
| `alarm.high_exceed` | `{0} 超過上限 {1}` | `{0} exceeds upper limit {1}` |
| `alarm.low_below` | `{0} 低於下限 {1}` | `{0} below lower limit {1}` |
| `alarm.di_triggered` | `{0} 狀態為 {1} 觸發警報` | `{0} state {1} triggered alarm` |

`{0}` = 點位名（user input，可能仍是中文）；`{1}` = threshold 或 state（state 為使用者自填的 DiOnLabel/DiOffLabel，可能仍是中文）。

## 翻譯原則

1. **動詞用祈使句**（Save / Cancel / Delete），不要 ing 形式
2. **縮寫用大寫**（OK / SCADA / DB），常見縮寫不翻
3. **進行式加省略號**（Loading… / Querying…），與 zh-TW `…` 對應
4. **品牌詞不翻**（SCADA、ITRI）
5. **領域名詞優先業界慣例**（Modbus、MQTT、SID 不翻）
6. **避免直譯**，例如「迴路」翻 Circuit 不翻 Loop（電力業界 Circuit）
7. **時間軸統一 ISO 8601**：`yyyy-MM-dd HH:mm:ss`，不依 culture 在地化
