using ScadaEngine.Web.Features.TariffSetting.Models;
using ScadaEngine.Web.Services;

namespace ScadaEngine.Tests.Electricity;

/// <summary>
/// 鎖住電費「方案版本 + 生效日」的三件純邏輯：
/// SelectPlanForDate（依日期選採用方案版本）、MigrateLegacyActivePlan（舊資料相容）、
/// ValidateAdoptions（採用時間軸驗證），外加自建方案的 ValidatePlan。
///
/// 對錯 = 歷史電費被追溯改寫（改今天的費率卻改到去年的帳）、
/// 或舊資料升級後歷史突然算不出金額、或存進壞時間軸讓某段時間無方案可算。
/// </summary>
public class TariffAdoptionTests
{
    // ── 測試資料 ─────────────────────────────────────────

    private static TariffPlan FlatPlan(string szPlanId, double dPrice = 5) => new()
    {
        szPlanId = szPlanId,
        szName = szPlanId,
        szCategory = "custom",
        szType = "flat",
        szSummerStart = "06-01",
        szSummerEnd = "09-30",
        flatRate = new TariffFlatRate { dSummer = dPrice, dNonSummer = dPrice },
    };

    /// <summary>三個方案 + 三筆採用（2000/2025/2026 各換一次）</summary>
    private static TariffConfig MakeTimelineConfig() => new()
    {
        plans = [FlatPlan("planA"), FlatPlan("planB"), FlatPlan("planC")],
        adoptions =
        [
            new TariffAdoption { szEffectiveDate = "2000-01-01", szPlanId = "planA" },
            new TariffAdoption { szEffectiveDate = "2025-07-01", szPlanId = "planB" },
            new TariffAdoption { szEffectiveDate = "2026-01-01", szPlanId = "planC" },
        ],
    };

    // ── SelectPlanForDate ────────────────────────────────

    [Fact]
    public void 時間軸為空_回null()
    {
        var config = new TariffConfig { plans = [FlatPlan("planA")] };
        Assert.Null(TariffSettingService.SelectPlanForDate(config, DateTime.Today));
    }

    [Fact]
    public void 單筆採用_生效日當天起適用()
    {
        var config = new TariffConfig
        {
            plans = [FlatPlan("planA")],
            adoptions = [new TariffAdoption { szEffectiveDate = "2026-03-01", szPlanId = "planA" }],
        };

        Assert.Null(TariffSettingService.SelectPlanForDate(config, new DateTime(2026, 2, 28)));
        Assert.Equal("planA", TariffSettingService.SelectPlanForDate(config, new DateTime(2026, 3, 1))?.szPlanId);
        Assert.Equal("planA", TariffSettingService.SelectPlanForDate(config, new DateTime(2030, 1, 1))?.szPlanId);
    }

    [Theory]
    [InlineData("2000-01-01", "planA")]  // 生效日當天即適用
    [InlineData("2024-12-31", "planA")]  // planB 尚未生效
    [InlineData("2025-07-01", "planB")]  // 換版當天
    [InlineData("2025-12-31", "planB")]  // planB 與 planC 之間
    [InlineData("2026-01-01", "planC")]
    [InlineData("2026-08-03", "planC")]  // 之後都用最新版
    public void 多筆採用_取生效日不晚於該日的最新一筆(string szDate, string szExpectedPlanId)
    {
        var config = MakeTimelineConfig();
        var plan = TariffSettingService.SelectPlanForDate(config, DateTime.Parse(szDate));
        Assert.NotNull(plan);
        Assert.Equal(szExpectedPlanId, plan!.szPlanId);
    }

    [Fact]
    public void 日期早於所有生效日_回null不計價()
    {
        // 與水費「退回最早方案」相反 — 電費未採用方案就不該計價
        var config = MakeTimelineConfig();
        Assert.Null(TariffSettingService.SelectPlanForDate(config, new DateTime(1999, 12, 31)));
    }

    [Fact]
    public void 採用指向已刪除方案_回null()
    {
        var config = MakeTimelineConfig();
        config.plans.RemoveAll(p => p.szPlanId == "planC");
        Assert.Null(TariffSettingService.SelectPlanForDate(config, new DateTime(2026, 6, 1)));
        // 更早的日期仍選得到未被刪的方案
        Assert.Equal("planB", TariffSettingService.SelectPlanForDate(config, new DateTime(2025, 8, 1))?.szPlanId);
    }

    [Fact]
    public void 同一生效日多筆_取後定義者()
    {
        var config = new TariffConfig
        {
            plans = [FlatPlan("planA"), FlatPlan("planB")],
            adoptions =
            [
                new TariffAdoption { szEffectiveDate = "2026-01-01", szPlanId = "planA" },
                new TariffAdoption { szEffectiveDate = "2026-01-01", szPlanId = "planB" },
            ],
        };
        Assert.Equal("planB", TariffSettingService.SelectPlanForDate(config, new DateTime(2026, 5, 1))?.szPlanId);
    }

