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
    /// 儲存即時資料至資料庫
    /// </summary>
    /// <param name="realtimeDataList">即時資料清單</param>
    /// <returns>成功儲存的資料筆數</returns>
    Task<int> SaveRealtimeDataAsync(IEnumerable<RealtimeDataModel> realtimeDataList);

    /// <summary>
    /// 儲存單筆即時資料至資料庫
    /// </summary>
    /// <param name="realtimeData">即時資料</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveRealtimeDataAsync(RealtimeDataModel realtimeData);

    /// <summary>
    /// 儲存設備配置至資料庫
    /// </summary>
    /// <param name="deviceConfig">設備配置</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveConfigAsync(ModbusDeviceConfigModel deviceConfig);

    /// <summary>
    /// 查詢指定時間範圍的歷史資料
    /// </summary>
    /// <param name="szSID">點位識別碼</param>
    /// <param name="dtStartTime">開始時間</param>
    /// <param name="dtEndTime">結束時間</param>
    /// <returns>歷史資料清單</returns>
    Task<IEnumerable<RealtimeDataModel>> GetHistoryDataAsync(string szSID, DateTime dtStartTime, DateTime dtEndTime);

    /// <summary>
    /// 取得所有設備配置
    /// </summary>
    /// <returns>設備配置清單</returns>
    Task<IEnumerable<ModbusDeviceConfigModel>> GetDeviceConfigsAsync();

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
    /// 釋放資源
    /// </summary>
    void Dispose();
}