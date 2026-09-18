using ScadaEngine.Engine.Communication.Modbus.Models;

namespace ScadaEngine.Tests.Modbus;

/// <summary>
/// 站號 / Device 互斥規則測試（plan 2026-09-18 決策 4 + 決策 5 留白）。
/// ModbusPointModel.ResolveDeviceGroup 是互斥規則單一真相，Engine 載入 JSON 與 Web 熱編輯共用，
/// 這裡鎖住它的邊界，避免未來任一端改動時規則悄悄走偏。
///
/// 規則：
///   多站號（isMultiStation=true）→ 一律 null（站號即設備，Tag.Device 靜默忽略）
///   單站號 → Tag.Device 去頭尾空白；留白 / 全空白 → null（未分群）
/// </summary>
public class ResolveDeviceGroupTests
{
    // ── 多站號：無論 Device 填什麼，一律忽略回 null（決策 4 互斥）──
    [Theory]
    [InlineData("冰水主機")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MultiStation_AlwaysNull_IgnoringDevice(string? szDevice)
    {
        Assert.Null(ModbusPointModel.ResolveDeviceGroup(szDevice, isMultiStation: true));
    }

    // ── 單站號 + 有填 Device：投影進 DeviceGroup ──
    [Fact]
    public void SingleStation_WithDevice_ProjectsValue()
    {
        Assert.Equal("冰水主機", ModbusPointModel.ResolveDeviceGroup("冰水主機", isMultiStation: false));
    }

    // ── 單站號 + Device 前後有空白：去頭尾空白 ──
    [Fact]
    public void SingleStation_TrimsWhitespace()
    {
        Assert.Equal("冷卻塔", ModbusPointModel.ResolveDeviceGroup("  冷卻塔  ", isMultiStation: false));
    }

    // ── 單站號 + 留白 / 全空白：未分群 → null（決策 5 留白必須，行為不變）──
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SingleStation_BlankDevice_Null(string? szDevice)
    {
        Assert.Null(ModbusPointModel.ResolveDeviceGroup(szDevice, isMultiStation: false));
    }

    // ── FromTag 會把算好的 DeviceGroup 帶進點位；未傳則預設 null（舊呼叫相容）──
    [Fact]
    public void FromTag_CarriesDeviceGroup_AndDefaultsNull()
    {
        var tag = new ModbusTagModel { szName = "T", szAddress = "40001", szDataType = "INTEGER", szRatio = "1" };

        var withGroup = ModbusPointModel.FromTag(tag, "65537-S1", "冰水主機");
        Assert.Equal("冰水主機", withGroup.szDeviceGroup);

        var noGroup = ModbusPointModel.FromTag(tag, "65537-S1");
        Assert.Null(noGroup.szDeviceGroup);
    }
}
