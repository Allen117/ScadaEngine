using System.ComponentModel;
using ScadaEngine.Engine.Models;

namespace ScadaEngine.Web.Features.Realtime.Models;

/// <summary>
/// 即時監控視圖模型
/// </summary>
public class RealtimeMonitorViewModel
{
    /// <summary>
    /// 最後更新時間
    /// </summary>
    [DisplayName("最後更新時間")]
    public DateTime dtLastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// 連線狀態
    /// </summary>
    [DisplayName("MQTT 連線狀態")]
    public bool isConnectionHealthy { get; set; }

    /// <summary>
    /// 即時資料清單
    /// </summary>
    public List<RealtimeDataItemModel> RealtimeDataList { get; set; } = new();

    /// <summary>
    /// 總點位數量
    /// </summary>
    [DisplayName("總點位數量")]
    public int nTotalPoints { get; set; }

    /// <summary>
    /// 活躍點位數量
    /// </summary>
    [DisplayName("活躍點位")]
    public int nActivePoints { get; set; }

    /// <summary>
    /// 錯誤訊息
    /// </summary>
    [DisplayName("錯誤訊息")]
    public string szErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// ModbusCoordinator 設備清單 (左側邊欄用)
    /// </summary>
    public List<CoordinatorModel> CoordinatorList { get; set; } = [];
}

/// <summary>
/// 單一即時資料項目模型
/// </summary>
public class RealtimeDataItemModel
{
    /// <summary>
    /// 主題 (子路徑)
    /// </summary>
    [DisplayName("主題")]
    public string szSubTopic { get; set; } = string.Empty;

    /// <summary>
    /// SID (點位識別碼)
    /// </summary>
    [DisplayName("點位ID")]
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 點位名稱
    /// </summary>
    [DisplayName("點位名稱")]
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// 數值
    /// </summary>
    [DisplayName("數值")]
    public double dValue { get; set; }

    /// <summary>
    /// 品質狀態
    /// </summary>
    [DisplayName("品質")]
    public string szQuality { get; set; } = string.Empty;

    /// <summary>
    /// 時間戳記
    /// </summary>
    [DisplayName("時間戳記")]
    public DateTime dtTimestamp { get; set; }

    /// <summary>
    /// 單位
    /// </summary>
    [DisplayName("單位")]
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 資料品質是否正常
    /// </summary>
    public bool isQualityGood => szQuality.Equals("GOOD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 資料是否為最新 (1分鐘內)
    /// </summary>
    public bool isRecent => DateTime.Now.Subtract(dtTimestamp).TotalMinutes <= 1;

    /// <summary>
    /// CSS 樣式類別
    /// </summary>
    public string CssRowClass => GetCssRowClass();

    /// <summary>
    /// 品質徽章樣式類別
    /// </summary>
    public string QualityBadgeClass => GetQualityBadgeClass();

    /// <summary>
    /// 是否尚未收到任何 MQTT 資料 (點位已設定但無數值)
    /// </summary>
    public bool hasData { get; set; } = true;

    /// <summary>
    /// 取得 CSS 列樣式類別
    /// </summary>
    public string GetCssRowClass()
    {
        if (!hasData) return "row-nodata";
        if (!isQualityGood) return "row-error table-danger";
        if (!isRecent) return "row-outdated table-warning";
        return "row-recent";
    }

    /// <summary>
    /// 取得品質徽章樣式類別
    /// </summary>
    public string GetQualityBadgeClass()
    {
        return szQuality.ToUpper() switch
        {
            "GOOD" => "bg-success",
            "UNCERTAIN" => "bg-warning",
            "BAD" => "bg-danger",
            "NO_DATA" => "bg-secondary",
            _ => "bg-secondary"
        };
    }
}