    [Fact]
    public void 生效日順序顛倒仍正確選版()
    {
        var config = MakeTimelineConfig();
        config.adoptions.Reverse();
        Assert.Equal("planB", TariffSettingService.SelectPlanForDate(config, new DateTime(2025, 9, 1))?.szPlanId);
    }

    // ── MigrateLegacyActivePlan ──────────────────────────

    [Fact]
    public void 舊資料只有採用方案指標_補一筆2000年生效()
    {
        var config = new TariffConfig
        {
            szActivePlanId = "planA",
            plans = [FlatPlan("planA")],
        };

        TariffSettingService.MigrateLegacyActivePlan(config);

        var adoption = Assert.Single(config.adoptions);
        Assert.Equal("2000-01-01", adoption.szEffectiveDate);
        Assert.Equal("planA", adoption.szPlanId);
        // 生效日極早 → 任何歷史日期都選到同一方案（歷史電費數字不變）
        Assert.Equal("planA", TariffSettingService.SelectPlanForDate(config, new DateTime(2001, 5, 5))?.szPlanId);
    }

    [Fact]
    public void 已有時間軸_不重複補()
    {
        var config = MakeTimelineConfig();
        config.szActivePlanId = "planA";

        TariffSettingService.MigrateLegacyActivePlan(config);

        Assert.Equal(3, config.adoptions.Count);
    }

    [Fact]
    public void 採用方案指標與時間軸皆空_維持空()
    {
        var config = new TariffConfig { plans = [FlatPlan("planA")] };
        TariffSettingService.MigrateLegacyActivePlan(config);
        Assert.Empty(config.adoptions);
    }

    // ── ValidateAdoptions ────────────────────────────────

    [Fact]
    public void 空時間軸_驗證通過()
    {
        var config = new TariffConfig { plans = [FlatPlan("planA")] };
        Assert.True(TariffSettingService.ValidateAdoptions(config).isValid);
    }

    [Fact]
    public void 合法時間軸_驗證通過()
    {
        var (isValid, szError) = TariffSettingService.ValidateAdoptions(MakeTimelineConfig());
        Assert.True(isValid, szError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026/01/01")]
    [InlineData("2026-1-1")]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    public void 生效日格式錯_驗證失敗(string szEffectiveDate)
    {
        var config = MakeTimelineConfig();
        config.adoptions[1].szEffectiveDate = szEffectiveDate;
        Assert.False(TariffSettingService.ValidateAdoptions(config).isValid);
    }

    [Fact]
    public void 採用方案不存在_驗證失敗()
    {
        var config = MakeTimelineConfig();
        config.adoptions[2].szPlanId = "planZ";
        Assert.False(TariffSettingService.ValidateAdoptions(config).isValid);
    }

    [Fact]
    public void 採用方案為空_驗證失敗()
    {
        var config = MakeTimelineConfig();
        config.adoptions[0].szPlanId = "  ";
        Assert.False(TariffSettingService.ValidateAdoptions(config).isValid);
    }

    [Fact]
    public void 同一生效日重複_驗證失敗()
    {
        var config = MakeTimelineConfig();
        config.adoptions[2].szEffectiveDate = config.adoptions[1].szEffectiveDate;
        Assert.False(TariffSettingService.ValidateAdoptions(config).isValid);
    }

    // ── ValidatePlan（自建方案） ─────────────────────────

    [Fact]
    public void 自建flat方案_一度五塊_驗證通過()
    {
        // 房東「一度五塊」情境：夏月/非夏月同價
        var plan = FlatPlan("custom_landlord", 5);
        Assert.Null(TariffSettingService.ValidatePlan(plan));
    }

    [Fact]
    public void 自建flat方案缺單一費率_驗證失敗()
    {
        var plan = FlatPlan("custom_landlord");
        plan.flatRate = null;
        Assert.NotNull(TariffSettingService.ValidatePlan(plan));
    }

    [Fact]
    public void 自建flat方案單價為負_驗證失敗()
    {
        var plan = FlatPlan("custom_landlord");
        plan.flatRate!.dNonSummer = -1;
        Assert.NotNull(TariffSettingService.ValidatePlan(plan));
    }

    [Fact]
    public void 自建progressive方案_兩級距_驗證通過()
    {
        var plan = FlatPlan("custom_prog");
        plan.szType = "progressive";
        plan.flatRate = null;
        plan.tiers =
        [
            new TariffTier { nFrom = 1, nTo = 120, dSummer = 3, dNonSummer = 3 },
            new TariffTier { nFrom = 121, nTo = null, dSummer = 5, dNonSummer = 5 },
        ];
        Assert.Null(TariffSettingService.ValidatePlan(plan));
    }

    [Fact]
    public void 未知型態_驗證失敗()
    {
        var plan = FlatPlan("custom_bad");
        plan.szType = "weird";
        Assert.NotNull(TariffSettingService.ValidatePlan(plan));
    }
}
