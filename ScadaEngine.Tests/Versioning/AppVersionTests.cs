using System.Text.RegularExpressions;
using ScadaEngine.Web.Services;
using Xunit;

namespace ScadaEngine.Tests.Versioning;

/// <summary>
/// 鎖住產品版號管線：Directory.Build.props &lt;Version&gt; → AssemblyInformationalVersion → AppVersion.szCurrent（Web footer）。
/// 若有人把 IncludeSourceRevisionInInformationalVersion 打開（版號後綴 +commit hash）、或把 props 版號寫成非 YY.A.B，這裡會紅。
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void Current_Matches_YY_A_B_Format_Without_CommitHash()
    {
        var sz = AppVersion.szCurrent;

        Assert.Matches(new Regex(@"^\d{2}\.\d+\.\d+$"), sz);
        Assert.DoesNotContain("+", sz);
        Assert.NotEqual("-", sz);
    }

    [Fact]
    public void Current_Equals_Directory_Build_Props_Version()
    {
        // 從測試組件往上找 repo 根目錄的 Directory.Build.props，確認 footer 顯示的就是 props 寫的那個數字
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        FileInfo? props = null;
        while (dir != null && props == null)
        {
            var candidate = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(candidate)) props = new FileInfo(candidate);
            dir = dir.Parent;
        }
        Assert.NotNull(props);

        var m = Regex.Match(File.ReadAllText(props!.FullName), @"<Version>\s*([^<\s]+)\s*</Version>");
        Assert.True(m.Success, "Directory.Build.props 缺 <Version>");
        Assert.Equal(m.Groups[1].Value, AppVersion.szCurrent);
    }
}
