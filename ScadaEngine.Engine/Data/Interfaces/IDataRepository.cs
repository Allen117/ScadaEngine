using ScadaEngine.Engine.Communication.Modbus.Models;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Engine.Data.Interfaces;

/// <summary>
/// 資料持久化存取介面，定義資料庫操作標準
/// </summary>
public interface IDataRepository
{
    /// <summary>
    /// 測試資料庫連線是否正常
    /// </summary>
    /// <returns>連線正常回傳 true，失敗回傳 false</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// 查詢指定時間範圍的歷史資料
    /// </summary>
    /// <param name="szSID">點位識別碼</param>
    /// <param name="dtStartTime">開始時間</param>
    /// <param name="dtEndTime">結束時間</param>
    /// <returns>歷史資料清單</returns>
    Task<IEnumerable<RealtimeDataModel>> GetHistoryDataAsync(string szSID, DateTime dtStartTime, DateTime dtEndTime);

    /// <summary>
    /// 儲存歷史資料至 HistoryData 資料表
    /// </summary>
    /// <param name="historyDataList">歷史資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    Task<int> SaveHistoryDataAsync(IEnumerable<HistoryDataModel> historyDataList);

    /// <summary>
    /// 儲存單筆歷史資料至 HistoryData 資料表
    /// </summary>
    /// <param name="historyData">歷史資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveHistoryDataAsync(HistoryDataModel historyData);

    /// <summary>
    /// 儲存最新資料至 LatestData 資料表 (INSERT or UPDATE)
    /// </summary>
    /// <param name="latestDataList">最新資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    Task<int> SaveLatestDataAsync(IEnumerable<LatestDataModel> latestDataList);

    /// <summary>
    /// 儲存單筆最新資料至 LatestData 資料表 (INSERT or UPDATE)
    /// </summary>
    /// <param name="latestData">最新資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveLatestDataAsync(LatestDataModel latestData);

    /// <summary>
    /// 取得最新的即時資料 (用於Web監控介面)
    /// </summary>
    /// <param name="nLimit">限制筆數</param>
    /// <returns>最新資料清單</returns>
    Task<IEnumerable<LatestDataModel>> GetLatestDataAsync(int nLimit = 100);

    /// <summary>
    /// 取得最新資料的時間戳記
    /// </summary>
    /// <returns>最新時間戳記</returns>
    Task<DateTime> GetLatestTimestampAsync();

    /// <summary>
    /// 取得總點位數量
    /// </summary>
    /// <returns>點位總數</returns>
    Task<int> GetTotalTagCountAsync();

    /// <summary>
    /// 取得 Users 資料表中的使用者數量
    /// </summary>
    /// <returns>使用者總數</returns>
    Task<int> GetUserCountAsync();

    /// <summary>
    /// 取得 Users 資料表中 Admin 角色的使用者數量
    /// </summary>
    /// <returns>Admin 角色使用者總數</returns>
    Task<int> GetAdminCountAsync();

    /// <summary>
    /// 驗證使用者帳號密碼 (密碼以 SHA256 hex 儲存於 PasswordHash 欄位)
    /// </summary>
    /// <param name="szUsername">使用者名稱</param>
    /// <param name="szPassword">明文密碼</param>
    /// <returns>驗證成功回傳 true</returns>
    Task<bool> ValidateUserAsync(string szUsername, string szPassword);

    /// <summary>
    /// 取得所有已設定的 ModbusPoints (用於Web初始化全點位清單)
    /// </summary>
    /// <returns>所有 ModbusPoint 清單</returns>
    Task<IEnumerable<ModbusPointModel>> GetAllModbusPointsAsync();

    /// <summary>
    /// 取得 ModbusCoordinator 資料表的所有設備清單
    /// </summary>
    /// <returns>所有 Coordinator 清單</returns>
    Task<IEnumerable<CoordinatorModel>> GetAllCoordinatorsAsync();

    /// <summary>
    /// 更新 ModbusCoordinator 的 DeviceName 欄位
    /// </summary>
    /// <param name="nId">Coordinator Id</param>
    /// <param name="szDeviceName">裝置名稱（逗點分隔）</param>
    /// <returns>更新成功回傳 true</returns>
    Task<bool> UpdateDeviceNameAsync(int nId, string szDeviceName);

    /// <summary>
    /// 查詢 HistoryData 資料表中指定 SID 的歷史記錄
    /// </summary>
    /// <param name="szSID">點位識別碼</param>
    /// <param name="dtStartTime">開始時間</param>
    /// <param name="dtEndTime">結束時間</param>
    /// <param name="nMaxRecords">最大筆數上限，預設 5000</param>
    /// <returns>歷史資料清單 (時間升冪)</returns>
    Task<IEnumerable<HistoryDataModel>> GetHistoryTableDataAsync(string szSID, DateTime dtStartTime, DateTime dtEndTime, int nMaxRecords = 5000);

