using System.Text.Json.Serialization;

namespace ScadaEngine.Common.Data.Models;

/// <summary>
/// 資料庫配置模型，對應 Setting/dbSetting.json 檔案結構
/// </summary>
public class DatabaseConfigModel
{
    /// <summary>
    /// 資料庫伺服器 IP 或主機名稱
    /// </summary>
    [JsonPropertyName("DatabaseAddress")]
    public string szDatabaseAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    [JsonPropertyName("DataBaseName")]
    public string szDataBaseName { get; set; } = "wsnCsharp";

    /// <summary>
    /// 登入帳號
    /// </summary>
    [JsonPropertyName("DataBaseAccount")]
    public string szDataBaseAccount { get; set; } = "wsn";

    /// <summary>
    /// 登入密碼 (敏感資料)
    /// </summary>
    [JsonPropertyName("DataBasePassword")]
    public string szDataBasePassword { get; set; } = "wsn";

    /// <summary>
    /// 建構完整的 SQL Server 連線字串
    /// </summary>
    /// <returns>SQL Server 連線字串</returns>
    public string BuildConnectionString()
    {
        return $"Server={szDatabaseAddress};Database={szDataBaseName};User Id={szDataBaseAccount};Password={szDataBasePassword};TrustServerCertificate=true;";
    }

    /// <summary>
    /// 驗證資料庫配置的有效性
    /// </summary>
    /// <returns>配置有效回傳 true，無效回傳 false</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(szDatabaseAddress) &&
               !string.IsNullOrWhiteSpace(szDataBaseName) &&
               !string.IsNullOrWhiteSpace(szDataBaseAccount) &&
               !string.IsNullOrWhiteSpace(szDataBasePassword);
    }
}