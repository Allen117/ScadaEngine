using System.ComponentModel.DataAnnotations;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Engine.Communication.Modbus.Models;

/// <summary>
/// Modbus 點位標籤資料模型，封裝點位的完整屬性與物理量轉換邏輯
/// </summary>
public class ModbusTagModel
{
    /// <summary>
    /// 點位名稱代碼 (如 CH1RUN)
    /// </summary>
    public string szName { get; set; } = string.Empty;

    /// <summary>
    /// Modbus 暫存器地址 (5位數慣例格式，如 40001)
    /// </summary>
    public string szAddress { get; set; } = string.Empty;

    /// <summary>
    /// 資料型態 (Integer, FloatingPt, SwappedFP, Double)
    /// </summary>
    public string szDataType { get; set; } = string.Empty;

    /// <summary>
    /// 數值縮放比例，實際值 = 原始值 × Ratio
    /// </summary>
    public string szRatio { get; set; } = "1";

    /// <summary>
    /// 物理單位 (如 C, F, V, A)
    /// </summary>
    public string szUnit { get; set; } = string.Empty;

    /// <summary>
    /// 控制點位最大值
    /// </summary>
    public string szMax { get; set; } = string.Empty;

    /// <summary>
    /// 控制點位最小值
    /// </summary>
    public string szMin { get; set; } = string.Empty;

    /// <summary>
    /// 點位唯一識別碼 (SID)，格式為 XXX-SN
    /// </summary>
    public string szSID { get; set; } = string.Empty;

    /// <summary>
    /// 解析後的實際 Modbus 地址 (0-based，已扣除前綴)
    /// </summary>
    public int nParsedAddress { get; private set; }

    /// <summary>
    /// 解析後的縮放比例
    /// </summary>
    public float fRatio { get; private set; } = 1.0f;

    /// <summary>
    /// Modbus 功能碼 (03=Holding, 04=Input, 01=Coil, 02=Discrete)
    /// </summary>
    public byte nFunctionCode { get; private set; }

    /// <summary>
    /// 暫存器數量 (根據資料型態決定)
    /// </summary>
    public int nRegisterCount { get; private set; } = 1;