    /// <summary>
    /// 取得所有條件控制規則
    /// </summary>
    Task<IEnumerable<ConditionControlRuleModel>> GetAllConditionControlRulesAsync();

    /// <summary>
    /// 全量覆寫條件控制規則（先清空再批次插入）
    /// </summary>
    /// <param name="rules">規則清單</param>
    /// <returns>儲存成功回傳 true</returns>
    Task<bool> SaveConditionControlRulesAsync(IEnumerable<ConditionControlRuleModel> rules);

    /// <summary>
    /// 儲存畫面設計至資料庫（先將舊版 IsPublished 清零，再插入新版）
    /// </summary>
    /// <param name="szName">設計名稱</param>
    /// <param name="pages">頁面清單（已展平的樹狀結構）</param>
    /// <returns>儲存成功回傳 true</returns>
    Task<bool> SaveDesignAsync(string szName, IEnumerable<ScadaDesignPageModel> pages);

    /// <summary>
    /// 讀取已發布的畫面設計（IsPublished=1）的所有頁面
    /// </summary>
    /// <returns>頁面平坦清單；若無發布版本回傳空集合</returns>
    Task<IEnumerable<ScadaDesignPageModel>> LoadPublishedDesignAsync();

    /// <summary>
    /// 取得所有使用者帳號資料
    /// </summary>
    /// <returns>使用者清單</returns>
    Task<IEnumerable<UserModel>> GetAllUsersAsync();

    /// <summary>
    /// 依帳號名稱取得單一使用者（含 Role），登入時用
    /// </summary>
    Task<UserModel?> GetUserByUsernameAsync(string szUsername);

    /// <summary>
    /// 新增使用者帳號（密碼以 SHA256 hex 儲存）
    /// </summary>
    /// <param name="user">使用者資料（szPasswordHash 欄位傳入明文密碼，方法內自動雜湊）</param>
    /// <returns>新增成功回傳 true</returns>
    Task<bool> CreateUserAsync(UserModel user);

    /// <summary>
    /// 更新使用者帳號（不含密碼）
    /// </summary>
    Task<bool> UpdateUserAsync(UserModel user);

    /// <summary>
    /// 刪除使用者帳號（同時刪除 UserPermissions）
    /// </summary>
    Task<bool> DeleteUserAsync(int nUserID);

    /// <summary>
    /// 取得使用者權限 JSON
    /// </summary>
    Task<UserPermissionModel?> GetUserPermissionsAsync(int nUserID);

    /// <summary>
    /// 儲存使用者權限 JSON（UPSERT）
    /// </summary>
    Task<bool> SaveUserPermissionsAsync(int nUserID, string szPermissionJson);

    /// <summary>
    /// 更新使用者最後登入時間
    /// </summary>
    /// <param name="szUsername">使用者名稱</param>
    /// <returns>更新成功回傳 true</returns>
    Task<bool> UpdateLastLoginAsync(string szUsername);

    /// <summary>
    /// 儲存手動控制值（UPSERT ManualControlValue，IsAuto=0）
    /// </summary>
    /// <param name="szSid">點位 SID</param>
    /// <param name="dValue">手動控制值</param>
    /// <returns>儲存成功回傳 true</returns>
    Task<bool> SaveManualControlValueAsync(string szSid, double dValue);

    /// <summary>
    /// 將點位標記為自動控制（UPSERT ManualControlValue，IsAuto=1）
    /// </summary>
    /// <param name="szSid">點位 SID</param>
    /// <returns>儲存成功回傳 true</returns>
    Task<bool> SetAutoControlAsync(string szSid);

    /// <summary>
    /// 確保 ManualControlValue 存在該 SID 的記錄（不存在時 INSERT IsAuto=1, Value=0）
    /// 已存在的記錄不會被覆蓋。
    /// </summary>
    Task EnsureManualControlEntryExistsAsync(string szSid);

    /// <summary>
    /// 讀取所有手動控制值與自動模式狀態
    /// </summary>
    /// <returns>SID → (Value, IsAuto) 字典</returns>
    Task<Dictionary<string, (double dValue, bool isAuto)>> LoadManualControlValuesAsync();

    #region CalculatedPoints 計算點位

    /// <summary>
    /// 取得所有計算點位設定
    /// </summary>
    Task<IEnumerable<CalculatedPointModel>> GetAllCalculatedPointsAsync();

    /// <summary>
    /// 新增計算點位
    /// </summary>
    Task<bool> CreateCalculatedPointAsync(CalculatedPointModel model);

    /// <summary>
    /// 更新計算點位
    /// </summary>
    Task<bool> UpdateCalculatedPointAsync(CalculatedPointModel model);

    /// <summary>
    /// 刪除計算點位
    /// </summary>
    Task<bool> DeleteCalculatedPointAsync(string szSID);

    /// <summary>
    /// 取得計算點位數量（用於產生下一個 SID）
    /// </summary>
    Task<int> GetCalculatedPointMaxIndexAsync();

    #endregion

    /// <summary>
    /// 釋放資源
    /// </summary>
    void Dispose();
}