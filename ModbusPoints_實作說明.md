# ModbusPoints 資料庫整合功能實作說明

## 功能概述

本次實作完成了將 Modbus coordinator JSON 設定檔中的點位資料自動儲存到 `ModbusPoints` 資料表的功能。

## 主要實作內容

### 1. 新增 `ModbusPointModel` 類別
- **檔案位置**: `Communication/Modbus/Models/ModbusPointModel.cs`
- **功能**: 對應 `ModbusPoints` 資料表的模型類別
- **主要屬性**:
  - `szSID`: 點位唯一識別碼 (主鍵)
  - `szName`: 點位名稱
  - `nAddress`: Modbus 暫存器地址
  - `szDataType`: 資料型態
  - `fRatio`: 數值縮放比例
  - `szUnit`: 物理單位
  - `fMin`, `fMax`: 最小值和最大值 (可選)

### 2. 擴展 `SqlServerDataRepository` 類別
- **新增方法**:
  - `SaveModbusPointsAsync()`: 批量儲存點位到資料庫
  - `GetModbusPointsByCoordinatorAsync()`: 查詢指定 Coordinator 的點位

### 3. 修改 `ModbusConfigService` 類別
- **更新 `GenerateTagSIDsAsync()` 方法**: 在取得 `nDatabaseId` 後自動插入點位資料
- **新增 `InsertTagsToModbusPointsAsync()` 方法**: 處理點位資料的插入邏輯

## 資料庫操作策略

### 使用「先刪後插入」(Delete + Insert) 策略
**優點**:
1. **資料一致性保證**: 每次更新都會確保資料表中的資料與設定檔完全一致
2. **處理點位變更**: 自動處理點位新增、刪除、修改等情況
3. **避免重複資料**: 不會產生重複的點位記錄
4. **交易完整性**: 使用資料庫交易確保操作原子性

**實作細節**:
```csharp
// Step 1: 刪除該 Coordinator 的所有舊點位
DELETE FROM ModbusPoints WHERE SID LIKE 'CoordinatorId-%'

// Step 2: 批量插入新點位
INSERT INTO ModbusPoints (SID, Name, Address, DataType, Ratio, Unit, Min, Max)
VALUES (...)
```

## SID 生成規則

按照系統設計文件的規範：
```
SID = DatabaseId * 65536 + ModbusId * 256 + TagIndex + 1
```

- **DatabaseId**: Coordinator 在資料庫中的 ID
- **ModbusId**: Modbus 設備站號 (支援多站號用逗號分隔)
- **TagIndex**: 點位在 Tags 清單中的索引 (從 0 開始)

## 多站號支援

系統支援單一設定檔包含多個 ModbusId 的情況：
- 設定檔中 `ModbusId` 可以是 `"1,2,3"` 格式
- 系統會為每個站號的每個點位都生成對應的 ModbusPoint 記錄

## 錯誤處理機制

1. **交易回滾**: 插入過程中發生錯誤會自動回滾，確保資料一致性
2. **點位驗證**: 每個點位都會經過 `Validate()` 檢查，無效點位會被跳過
3. **詳細日誌**: 記錄完整的操作過程，便於除錯

## 使用時機

此功能會在以下時機自動執行：
1. **系統啟動**: 載入所有 Modbus 設定檔時
2. **設定檔變更**: 檔案監控器偵測到設定檔修改時
3. **手動重載**: 呼叫 `ReloadDeviceConfigAsync()` 時

## 性能考量

- **批量操作**: 使用批量插入提升效率
- **並行載入**: 多個設定檔並行處理
- **索引優化**: SID 作為主鍵，查詢效率佳

## 後續建議

1. **定期清理**: 可考慮實作清理無效或過期點位的機制
2. **增量更新**: 未來可考慮實作增量更新邏輯以提升性能
3. **監控機制**: 建議增加點位資料同步狀態的監控功能