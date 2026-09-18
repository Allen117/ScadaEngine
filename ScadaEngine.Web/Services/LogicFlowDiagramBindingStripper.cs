using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScadaEngine.Web.Services;

/// <summary>
/// LogicFlow 流程圖「點位綁定清除器」—— 複製邏輯 / 資料夾時，把 DiagramJson 內
/// 所有指向特定點位、排程的欄位移除，其餘參數（運算子、常數、計時秒數、演算法設定、
/// 座標、連線）原樣保留。
///
/// ⚠️ 這是**正確性**要求而非顯示偏好：複製出的 output 節點若殘留來源 sid，
/// 使用者一旦啟用該邏輯，Engine 就會寫入**別台設備**的 Modbus 暫存器。
/// 因此一律在資料落地前（CopyNodeAsync 寫入 LogicFlowDiagram 之前）清除。
///
/// ⚠️ 欄位清單與前端 `wwwroot/js/logicflow/canvas.js` 的節點右鍵「清除綁定」
/// （node-clear-binding handler）平行維護 —— 未來新增節點綁定欄位時兩處都要改，
/// 書面真相以 <c>ScadaEngine.Tests/LogicFlow/DiagramBindingStripperTests.cs</c> 的預期清單為準。
/// </summary>
public static class LogicFlowDiagramBindingStripper
{
    /// <summary>空白流程圖（解析失敗時的安全退路）</summary>
    public const string EmptyDiagram = "{\"nodes\":[],\"edges\":[]}";

    /// <summary>節點上屬於「點位 / 排程綁定」的欄位，複製時一律移除</summary>
    public static readonly IReadOnlyList<string> BindingKeys = new[]
    {
        "sid",                  // 點位 SID（input / output / 接點）
        "pointName",            // 點位顯示名
        "unit",                 // 由 picker 從點位帶入的單位
        "scheduleId",           // 排程綁定（接點）
        "scheduleName",
        "histEnabled",          // 歷史值讀取設定（綁定在特定點位上才有意義）
        "histOffsetMinutes",
        "fMin",                 // picker 從點位帶入的量程快照 —— 換點位後判斷會錯（決策 6）
        "fMax",
    };

    /// <summary>
    /// 清除 DiagramJson 內所有節點的點位綁定，回傳新的 JSON 字串。
    /// 輸入為 null / 空字串 / 無法解析時回傳 <see cref="EmptyDiagram"/> ——
    /// 解析不了就無法保證清乾淨，原樣傳回等於把綁定照抄過去，寧可給空白畫布。
    /// </summary>
    public static string Strip(string? szDiagramJson)
    {
        if (string.IsNullOrWhiteSpace(szDiagramJson))
            return EmptyDiagram;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(szDiagramJson);
        }
        catch (JsonException)
        {
            return EmptyDiagram;
        }

        if (root is not JsonObject rootObj)
            return EmptyDiagram;

        if (rootObj["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes)
            {
                if (node is not JsonObject nodeObj) continue;
                foreach (var szKey in BindingKeys)
                    nodeObj.Remove(szKey);
            }
        }

        return rootObj.ToJsonString();
    }
}
