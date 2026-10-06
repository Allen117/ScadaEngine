using System.Reflection;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 產品版號（唯一真相來源 = repo 根目錄 Directory.Build.props 的 &lt;Version&gt;，格式 YY.A.B，
/// 規則見 CLAUDE.md §版本號規則）。MSBuild 把它寫進 AssemblyInformationalVersion，這裡讀回來給
/// _Layout footer 顯示；BuildRelease.ps1 讀同一份 props 寫進 Release 包名與 Windows 服務描述。
/// 用 typeof(AppVersion).Assembly 而非 GetEntryAssembly()，避免宿主差異（服務 / 測試）回 null。
/// </summary>
public static class AppVersion
{
    public static readonly string szCurrent =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
        ?? "-";
}
