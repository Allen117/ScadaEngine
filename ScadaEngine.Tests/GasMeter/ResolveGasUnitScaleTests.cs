using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.GasMeter;

/// <summary>
/// 鎖住 GasMeterCircuitService.ResolveUnitScale：點位單位字串 → m³ 換算係數。
/// 這是「綁定時定案」的單位判定（寫進 GasMeterCircuit.UnitScale，之後不追溯），
/// 對錯 = 該氣表所有歷史用氣量差 1000 倍，或該讓使用者選的點位選不到 / 不該選的選得到。
///
/// ⚠️ 本組測試明文鎖住與水表的**刻意差異**：氣表把「度 / 氣度 / 天然氣度 / 瓦斯度」判為 1.0，
///    水表則刻意排除「度」（見 <c>ResolveUnitScaleTests</c>）。兩者不可互相「對稱修正」。
/// </summary>
public class ResolveGasUnitScaleTests
{
    [Theory]
    [InlineData("m³")]
    [InlineData("m3")]
    [InlineData("M3")]          // 不分大小寫
    [InlineData("Nm³")]
    [InlineData("Nm3")]
    [InlineData("NM3")]
    [InlineData("SCM")]
    [InlineData("CMD")]
    [InlineData("cms")]
    [InlineData("立方公尺")]
    [InlineData("立方米")]
    [InlineData("米立方")]
    [InlineData(" m3 ")]        // 前後空白先 trim
    public void 立方公尺系單位_係數為1(string szUnit)
    {
        Assert.Equal(1.0, GasMeterCircuitService.ResolveUnitScale(szUnit));
    }

    [Theory]
    [InlineData("度")]           // ⚠ 與水表相反：氣表**納入**裸「度」（使用者指定）
    [InlineData("氣度")]
    [InlineData("天然氣度")]
    [InlineData("瓦斯度")]
    [InlineData(" 度 ")]
    public void 度數系單位_係數為1_與水表刻意相反(string szUnit)
    {
        Assert.Equal(1.0, GasMeterCircuitService.ResolveUnitScale(szUnit));
    }

    /// <summary>水表刻意排除「度」以避開電度混淆；此處明文對照，避免日後有人把兩張對照表「統一」</summary>
    [Fact]
    public void 度_在水表回null_在氣表回1()
    {
        Assert.Null(WaterMeterCircuitService.ResolveUnitScale("度"));
        Assert.Equal(1.0, GasMeterCircuitService.ResolveUnitScale("度"));
    }

    [Theory]
    [InlineData("L")]
    [InlineData("l")]
    [InlineData("Liter")]
    [InlineData("litre")]
    [InlineData("公升")]
    [InlineData("升")]
    public void 公升系單位_係數為千分之一(string szUnit)
    {
        Assert.Equal(0.001, GasMeterCircuitService.ResolveUnitScale(szUnit));
    }

    [Theory]
    [InlineData("kWh")]
    [InlineData("kW")]
    [InlineData("RT")]
    [InlineData("°C")]
    [InlineData("m³/h")]        // 流量（瞬時）非累積量
    [InlineData("Nm³/h")]       // 流量（Nm³/hr）非累積量
    [InlineData("CMH")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void 非氣量累積單位_回null(string? szUnit)
    {
        Assert.Null(GasMeterCircuitService.ResolveUnitScale(szUnit));
    }
}

/// <summary>
/// 鎖住 <c>GasMeterCircuitService.ResolveGasPointScale</c> — **氣量點位判定的唯一入口**
/// （點位下拉過濾與存檔驗證共用同一支）。
///
/// 條件：單位可換算 m³ **且**（點位名稱含天然氣關鍵字 **或** 單位本身無歧義如 Nm³ / SCM / 氣度）。
/// 名稱這道條件的存在理由：氣表單位對照表刻意納入「度」，但「度」也是 kWh 俗稱 —
/// 只看單位會把**電表點位**撈進氣表下拉。錯放 = 氣費拿電度數計價。
/// </summary>
public class ResolveGasPointScaleTests
{
    // ── 名稱關鍵字 ──────────────────────────────────────

