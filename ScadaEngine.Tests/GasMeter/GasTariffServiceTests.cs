using ScadaEngine.Web.Features.GasTariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.GasMeter;

/// <summary>
/// 鎖住 GasTariffService 的純邏輯三件套：
/// ValidatePlan（級距連續 / 第一級 nFrom=1 / 末級 nTo=null / 單價非負 / 生效日格式）、
/// ParseConfig（seed JSON 可解析）、SelectPlanForDate（依期別起日選生效版本）。
/// 對錯 = 存進壞方案讓氣費全算錯、或期別選錯費率版本套錯單價。
/// </summary>
public class GasTariffServiceTests
{
    /// <summary>合法的四級距方案（形狀同 seed，但填了實際單價）</summary>
    private static GasTariffPlan MakePlan() => new()
    {
        szPlanId = "gas-flow-default",
        szName = "天然氣流動氣費",
        szEffectiveDate = "2000-01-01",
        tiers = new List<GasTariffTier>
        {
            new() { nFrom = 1, nTo = 20, dPrice = 10 },
            new() { nFrom = 21, nTo = 50, dPrice = 12 },
            new() { nFrom = 51, nTo = 100, dPrice = 15 },
            new() { nFrom = 101, nTo = null, dPrice = 18 },
        },
    };

    // ── ValidatePlan ─────────────────────────────────────

    [Fact]
    public void 合法方案_驗證通過()
    {
        var (isValid, szError) = GasTariffService.ValidatePlan(MakePlan());
        Assert.True(isValid, szError);
    }

    /// <summary>seed 為「單一級距 1 度以上、單價 0」的空白範本 — 必須通過驗證，否則首次開頁就存不了</summary>
    [Fact]
    public void 空白seed範本_單一級距單價0_驗證通過()
    {
        var plan = new GasTariffPlan
        {
            szPlanId = "gas-flow-default",
            szName = "天然氣流動氣費",
            szEffectiveDate = "2000-01-01",
            tiers = new List<GasTariffTier> { new() { nFrom = 1, nTo = null, dPrice = 0 } },
        };
        var (isValid, szError) = GasTariffService.ValidatePlan(plan);
        Assert.True(isValid, szError);
    }

    [Fact]
    public void 級距不連續_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers[1].nFrom = 22;   // 應為 21（上一級 nTo=20 + 1）
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 第一級nFrom非1_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers[0].nFrom = 0;
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 單價為負_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers[2].dPrice = -0.01;
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 中間級nTo為null_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers[1].nTo = null;
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 末級nTo非null_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers[^1].nTo = 999;
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026/01/01")]
    [InlineData("2026-1-1")]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    public void 生效日格式錯_驗證失敗(string szEffectiveDate)
    {
        var plan = MakePlan();
        plan.szEffectiveDate = szEffectiveDate;
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 方案Id為空_驗證失敗()
    {
        var plan = MakePlan();
        plan.szPlanId = "  ";
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    [Fact]
    public void 無任何級距_驗證失敗()
    {
        var plan = MakePlan();
        plan.tiers.Clear();
        Assert.False(GasTariffService.ValidatePlan(plan).isValid);
    }

    // ── ParseConfig ──────────────────────────────────────

    [Fact]
    public void 解析seed格式JSON_單一空白級距正確()
    {
        const string szJson = """
        {
          "plans": [
            {
              "szPlanId": "gas-flow-default",
              "szName": "天然氣流動氣費",
              "szEffectiveDate": "2000-01-01",
              "tiers": [
                { "nFrom": 1, "nTo": null, "dPrice": 0 }
              ]
            }
          ]
        }
        """;

        var config = GasTariffService.ParseConfig(szJson);

        Assert.NotNull(config);
        var plan = Assert.Single(config!.plans);
        Assert.Equal("gas-flow-default", plan.szPlanId);
        Assert.Equal("2000-01-01", plan.szEffectiveDate);
        var tier = Assert.Single(plan.tiers);
        Assert.Equal(1, tier.nFrom);
        Assert.Null(tier.nTo);
        Assert.Equal(0, tier.dPrice, 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ broken json")]
    public void 解析壞JSON_回null不擲例外(string szJson)
    {
        Assert.Null(GasTariffService.ParseConfig(szJson));
    }

    // ── SelectPlanForDate ────────────────────────────────

    private static GasTariffConfig MakeMultiVersionConfig()
    {
        GasTariffPlan Make(string szId, string szDate)
        {
            var plan = MakePlan();
            plan.szPlanId = szId;
            plan.szEffectiveDate = szDate;
            return plan;
        }
        return new GasTariffConfig
        {
            plans = new List<GasTariffPlan>
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
        var plan = GasTariffService.SelectPlanForDate(config, DateTime.Parse(szPeriodStart));
        Assert.NotNull(plan);
        Assert.Equal(szExpectedId, plan!.szPlanId);
    }

    [Fact]
    public void 期別起日早於所有生效日_取生效日最早方案()
    {
        var config = MakeMultiVersionConfig();
        var plan = GasTariffService.SelectPlanForDate(config, new DateTime(1999, 5, 1));
        Assert.NotNull(plan);
        Assert.Equal("v2000", plan!.szPlanId);
    }

    [Fact]
    public void 無任何方案_回null()
    {
        Assert.Null(GasTariffService.SelectPlanForDate(new GasTariffConfig(), DateTime.Today));
    }
}
