using ScadaEngine.Common.Data.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// EnPI / 節能量報告 — 讀凍結基線 → 報告期逐 bucket 取實際 Y 與各 X →
/// 帶入凍結係數算基線預測 → 節能量（預測 − 實際）/ 累計節能量 / EnPI（實際 ÷ 預測）。
/// 一律用凍結係數，不重算 — 基線是固定比較基準（ISO 50001）。
/// </summary>
public class EnPIReportService
{
    private readonly ILogger<EnPIReportService> _logger;
    private readonly EnergyBaselineService _baselineService;

    public EnPIReportService(ILogger<EnPIReportService> logger, EnergyBaselineService baselineService)
    {
        _logger = logger;
        _baselineService = baselineService;
    }

    public async Task<EnPIReportResult> GetReportAsync(int nBaselineId, DateTime dtStart, DateTime dtEnd)
    {
        var model = await _baselineService.GetByIdAsync(nBaselineId)
            ?? throw new InvalidOperationException($"基線模型 Id={nBaselineId} 不存在");
        if (model.szStatus != "frozen")
            throw new InvalidOperationException("基線尚未凍結 — EnPI 報告只能用凍結後的基線係數");
        if (model.dIntercept == null || model.variables.Any(v => v.dCoefficient == null))
            throw new InvalidOperationException("基線缺少回歸係數，無法產生報告");

        var (allRanges, allLabels) = EnergyBaselineService.BuildRanges(model.szGranularity, dtStart, dtEnd);

        // 只留已過完的 bucket（半桶會產生假節能量）
        var dtNow = DateTime.Now;
        var ranges = new List<(DateTime dtStart, DateTime dtEnd)>();
        var labels = new List<string>();
        for (var i = 0; i < allRanges.Count; i++)
        {
            if (allRanges[i].dtEnd > dtNow) continue;
            ranges.Add(allRanges[i]);
            labels.Add(allLabels[i]);
        }
        if (ranges.Count == 0)
            throw new InvalidOperationException("報告期內沒有任何已過完的取樣區間");

        var yValues = await _baselineService.SampleSeriesAsync(
            model.szTargetType, model.szTargetSID, model.nTargetCircuitId, model.szTargetMode, ranges);
        var xSeries = new List<double?[]>(model.variables.Count);
        foreach (var v in model.variables)
            xSeries.Add(await _baselineService.SampleSeriesAsync(
                v.szVarType, v.szSourceSID, v.nSourceCircuitId, "average", ranges));

        var result = new EnPIReportResult
        {
            nBaselineId = model.nId,
            szBaselineName = model.szName,
            szTargetLabel = model.szTargetLabel,
            szTargetUnit = model.szTargetUnit,
            szGranularity = model.szGranularity,
            dtStart = ranges[0].dtStart,
            dtEnd = ranges[^1].dtEnd,
        };
        result.variableLabels.AddRange(model.variables.Select(
            v => string.IsNullOrEmpty(v.szUnit) ? v.szLabel : $"{v.szLabel} ({v.szUnit})"));

        var dIntercept = model.dIntercept.Value;
        var dCoefficients = model.variables.Select(v => v.dCoefficient!.Value).ToArray();
        var dCumSavings = 0.0;
        foreach (var (range, i) in ranges.Select((r, i) => (r, i)))
        {
            var bucket = new EnPIReportBucket
            {
                dtBucketStart = range.dtStart,
                szLabel = labels[i],
            };
            for (var j = 0; j < xSeries.Count; j++)
                bucket.xValues.Add(xSeries[j][i]);

            var dActual = yValues[i];
            var bAllXPresent = xSeries.All(s => s[i] != null);
            if (dActual == null || !bAllXPresent)
            {
                bucket.isMissing = true;
                bucket.dActual = dActual;
                bucket.dCumulativeSavings = Math.Round(dCumSavings, 4);
                result.nMissingCount++;
                result.buckets.Add(bucket);
                continue;
            }

            var dPredicted = BaselineRegressionEngine.Predict(
                dIntercept, dCoefficients, xSeries.Select(s => s[i]!.Value).ToArray());
            var dSavings = dPredicted - dActual.Value;
            dCumSavings += dSavings;

            bucket.dActual = Math.Round(dActual.Value, 4);
            bucket.dPredicted = Math.Round(dPredicted, 4);
            bucket.dSavings = Math.Round(dSavings, 4);
            bucket.dCumulativeSavings = Math.Round(dCumSavings, 4);
            bucket.dEnpi = dPredicted > 1e-9 ? Math.Round(dActual.Value / dPredicted, 4) : null;

            result.dTotalActual += dActual.Value;
            result.dTotalPredicted += dPredicted;
            result.buckets.Add(bucket);
        }

        result.dTotalActual = Math.Round(result.dTotalActual, 3);
        result.dTotalPredicted = Math.Round(result.dTotalPredicted, 3);
        result.dTotalSavings = Math.Round(result.dTotalPredicted - result.dTotalActual, 3);
        result.dOverallEnpi = result.dTotalPredicted > 1e-9
            ? Math.Round(result.dTotalActual / result.dTotalPredicted, 4) : null;

        _logger.LogInformation("EnPI 報告完成 BaselineId={Id} buckets={N} 缺值={Missing} 累計節能={Savings}",
            nBaselineId, result.buckets.Count, result.nMissingCount, result.dTotalSavings);
        return result;
    }
}
