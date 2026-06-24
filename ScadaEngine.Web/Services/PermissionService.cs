using System.Security.Claims;
using System.Text.Json;
using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 權限檢查服務 — 從 ClaimsPrincipal 解析使用者角色與權限
/// </summary>
public static class PermissionService
{
    /// <summary>
    /// 系統中可配置的主頁面路由清單
    /// </summary>
    public static readonly (string Route, string Name)[] ConfigurablePages =
    [
        ("/ScadaPage",      "即時監控"),
        ("/RealTime",       "即時數據"),
        ("/ConditionCtrl",  "條件控制"),
        ("/LogicFlow",      "流程圖控制"),
        ("/HistoryData",    "歷史資料"),
        ("/EventLog",       "事件記錄"),
        ("/ChilledWaterSystem", "水系統迴路設定"),
        ("/EnergyMeter",    "電表/迴路設定"),
        ("/EnergyReport",   "用電報表"),
        ("/Designer",       "畫面設計"),
        ("/Config",         "系統參數"),
        ("/ModbusCoordinator", "Modbus來源"),
        ("/DbCoordinator",  "DB 來源"),
        ("/AccountSetting", "帳號管理"),
    ];

    /// <summary>
    /// 判斷是否為 Admin 角色
    /// </summary>
    public static bool IsAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole("Admin");
    }

    /// <summary>
    /// 檢查使用者是否可存取指定主頁面路由
    /// </summary>
    public static bool CanAccessPage(ClaimsPrincipal user, string szRoute)
    {
        if (!user.Identity?.IsAuthenticated ?? true)
            return false;

        if (IsAdmin(user))
            return true;

        var permData = GetPermissionData(user);
        return permData.pages.Contains(szRoute, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 從 Claims 解析 PermissionData
    /// </summary>
    public static PermissionData GetPermissionData(ClaimsPrincipal user)
    {
        var szJson = user.FindFirstValue("Permissions");
        if (string.IsNullOrEmpty(szJson) || szJson == "{}")
            return new PermissionData();

        try
        {
            return JsonSerializer.Deserialize<PermissionData>(szJson) ?? new PermissionData();
        }
        catch
        {
            return new PermissionData();
        }
    }

    /// <summary>
    /// 檢查 User 是否可檢視指定 ScadaPage 子頁面
    /// </summary>
    public static bool CanViewScadaPage(ClaimsPrincipal user, string szPageSid)
    {
        if (IsAdmin(user))
            return true;

        var permData = GetPermissionData(user);
        return permData.scadaPages.TryGetValue(szPageSid, out var perm) && perm.canView;
    }

    /// <summary>
    /// 檢查 User 是否可右鍵控制指定 ScadaPage 子頁面
    /// </summary>
    public static bool CanControlScadaPage(ClaimsPrincipal user, string szPageSid)
    {
        if (IsAdmin(user))
            return true;

        var permData = GetPermissionData(user);
        return permData.scadaPages.TryGetValue(szPageSid, out var perm) && perm.canControl;
    }
}