    /// <summary>
    /// 解析地址格式並設定功能碼與實際地址
    /// </summary>
    /// <returns>解析成功回傳 true，失敗回傳 false</returns>
    public bool ParseAddress()
    {
        if (string.IsNullOrEmpty(szAddress))
            return false;

        try
        {
            // 解析 5位數慣例地址格式
            var nFullAddress = int.Parse(szAddress);
            
            if (nFullAddress >= 40000 && nFullAddress <= 49999)
            {
                // 4xxxx: Holding Registers (Function Code 03)
                nFunctionCode = 3;
                nParsedAddress = nFullAddress - 40001; // 0-based
            }
            else if (nFullAddress >= 30000 && nFullAddress <= 39999)
            {
                // 3xxxx: Input Registers (Function Code 04)
                nFunctionCode = 4;
                nParsedAddress = nFullAddress - 30001; // 0-based
            }
            else if (nFullAddress >= 10000 && nFullAddress <= 19999)
            {
                // 1xxxx: Discrete Inputs (Function Code 02)
                nFunctionCode = 2;
                nParsedAddress = nFullAddress - 10001; // 0-based
            }
            else if (nFullAddress >= 1 && nFullAddress <= 9999)
            {
                // 0xxxx: Coils (Function Code 01)
                nFunctionCode = 1;
                nParsedAddress = nFullAddress - 1; // 0-based
            }
            else
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解析縮放比例並設定暫存器數量
    /// </summary>
    /// <returns>解析成功回傳 true，失敗回傳 false</returns>
    public bool ParseRatioAndRegisterCount()
    {
        if (!float.TryParse(szRatio, out var tempRatio))
        {
            fRatio = 1.0f;
        }
        else
        {
            fRatio = tempRatio;
        }

        // 根據資料型態設定暫存器數量
        switch (szDataType.ToUpper())
        {
            case "INTEGER":
                nRegisterCount = 1;
                break;
            case "UINTEGER":
                nRegisterCount = 1;
                break;
            case "FLOATINGPT":
                nRegisterCount = 2;
                break;
            case "SWAPPEDFP":
                nRegisterCount = 2;
                break;
            case "DOUBLE":
                nRegisterCount = 4;
                break;
            case "SWAPPEDDOUBLE":
                nRegisterCount = 4;
                break;
            default:
                nRegisterCount = 1;
                break;
        }

        return true;
    }

    /// <summary>
    /// 根據資料型態與 Ratio 計算實際物理量
    /// </summary>
    /// <param name="rawData">原始暫存器資料</param>
    /// <returns>計算後的物理量</returns>
    public float CalculatePhysicalValue(ushort[] rawData)
    {
        if (rawData == null || rawData.Length < nRegisterCount)
            return 0.0f;

        float fRawValue = 0.0f;

        switch (szDataType.ToUpper())
        {
            case "INTEGER":
                fRawValue = (short)rawData[0]; // 有號整數
                break;
            case "UINTEGER":
                fRawValue = rawData[0]; // 無號整數
                break;
            case "FLOATINGPT":
                if (rawData.Length >= 2)
                {
                    // 對應 ModScan "Floating Pt": 其實際邏輯為 Low Word First (CDAB)
                    // 將第一個暫存器 rawData[0] 當作低位字組
                    var bytes = new byte[4];
                    bytes[0] = (byte)(rawData[0] & 0xFF);      
                    bytes[1] = (byte)(rawData[0] >> 8);         
                    bytes[2] = (byte)(rawData[1] & 0xFF);       
                    bytes[3] = (byte)(rawData[1] >> 8);         
                    fRawValue = BitConverter.ToSingle(bytes, 0);
                }
                break;
            case "SWAPPEDFP":
                if (rawData.Length >= 2)
                {
                    // 對應 ModScan "Swapped FP": 其實際邏輯為 High Word First (ABCD)
                    // 將第一個暫存器 rawData[0] 當作高位字組
                    var bytes = new byte[4];
                    bytes[0] = (byte)(rawData[1] & 0xFF);      
                    bytes[1] = (byte)(rawData[1] >> 8);      
                    bytes[2] = (byte)(rawData[0] & 0xFF);    
                    bytes[3] = (byte)(rawData[0] >> 8);       
                    fRawValue = BitConverter.ToSingle(bytes, 0);
                }
                break;
            case "DOUBLE":
                if (rawData.Length >= 4)
                {
                    // 對應 ModScan "Double": 其實際邏輯為 Low Word First (GHEFCDAB)
                    var bytes = new byte[8];
                    bytes[0] = (byte)(rawData[0] & 0xFF);       
                    bytes[1] = (byte)(rawData[0] >> 8);         
                    bytes[2] = (byte)(rawData[1] & 0xFF);       
                    bytes[3] = (byte)(rawData[1] >> 8);         
                    bytes[4] = (byte)(rawData[2] & 0xFF);       
                    bytes[5] = (byte)(rawData[2] >> 8);         
                    bytes[6] = (byte)(rawData[3] & 0xFF);       
                    bytes[7] = (byte)(rawData[3] >> 8);         
                    fRawValue = (float)BitConverter.ToDouble(bytes, 0);
                }
                break;
            case "SWAPPEDDOUBLE":
                if (rawData.Length >= 4)
                {
                    // 對應 ModScan "Swapped Double": 其實際邏輯為 High Word First (ABCDEFGH)
                    var bytes = new byte[8];
                    bytes[0] = (byte)(rawData[3] & 0xFF);       
                    bytes[1] = (byte)(rawData[3] >> 8);         
                    bytes[2] = (byte)(rawData[2] & 0xFF);       
                    bytes[3] = (byte)(rawData[2] >> 8);         
                    bytes[4] = (byte)(rawData[1] & 0xFF);       
                    bytes[5] = (byte)(rawData[1] >> 8);         
                    bytes[6] = (byte)(rawData[0] & 0xFF);       
                    bytes[7] = (byte)(rawData[0] >> 8);         
                    fRawValue = (float)BitConverter.ToDouble(bytes, 0);
                }
                break;
        }

        return fRawValue * fRatio;
    }

    /// <summary>
    /// 驗證點位設定是否有效
    /// </summary>
    /// <returns>驗證成功回傳 true</returns>
    public bool Validate()
    {
        if (string.IsNullOrEmpty(szName) || string.IsNullOrEmpty(szAddress))
            return false;

        return ParseAddress() && ParseRatioAndRegisterCount();
    }
}

/// <summary>
/// Modbus 設備配置資料模型
/// </summary>
public class ModbusDeviceConfigModel
{
    /// <summary>
    /// Modbus 設備 IP 地址
    /// </summary>
    [Required]
    public string szIP { get; set; } = string.Empty;

    /// <summary>
    /// TCP 通訊埠號 (預設 502)
    /// </summary>
    public int nPort { get; set; } = 502;

    /// <summary>
    /// 設備站號 (Unit ID)，多個站號以逗點分隔
    /// </summary>
    public string szModbusId { get; set; } = "1";

    /// <summary>
    /// 連線逾時時間 (毫秒)
    /// </summary>
    public int nConnectTimeout { get; set; } = 1000;

    /// <summary>
    /// 資料庫配置 ID，用於 SID 動態生成
    /// </summary>
    public int nDatabaseId { get; set; } = 0;

    /// <summary>
    /// Coordinator 名稱 (對應 JSON 設定檔名稱，即 ModbusCoordinator.Name)
    /// </summary>
    public string szCoordinatorName { get; set; } = string.Empty;

    /// <summary>
    /// 點位標籤清單
    /// </summary>
    public List<ModbusTagModel> tagList { get; set; } = new();

    /// <summary>
    /// 解析 ModbusId 字串為陣列
    /// </summary>
    /// <returns>站號陣列</returns>
    public byte[] GetModbusIdArray()
    {
        if (string.IsNullOrEmpty(szModbusId))
            return new byte[] { 1 };

        try
        {
            return szModbusId.Split(',')
                           .Select(id => byte.Parse(id.Trim()))
                           .ToArray();
        }
        catch
        {
            return new byte[] { 1 };
        }
    }

    /// <summary>
    /// 驗證設備配置是否有效
    /// </summary>
    /// <returns>驗證成功回傳 true</returns>
    public bool Validate()
    {
        if (string.IsNullOrEmpty(szIP) || nPort <= 0 || nPort > 65535)
            return false;

        if (tagList == null || tagList.Count == 0)
            return false;

        return tagList.All(tag => tag.Validate());
    }
}