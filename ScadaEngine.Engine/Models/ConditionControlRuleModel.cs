namespace ScadaEngine.Engine.Models;

/// <summary>
/// 條件控制規則資料模型，對應 ConditionControlRules 資料表
/// </summary>
public class ConditionControlRuleModel
{
    /// <summary>自動遞增主鍵</summary>
    public int Id { get; set; }

    /// <summary>條件點位 SID</summary>
    public string szConditionPointSID { get; set; } = string.Empty;

    /// <summary>運算子 (0=&gt; 1=&lt; 2=&gt;= 3=&lt;= 4=== 5=!=)</summary>
    public byte nOperator { get; set; }

    /// <summary>條件數值</summary>
    public double dConditionValue { get; set; }

    /// <summary>控制點位 SID</summary>
    public string szControlPointSID { get; set; } = string.Empty;

    /// <summary>控制值</summary>
    public double dControlValue { get; set; }

    /// <summary>備註（最多 50 字）</summary>
    public string? szRemarks { get; set; }

    /// <summary>是否啟用</summary>
    public bool isEnabled { get; set; } = true;

    /// <summary>建立時間</summary>
    public DateTime? dtCreatedAt { get; set; }

    /// <summary>將 nOperator 轉換為符號字串</summary>
    public string OperatorSymbol => nOperator switch
    {
        0 => ">",
        1 => "<",
        2 => ">=",
        3 => "<=",
        4 => "==",
        5 => "!=",
        _ => "?"
    };

    /// <summary>將符號字串轉換為 nOperator 值</summary>
    public static byte ParseOperator(string symbol) => symbol switch
    {
        ">"  => 0,
        "<"  => 1,
        ">=" => 2,
        "<=" => 3,
        "==" => 4,
        "!=" => 5,
        _    => 0
    };
}
