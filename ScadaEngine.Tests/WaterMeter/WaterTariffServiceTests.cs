using ScadaEngine.Web.Features.WaterTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.WaterMeter;

/// <summary>
/// 鎖住 WaterTariffService 的純邏輯三件套：
/// ValidatePlan（級距連續 / 第一級 nFrom=1 / 末級 nTo=null / 單價非負 / 生效日格式）、
/// ParseConfig（seed JSON 可解析）、SelectPlanForDate（依期別起日選生效版本）。
/// 對錯 = 存進壞方案讓水費全算錯、或期別選錯費率版本套錯單價。
/// </summary>
public class WaterTariffServiceTests
{
    /// <summary>合法的台水 seed 方案（與 Setting/water-tariff-taiwater-defaults.json 同值）</summary>
    private static WaterTariffPlan MakeSeedPlan() => new()
    {
        szPlanId = "taiwater-flow-default",
        szName = "台水流動水費",
        szEffectiveDate = "2000-01-01",
        tiers = new List<WaterTariffTier>
        {
            new() { nFrom = 1, nTo = 10, dPrice = 7.35 },
            new() { nFrom = 11, nTo = 30, dPrice = 9.45 },
            new() { nFrom = 31, nTo = 50, dPrice = 11.55 },
            new() { nFrom = 51, nTo = null, dPrice = 12.075 },
        },
    };

    // ── ValidatePlan ─────────────────────────────────────

    [Fact]
    public void 合法seed方案_驗證通過()
    {
        var (isValid, szError) = WaterTariffService.ValidatePlan(MakeSeedPlan());
        Assert.True(isValid, szError);
    }

    [Fact]
    public void 級距不連續_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers[1].nFrom = 12;   // 應為 11（上一級 nTo=10 + 1）
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 第一級nFrom非1_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers[0].nFrom = 0;
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 單價為負_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers[2].dPrice = -0.01;
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 中間級nTo為null_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers[1].nTo = null;
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 末級nTo非null_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers[^1].nTo = 999;
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026/01/01")]
    [InlineData("2026-1-1")]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    public void 生效日格式錯_驗證失敗(string szEffectiveDate)
    {
        var plan = MakeSeedPlan();
        plan.szEffectiveDate = szEffectiveDate;
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 方案Id為空_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.szPlanId = "  ";
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 無任何級距_驗證失敗()
    {
        var plan = MakeSeedPlan();
        plan.tiers.Clear();
        Assert.False(WaterTariffService.ValidatePlan(plan).isValid);
    }

    // ── ParseConfig ──────────────────────────────────────

    [Fact]
    public void 解析seed格式JSON_四級距與單價正確()
    {
        const string szJson = """
        {
          "plans": [
            {
              "szPlanId": "taiwater-flow-default",
              "szName": "台水流動水費",
              "szEffectiveDate": "2000-01-01",
              "tiers": [
                { "nFrom": 1, "nTo": 10, "dPrice": 7.35 },
                { "nFrom": 11, "nTo": 30, "dPrice": 9.45 },
                { "nFrom": 31, "nTo": 50, "dPrice": 11.55 },
                { "nFrom": 51, "nTo": null, "dPrice": 12.075 }
              ]
            }
          ]
        }
        """;

        var config = WaterTariffService.ParseConfig(szJson);

        Assert.NotNull(config);
        var plan = Assert.Single(config!.plans);
        Assert.Equal("taiwater-flow-default", plan.szPlanId);
        Assert.Equal("2000-01-01", plan.szEffectiveDate);
        Assert.Equal(4, plan.tiers.Count);
        Assert.Equal(7.35, plan.tiers[0].dPrice, 10);
        Assert.Equal(9.45, plan.tiers[1].dPrice, 10);
        Assert.Equal(11.55, plan.tiers[2].dPrice, 10);
        Assert.Equal(12.075, plan.tiers[3].dPrice, 10);
        Assert.Null(plan.tiers[3].nTo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ broken json")]
    public void 解析壞JSON_回null不擲例外(string szJson)
    {
        Assert.Null(WaterTariffService.ParseConfig(szJson));
    }

    // ── SelectPlanForDate ────────────────────────────────

    private static WaterTariffConfig MakeMultiVersionConfig()
    {
        WaterTariffPlan Make(string szId, string szDate)
        {
            var plan = MakeSeedPlan();
            plan.szPlanId = szId;
            plan.szEffectiveDate = szDate;
            return plan;
        }
        return new WaterTariffConfig
        {
            plans = new List<WaterTariffPlan>
            {
                Make("v2000", "2000-01-01"),
                Make("v2025", "2025-07-01"),
                Make("v2026", "2026-01-01"),
            },
        };
    }

    [Theory]
    [InlineData("2025-12-01", "v2025")]  // 落在 v2025 與 v2026 之間 → 取 v2025
    [InlineData("2026-01-01", "v2026")]  // 生效日當天即適用（含當日）
    [InlineData("2026-08-01", "v2026")]  // 之後都用最新版
    [InlineData("2000-01-01", "v2000")]
    [InlineData("2024-12-31", "v2000")]  // v2025 未生效
    public void 依期別起日選版_取生效日不晚於起日的最新版本(string szPeriodStart, string szExpectedId)
    {
        var config = MakeMultiVersionConfig();
        var plan = WaterTariffService.SelectPlanForDate(config, DateTime.Parse(szPeriodStart));
        Assert.NotNull(plan);
        Assert.Equal(szExpectedId, plan!.szPlanId);
    }

    [Fact]
    public void 期別起日早於所有生效日_取生效日最早方案()
    {
        var config = MakeMultiVersionConfig();
        var plan = WaterTariffService.SelectPlanForDate(config, new DateTime(1999, 5, 1));
        Assert.NotNull(plan);
        Assert.Equal("v2000", plan!.szPlanId);
    }

    [Fact]
    public void 無任何方案_回null()
    {
        Assert.Null(WaterTariffService.SelectPlanForDate(new WaterTariffConfig(), DateTime.Today));
    }
}
