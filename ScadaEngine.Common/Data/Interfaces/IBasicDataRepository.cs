using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Common.Data.Interfaces;

/// <summary>
/// 基礎資料存取介面，定義共用的資料庫操作方法
/// 此介面為 SCADA 系統核心資料存取的標準定義
/// </summary>
public interface IBasicDataRepository
{
    /// <summary>
    /// 儲存即時資料至資料庫（單筆）
    /// </summary>
    /// <param name="realtimeData">即時資料模型</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveRealtimeDataAsync(RealtimeDataModel realtimeData);

    /// <summary>
    /// 批量儲存即時資料至資料庫
    /// </summary>
    /// <param name="realtimeDataList">即時資料清單</param>
    /// <returns>成功儲存的筆數</returns>
    Task<int> SaveRealtimeDataAsync(IEnumerable<RealtimeDataModel> realtimeDataList);

    /// <summary>
    /// 儲存歷史資料至資料庫（單筆）
    /// </summary>
    /// <param name="historyData">歷史資料模型</param>
    /// <returns>儲存成功回傳 true，失敗回傳 false</returns>
    Task<bool> SaveHistoryDataAsync(HistoryDataModel historyData);

    /// <summary>
    /// 批量儲存歷史資料至資料庫
    /// </summary>
    /// <param name="historyDataList">歷史資料清單</param>
    /// <returns>成功儲存的筆數</returns>
    Task<int> SaveHistoryDataAsync(IEnumerable<HistoryDataModel> historyDataList);

    /// <summary>
    /// 更新或插入最新資料（Upsert）
    /// </summary>
    /// <param name="latestData">最新資料模型</param>
    /// <returns>更新成功回傳 true，失敗回傳 false</returns>
    Task<bool> UpsertLatestDataAsync(LatestDataModel latestData);

    /// <summary>
    /// 根據 SID 取得最新資料
    /// </summary>
    /// <param name="szSID">點位系統識別碼</param>
    /// <returns>最新資料模型，若未找到則回傳 null</returns>
    Task<LatestDataModel?> GetLatestDataAsync(string szSID);

    /// <summary>
    /// 取得指定時間範圍的歷史資料
    /// </summary>
    /// <param name="szSID">點位系統識別碼</param>
    /// <param name="dtStartTime">起始時間</param>
    /// <param name="dtEndTime">結束時間</param>
    /// <returns>歷史資料清單</returns>
    Task<IEnumerable<HistoryDataModel>> GetHistoryDataAsync(string szSID, DateTime dtStartTime, DateTime dtEndTime);

    /// <summary>
    /// 檢查資料庫連線狀態
    /// </summary>
    /// <returns>連線正常回傳 true，異常回傳 false</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// 取得資料庫配置資訊
    /// </summary>
    /// <returns>資料庫配置模型</returns>
    Task<DatabaseConfigModel> GetDatabaseConfigAsync();
}