    [Theory]
    [InlineData("天然氣累積量")]
    [InlineData("鍋爐瓦斯用量")]
    [InlineData("燃氣總表")]
    [InlineData("A棟用氣量")]
    [InlineData("氣量累計")]
    [InlineData("一號氣表")]
    [InlineData("氣度累計")]
    [InlineData("Gas Total")]
    [InlineData("NATURAL GAS CUM")]     // 不分大小寫
    [InlineData("LNG_Totalizer")]
    [InlineData("CNG_Meter_01")]
    public void 名稱含天然氣關鍵字_判定為氣量點位(string szName)
    {
        Assert.True(GasMeterCircuitService.HasGasNameKeyword(szName));
    }

    [Theory]
    [InlineData("空氣壓縮機出口壓力")]   // 「氣」單字刻意不當關鍵字
    [InlineData("冷氣主機電流")]
    [InlineData("氣溫")]
    [InlineData("外氣濕度")]
    [InlineData("總用電度數")]
    [InlineData("自來水累積量")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void 名稱不含天然氣關鍵字_不判定為氣量點位(string? szName)
    {
        Assert.False(GasMeterCircuitService.HasGasNameKeyword(szName));
    }

    // ── 雙條件組合 ──────────────────────────────────────

    [Theory]
    [InlineData("天然氣累積量", "m³", 1.0)]
    [InlineData("鍋爐瓦斯用量", "度", 1.0)]        // 名稱擔保 → 裸「度」放行
    [InlineData("Gas Totalizer", "m3", 1.0)]
    [InlineData("天然氣累積量", "公升", 0.001)]     // L 系換算係數維持 0.001
    public void 名稱有關鍵字且單位可換算_回換算係數(string szName, string szUnit, double dExpected)
    {
        Assert.Equal(dExpected, GasMeterCircuitService.ResolveGasPointScale(szName, szUnit));
    }

    [Theory]
    [InlineData("累積量", "Nm³")]
    [InlineData("Totalizer", "SCM")]
    [InlineData("一號表累計", "氣度")]
    [InlineData("累積讀數", "天然氣度")]
    [InlineData("累積讀數", "瓦斯度")]
    public void 單位本身無歧義_名稱無關鍵字也放行(string szName, string szUnit)
    {
        Assert.Equal(1.0, GasMeterCircuitService.ResolveGasPointScale(szName, szUnit));
        Assert.True(GasMeterCircuitService.IsUnambiguousGasUnit(szUnit));
    }

    /// <summary>本次改動的核心：單位標「度」的**電表**點位不得進入氣表點位清單</summary>
    [Theory]
    [InlineData("總用電度數", "度")]
    [InlineData("A棟累積電量", "度")]
    [InlineData("Main Meter kWh", "度")]
    public void 電表點位單位標度_名稱無氣關鍵字_一律排除(string szName, string szUnit)
    {
        // 單看單位會過（「度」在氣表對照表中 = 1.0），加上名稱條件後才擋得住
        Assert.Equal(1.0, GasMeterCircuitService.ResolveUnitScale(szUnit));
        Assert.Null(GasMeterCircuitService.ResolveGasPointScale(szName, szUnit));
    }

    /// <summary>順帶效果：同為 m³ / L 的**水表**點位也被名稱條件排除</summary>
    [Theory]
    [InlineData("自來水累積量", "m³")]
    [InlineData("冷卻塔補水量", "公升")]
    public void 水表點位_名稱無氣關鍵字_一律排除(string szName, string szUnit)
    {
        Assert.NotNull(GasMeterCircuitService.ResolveUnitScale(szUnit));
        Assert.Null(GasMeterCircuitService.ResolveGasPointScale(szName, szUnit));
    }

    [Theory]
    [InlineData("天然氣瞬時流量", "Nm³/h")]   // 名稱有關鍵字但單位是流量 → 仍排除
    [InlineData("瓦斯表溫度", "°C")]
    [InlineData("天然氣累積量", "")]
    [InlineData("天然氣累積量", null)]
    public void 單位不可換算_名稱有關鍵字也排除(string szName, string? szUnit)
    {
        Assert.Null(GasMeterCircuitService.ResolveGasPointScale(szName, szUnit));
    }
}
