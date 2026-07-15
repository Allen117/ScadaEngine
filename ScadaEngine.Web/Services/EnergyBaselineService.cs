using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Common.Data.Services;
using ScadaEngine.Web.Features.EnergyBaseline.Models;

namespace ScadaEngine.Web.Services;

/// <summary>
/// ISO 50001 能源基線 — 模型 CRUD + 取樣 + 回歸執行 + 凍結 + SEU 鑑別。
/// 取樣粒度：day = 曆日、month = 曆月（刻意不走月結期別 — 基線需與電費期界脫鉤，
/// X 變數（外氣溫度等）也以自然時間對齊才有物理意義）。
/// Y/X 取樣規則：
///   circuit → EnergyReportService 計算核心（boundary 相減 + 溢位 + staleness window）
///   point + cumulative → HistoryData 期初期末相減（倒退視為重置 → 該 bucket 剔除）
///   point + average → HistoryData 期間均值（Quality=1）
/// 只取「已過完」的 bucket（訖 ≤ 現在），避免半天/半月樣本汙染回歸。
/// </summary>
public class EnergyBaselineService
{
    private const int MaxVariables = 5;

    private readonly ILogger<EnergyBaselineService> _logger;
    private readonly DatabaseConfigService _configService;
    private readonly EnergyCircuitService _circuitService;
    private readonly EnergyReportService _reportService;
    private readonly BaselineRegressionEngine _regressionEngine;
    private readonly int _nMaxStalenessHours;
    private string _szConnectionString = string.Empty;

    public EnergyBaselineService(
        ILogger<EnergyBaselineService> logger,
        DatabaseConfigService configService,
        EnergyCircuitService circuitService,
        EnergyReportService reportService,
        BaselineRegressionEngine regressionEngine,
        IConfiguration configuration)
    {
        _logger = logger;
        _configService = configService;
        _circuitService = circuitService;
        _reportService = reportService;
        _regressionEngine = regressionEngine;
        // 與 EnergyReportService 同一把 staleness window（點位累計取樣的邊界值有效期）
        _nMaxStalenessHours = configuration.GetValue<int?>("EnergyAggregation:MaxStalenessHours") ?? 2;
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (string.IsNullOrEmpty(_szConnectionString))
            _szConnectionString = await _configService.GetConnectionStringAsync();
        var conn = new SqlConnection(_szConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ============================================================
    // CRUD
    // ============================================================

    private const string SelectBaselineSql = @"
        SELECT Id AS nId, Name AS szName, TargetType AS szTargetType, TargetSID AS szTargetSID,
               TargetCircuitId AS nTargetCircuitId, TargetMode AS szTargetMode,
               TargetLabel AS szTargetLabel, TargetUnit AS szTargetUnit,
               Granularity AS szGranularity, BaselineStart AS dtBaselineStart, BaselineEnd AS dtBaselineEnd,
               Status AS szStatus, Intercept AS dIntercept, R2 AS dR2, AdjR2 AS dAdjR2,
               CvRmse AS dCvRmse, SampleCount AS nSampleCount, FrozenAt AS dtFrozenAt,
               CreatedAt AS dtCreatedAt, UpdatedAt AS dtUpdatedAt, Description AS szDescription
        FROM EnergyBaseline WITH (NOLOCK)";

    private const string SelectVariableSql = @"
        SELECT Id AS nId, BaselineId AS nBaselineId, Sequence AS nSequence, VarType AS szVarType,
               SourceSID AS szSourceSID, SourceCircuitId AS nSourceCircuitId,
               Label AS szLabel, Unit AS szUnit, Coefficient AS dCoefficient, PValue AS dPValue
        FROM EnergyBaselineVariable WITH (NOLOCK)";

    public async Task<List<EnergyBaselineModel>> GetAllAsync()
    {
        using var conn = await GetConnectionAsync();
        var models = (await conn.QueryAsync<EnergyBaselineModel>(
            SelectBaselineSql + " ORDER BY Id DESC")).ToList();
        if (models.Count == 0) return models;

        var vars = await conn.QueryAsync<EnergyBaselineVariableModel>(
            SelectVariableSql + " ORDER BY BaselineId, Sequence");
        var lookup = vars.GroupBy(v => v.nBaselineId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var m in models)
            m.variables = lookup.TryGetValue(m.nId, out var list) ? list : new List<EnergyBaselineVariableModel>();
        return models;
    }

    public async Task<EnergyBaselineModel?> GetByIdAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        var model = await conn.QuerySingleOrDefaultAsync<EnergyBaselineModel>(
            SelectBaselineSql + " WHERE Id = @Id", new { Id = nId });
        if (model == null) return null;
        model.variables = (await conn.QueryAsync<EnergyBaselineVariableModel>(
            SelectVariableSql + " WHERE BaselineId = @Id ORDER BY Sequence", new { Id = nId })).ToList();
        return model;
    }

    /// <summary>新增模型（草稿）。回傳新 Id。</summary>
    public async Task<int> CreateAsync(EnergyBaselineModel model)
    {
        ValidateDefinition(model);
        using var conn = await GetConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            var nId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO EnergyBaseline
                    (Name, TargetType, TargetSID, TargetCircuitId, TargetMode, TargetLabel, TargetUnit,
                     Granularity, BaselineStart, BaselineEnd, Status, CreatedAt, Description)
                VALUES
                    (@szName, @szTargetType, @szTargetSID, @nTargetCircuitId, @szTargetMode, @szTargetLabel, @szTargetUnit,
                     @szGranularity, @dtBaselineStart, @dtBaselineEnd, 'draft', GETDATE(), @szDescription);
                SELECT CAST(SCOPE_IDENTITY() AS int);", model, tx);

            await InsertVariablesAsync(conn, tx, nId, model.variables);
            tx.Commit();
            return nId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>更新模型定義（僅 draft）。定義變更 → 既有回歸結果一併清空。</summary>
    public async Task UpdateAsync(EnergyBaselineModel model)
    {
        ValidateDefinition(model);
        var existing = await GetByIdAsync(model.nId)
            ?? throw new InvalidOperationException($"基線模型 Id={model.nId} 不存在");
        if (existing.szStatus == "frozen")
            throw new InvalidOperationException("模型已凍結，須先解除凍結才能修改定義");

        using var conn = await GetConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(@"
                UPDATE EnergyBaseline SET
                    Name = @szName, TargetType = @szTargetType, TargetSID = @szTargetSID,
                    TargetCircuitId = @nTargetCircuitId, TargetMode = @szTargetMode,
                    TargetLabel = @szTargetLabel, TargetUnit = @szTargetUnit,
                    Granularity = @szGranularity, BaselineStart = @dtBaselineStart, BaselineEnd = @dtBaselineEnd,
                    Description = @szDescription, UpdatedAt = GETDATE(),
                    Intercept = NULL, R2 = NULL, AdjR2 = NULL, CvRmse = NULL, SampleCount = NULL
                WHERE Id = @nId", model, tx);

            await conn.ExecuteAsync("DELETE FROM EnergyBaselineVariable WHERE BaselineId = @Id",
                new { Id = model.nId }, tx);
            await InsertVariablesAsync(conn, tx, model.nId, model.variables);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync("DELETE FROM EnergyBaselineVariable WHERE BaselineId = @Id", new { Id = nId }, tx);
            await conn.ExecuteAsync("DELETE FROM EnergyBaseline WHERE Id = @Id", new { Id = nId }, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>凍結 — 需已有回歸結果。凍結後係數固定，EnPI 報告一律用凍結係數。</summary>
    public async Task FreezeAsync(int nId)
    {
        var model = await GetByIdAsync(nId)
            ?? throw new InvalidOperationException($"基線模型 Id={nId} 不存在");
        if (model.szStatus == "frozen") return;
        if (model.dIntercept == null)
            throw new InvalidOperationException("尚未執行回歸，無係數可凍結 — 請先「建立基線」");

        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE EnergyBaseline SET Status = 'frozen', FrozenAt = GETDATE(), UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = nId });
    }

    /// <summary>解除凍結回草稿（係數保留，但可重算覆蓋）。</summary>
    public async Task UnfreezeAsync(int nId)
    {
        using var conn = await GetConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE EnergyBaseline SET Status = 'draft', FrozenAt = NULL, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = nId });
    }

    private static void ValidateDefinition(EnergyBaselineModel model)
    {
        if (string.IsNullOrWhiteSpace(model.szName))
            throw new InvalidOperationException("模型名稱不可空白");
        if (model.szTargetType != "circuit" && model.szTargetType != "point")
            throw new InvalidOperationException("Y 來源型別須為 circuit 或 point");
        if (model.szTargetType == "circuit" && model.nTargetCircuitId == null)
            throw new InvalidOperationException("請選擇 Y 迴路");
        if (model.szTargetType == "point" && string.IsNullOrWhiteSpace(model.szTargetSID))
            throw new InvalidOperationException("請選擇 Y 點位");
        if (model.szGranularity != "day" && model.szGranularity != "month")
            throw new InvalidOperationException("粒度須為 day 或 month");
        if (model.dtBaselineEnd < model.dtBaselineStart)
            throw new InvalidOperationException("基線期訖不可早於起");
        if (model.variables.Count < 1)
            throw new InvalidOperationException("至少需要一個相關變數 X");
        if (model.variables.Count > MaxVariables)
            throw new InvalidOperationException($"相關變數 X 最多 {MaxVariables} 個");
        foreach (var v in model.variables)
        {
            if (v.szVarType == "point" && string.IsNullOrWhiteSpace(v.szSourceSID))
                throw new InvalidOperationException("有相關變數尚未選擇點位");
            if (v.szVarType == "circuit" && v.nSourceCircuitId == null)
                throw new InvalidOperationException("有相關變數尚未選擇迴路");
        }
        // circuit 目標一律累計（kWh boundary 相減），防前端誤送
        if (model.szTargetType == "circuit")
            model.szTargetMode = "cumulative";
    }

    private static async Task InsertVariablesAsync(
        SqlConnection conn, SqlTransaction tx, int nBaselineId, List<EnergyBaselineVariableModel> variables)
    {
        var nSeq = 1;
        foreach (var v in variables)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO EnergyBaselineVariable
                    (BaselineId, Sequence, VarType, SourceSID, SourceCircuitId, Label, Unit)
                VALUES (@BaselineId, @Sequence, @VarType, @SourceSID, @SourceCircuitId, @Label, @Unit)",
                new
                {
                    BaselineId = nBaselineId,
                    Sequence = nSeq++,
                    VarType = v.szVarType,
                    SourceSID = v.szVarType == "point" ? v.szSourceSID : null,
                    SourceCircuitId = v.szVarType == "circuit" ? v.nSourceCircuitId : null,
                    Label = v.szLabel,
                    Unit = v.szUnit,
                }, tx);
        }
    }

    // ============================================================
    // 取樣
    // ============================================================

    /// <summary>
    /// 產生取樣 bucket 邊界對與標籤。day = 曆日 [00:00, 翌日 00:00)、month = 曆月 [1 號, 翌月 1 號)。
    /// dtStart/dtEnd 皆取「含」語意（含當日 / 含當月）。
    /// </summary>
    public static (List<(DateTime dtStart, DateTime dtEnd)> ranges, List<string> labels)
        BuildRanges(string szGranularity, DateTime dtStart, DateTime dtEnd)
    {
        var ranges = new List<(DateTime, DateTime)>();
        var labels = new List<string>();
        var ci = CultureInfo.InvariantCulture;
        if (szGranularity == "month")
        {
            var t = new DateTime(dtStart.Year, dtStart.Month, 1);
            var tEnd = new DateTime(dtEnd.Year, dtEnd.Month, 1);
            for (; t <= tEnd; t = t.AddMonths(1))
            {
                ranges.Add((t, t.AddMonths(1)));
                labels.Add(t.ToString("yyyy-MM", ci));
            }
        }
        else
        {
            var t = dtStart.Date;
            var tEnd = dtEnd.Date;
            for (; t <= tEnd; t = t.AddDays(1))
            {
                ranges.Add((t, t.AddDays(1)));
                labels.Add(t.ToString("yyyy-MM-dd", ci));
            }
        }
        return (ranges, labels);
    }

    /// <summary>
    /// 對單一資料序列（Y 或某個 X）取樣，回傳與 ranges 等長的陣列，取不到值的 bucket 為 null。
    /// </summary>
    public async Task<double?[]> SampleSeriesAsync(
        string szVarType, string? szSid, int? nCircuitId, string szMode,
        List<(DateTime dtStart, DateTime dtEnd)> ranges)
    {
        if (szVarType == "circuit")
        {
            var (sums, staleFlags) = await _reportService.GetBucketKwhForRangesAsync(nCircuitId!.Value, ranges);
            var result = new double?[ranges.Count];
            for (var i = 0; i < ranges.Count; i++)
                result[i] = staleFlags[i] ? null : Math.Round(sums[i], 4);
            return result;
        }

        if (string.IsNullOrWhiteSpace(szSid))
            throw new InvalidOperationException("點位 SID 空白，無法取樣");

        return szMode == "cumulative"
            ? await GetBoundaryDeltasAsync(szSid, ranges)
            : await GetBucketAveragesAsync(szSid, ranges);
    }

    /// <summary>
    /// 累計型點位 — 每 bucket 期初期末相減。邊界最近值套 staleness window；
    /// 值倒退（電表重置/換錶）該 bucket 視為無效（任意點位無 MaxKwh 溢位資訊，不做補正）。
    /// </summary>
    private async Task<double?[]> GetBoundaryDeltasAsync(string szSid, List<(DateTime dtStart, DateTime dtEnd)> ranges)
    {
        var times = ranges.SelectMany(r => new[] { r.dtStart, r.dtEnd }).Distinct().OrderBy(t => t).ToList();
        var timeIndex = new Dictionary<DateTime, int>(times.Count);
        for (var i = 0; i < times.Count; i++) timeIndex[times[i]] = i;

        using var conn = await GetConnectionAsync();
        var sb = new System.Text.StringBuilder();
        var dynParams = new DynamicParameters();
        dynParams.Add("@sid", szSid);
        dynParams.Add("@maxStalenessHours", _nMaxStalenessHours);
        sb.Append("SELECT b.idx, ba.Value FROM (VALUES ");
        for (var i = 0; i < times.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@i{i}, @t{i})");
            dynParams.Add($"@i{i}", i);
            dynParams.Add($"@t{i}", times[i]);
        }
        sb.Append(@") AS b(idx, BoundaryTime)
                   OUTER APPLY (
                       SELECT TOP 1 Value FROM HistoryData WITH (NOLOCK)
                       WHERE  SID = @sid
                          AND Timestamp <= b.BoundaryTime
                          AND Timestamp >= DATEADD(HOUR, -@maxStalenessHours, b.BoundaryTime)
                          AND Quality = 1
                       ORDER BY Timestamp DESC
                   ) ba
                   ORDER BY b.idx");

        var rows = await conn.QueryAsync<(int idx, double? Value)>(sb.ToString(), dynParams);
        var boundaryValues = new double?[times.Count];
        foreach (var r in rows) boundaryValues[r.idx] = r.Value;

        var result = new double?[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            var dStart = boundaryValues[timeIndex[ranges[i].dtStart]];
            var dEnd = boundaryValues[timeIndex[ranges[i].dtEnd]];
            if (dStart == null || dEnd == null || dEnd.Value < dStart.Value)
                continue;   // 缺值或倒退 → 該 bucket 無效
            result[i] = Math.Round(dEnd.Value - dStart.Value, 4);
        }
        return result;
    }

    /// <summary>瞬時型點位 — 每 bucket 期間均值（Quality=1）。無資料的 bucket 為 null。</summary>
    private async Task<double?[]> GetBucketAveragesAsync(string szSid, List<(DateTime dtStart, DateTime dtEnd)> ranges)
    {
        var dtMin = ranges[0].dtStart;
        var dtMax = ranges[^1].dtEnd;
        var isMonthly = ranges[0].dtEnd - ranges[0].dtStart > TimeSpan.FromDays(1.5);

        using var conn = await GetConnectionAsync();
        // 曆日/曆月對齊 → 直接以日期/年月分組，一條 SQL 撈完全部 bucket
        var szSql = isMonthly
            ? @"SELECT DATEFROMPARTS(YEAR(Timestamp), MONTH(Timestamp), 1) AS k, AVG(Value) AS v
                FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Timestamp >= @dtMin AND Timestamp < @dtMax AND Quality = 1
                GROUP BY DATEFROMPARTS(YEAR(Timestamp), MONTH(Timestamp), 1)"
            : @"SELECT CONVERT(date, Timestamp) AS k, AVG(Value) AS v
                FROM HistoryData WITH (NOLOCK)
                WHERE SID = @sid AND Timestamp >= @dtMin AND Timestamp < @dtMax AND Quality = 1
                GROUP BY CONVERT(date, Timestamp)";

        var rows = await conn.QueryAsync<(DateTime k, double? v)>(szSql, new { sid = szSid, dtMin, dtMax });
        var map = rows.Where(r => r.v != null).ToDictionary(r => r.k.Date, r => r.v!.Value);

        var result = new double?[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            if (map.TryGetValue(ranges[i].dtStart.Date, out var v))
                result[i] = Math.Round(v, 4);
        }
        return result;
    }

    // ============================================================
    // 回歸執行
    // ============================================================

    /// <summary>
    /// 對草稿模型執行基線回歸：取樣 → OLS → 統計量/係數回填 DB → 回傳完整結果（含散布圖資料）。
    /// </summary>
    public async Task<RegressionRunResponse> RunRegressionAsync(int nId)
    {
        var model = await GetByIdAsync(nId)
            ?? throw new InvalidOperationException($"基線模型 Id={nId} 不存在");
        if (model.szStatus == "frozen")
            throw new InvalidOperationException("模型已凍結，不可重算 — 需重算請先解除凍結");
        if (model.variables.Count == 0)
            throw new InvalidOperationException("模型沒有相關變數 X");

        var (allRanges, allLabels) = BuildRanges(model.szGranularity, model.dtBaselineStart, model.dtBaselineEnd);

        // 只取已過完的 bucket，未過完的整段剔除（半桶樣本會拉低 Y 汙染回歸）
        var dtNow = DateTime.Now;
        var ranges = new List<(DateTime dtStart, DateTime dtEnd)>();
        var labels = new List<string>();
        for (var i = 0; i < allRanges.Count; i++)
        {
            if (allRanges[i].dtEnd > dtNow) continue;
            ranges.Add(allRanges[i]);
            labels.Add(allLabels[i]);
        }
        var nIncomplete = allRanges.Count - ranges.Count;
        if (ranges.Count == 0)
            throw new InvalidOperationException("基線期內沒有任何已過完的取樣區間");

        // Y + 各 X 取樣
        var yValues = await SampleSeriesAsync(model.szTargetType, model.szTargetSID,
            model.nTargetCircuitId, model.szTargetMode, ranges);
        var xSeries = new List<double?[]>(model.variables.Count);
        foreach (var v in model.variables)
            xSeries.Add(await SampleSeriesAsync(v.szVarType, v.szSourceSID, v.nSourceCircuitId, "average", ranges));

        // 組完整樣本列（Y 與所有 X 皆有值才收）
        var usedLabels = new List<string>();
        var usedY = new List<double>();
        var usedX = new List<List<double>>();
        for (var i = 0; i < model.variables.Count; i++) usedX.Add(new List<double>());
        for (var i = 0; i < ranges.Count; i++)
        {
            if (yValues[i] == null || xSeries.Any(s => s[i] == null)) continue;
            usedLabels.Add(labels[i]);
            usedY.Add(yValues[i]!.Value);
            for (var j = 0; j < xSeries.Count; j++)
                usedX[j].Add(xSeries[j][i]!.Value);
        }
        var nDropped = ranges.Count - usedY.Count;

        var fit = _regressionEngine.Fit(usedY.ToArray(), usedX.Select(c => c.ToArray()).ToList());

        // 統計量 + 係數回填 DB
        using (var conn = await GetConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await conn.ExecuteAsync(@"
                    UPDATE EnergyBaseline SET
                        Intercept = @Intercept, R2 = @R2, AdjR2 = @AdjR2, CvRmse = @CvRmse,
                        SampleCount = @SampleCount, UpdatedAt = GETDATE()
                    WHERE Id = @Id",
                    new
                    {
                        Id = nId,
                        Intercept = fit.dIntercept,
                        R2 = fit.dR2,
                        AdjR2 = fit.dAdjR2,
                        CvRmse = double.IsNaN(fit.dCvRmse) ? (double?)null : fit.dCvRmse,
                        SampleCount = fit.nSampleCount,
                    }, tx);
                for (var i = 0; i < model.variables.Count; i++)
                {
                    await conn.ExecuteAsync(@"
                        UPDATE EnergyBaselineVariable SET Coefficient = @Coef, PValue = @PValue WHERE Id = @Id",
                        new
                        {
                            Id = model.variables[i].nId,
                            Coef = fit.dCoefficients[i],
                            PValue = double.IsNaN(fit.dPValues[i]) ? (double?)null : fit.dPValues[i],
                        }, tx);
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // 散布圖：實際 vs 預測
        var response = new RegressionRunResponse
        {
            intercept = fit.dIntercept,
            r2 = fit.dR2,
            adjR2 = fit.dAdjR2,
            cvRmse = double.IsNaN(fit.dCvRmse) ? null : fit.dCvRmse,
            sampleCount = fit.nSampleCount,
            droppedCount = nDropped,
            incompleteCount = nIncomplete,
            isSampleLow = fit.nSampleCount < model.variables.Count * 5,
            isR2Low = fit.dR2 < 0.5,
        };
        for (var i = 0; i < model.variables.Count; i++)
        {
            response.variables.Add(new RegressionVariableResult
            {
                label = model.variables[i].szLabel,
                unit = model.variables[i].szUnit,
                coefficient = fit.dCoefficients[i],
                pValue = double.IsNaN(fit.dPValues[i]) ? null : fit.dPValues[i],
            });
        }
        for (var i = 0; i < usedY.Count; i++)
        {
            var xRow = new double[model.variables.Count];
            for (var j = 0; j < xRow.Length; j++) xRow[j] = usedX[j][i];
            response.scatter.Add(new RegressionScatterPoint
            {
                label = usedLabels[i],
                actual = Math.Round(usedY[i], 4),
                predicted = Math.Round(BaselineRegressionEngine.Predict(fit.dIntercept, fit.dCoefficients, xRow), 4),
            });
        }

        _logger.LogInformation("能源基線回歸完成 Id={Id} n={N} R2={R2:F4} AdjR2={AdjR2:F4}",
            nId, fit.nSampleCount, fit.dR2, fit.dAdjR2);
        return response;
    }

    // ============================================================
    // SEU 重大能源使用鑑別（帕累托）
    // ============================================================

    /// <summary>
    /// SEU 鑑別：以主要電表直接子迴路（無主要電表則所有根迴路）為能源使用單位，
    /// 期間 kWh 降冪排序，累計占比達門檻（預設 80%）前的項目（含跨越門檻項）鑑別為 SEU。
    /// </summary>
    public async Task<SeuAnalysisResult> GetSeuAnalysisAsync(DateTime dtStart, DateTime dtEnd, double dThresholdPct)
    {
        if (dThresholdPct <= 0 || dThresholdPct > 100) dThresholdPct = 80;

        var meter = await _circuitService.GetMainMeterAsync();
        List<EnergyCircuitModel> units;
        string szSourceName;
        if (meter != null)
        {
            units = await _circuitService.GetDirectChildrenAsync(meter.nId);
            szSourceName = meter.szName;
            if (units.Count == 0) units = new List<EnergyCircuitModel> { meter };
        }
        else
        {
            units = (await _circuitService.GetAllAsync()).Where(c => c.nParentId == null).ToList();
            szSourceName = string.Empty;
        }

        var result = new SeuAnalysisResult { threshold = dThresholdPct, sourceName = szSourceName };
        if (units.Count == 0) return result;
        result.hasSource = true;

        // 期間切成曆月對齊 chunk 再加總 — 兼顧溢位處理正確性與查詢量
        var ranges = BuildMonthAlignedRanges(dtStart.Date, dtEnd.Date.AddDays(1));
        var items = new List<SeuItem>();
        foreach (var u in units)
        {
            var (sums, _) = await _reportService.GetBucketKwhForRangesAsync(u.nId, ranges);
            // GetBucketKwhForRangesAsync 未套迴路自身對父層的 Sign，這裡補乘（同 EMS breakdown 慣例）
            var nSign = u.nSign == -1 ? -1 : 1;
            items.Add(new SeuItem { id = u.nId, name = u.szName, kwh = Math.Round(sums.Sum() * nSign, 3) });
        }

        var dTotal = items.Where(i => i.kwh > 0).Sum(i => i.kwh);
        result.totalKwh = Math.Round(dTotal, 3);
        var dCum = 0.0;
        var bThresholdReached = false;
        foreach (var item in items.OrderByDescending(i => i.kwh))
        {
            if (item.kwh > 0 && dTotal > 0)
            {
                item.pct = Math.Round(item.kwh / dTotal * 100, 2);
                dCum += item.kwh / dTotal * 100;
                item.cumPct = Math.Round(dCum, 2);
                if (!bThresholdReached)
                {
                    item.isSeu = true;   // 累計達門檻前的項目 + 跨越門檻那一項
                    if (dCum >= dThresholdPct) bThresholdReached = true;
                }
            }
            else
            {
                item.cumPct = Math.Round(dCum, 2);
            }
            result.items.Add(item);
        }
        return result;
    }

    /// <summary>把 [dtStart, dtEndExclusive) 切成曆月對齊的 chunk（首尾可為部分月）</summary>
    public static List<(DateTime dtStart, DateTime dtEnd)> BuildMonthAlignedRanges(DateTime dtStart, DateTime dtEndExclusive)
    {
        var ranges = new List<(DateTime, DateTime)>();
        var t = dtStart;
        while (t < dtEndExclusive)
        {
            var dtNextMonth = new DateTime(t.Year, t.Month, 1).AddMonths(1);
            var dtChunkEnd = dtNextMonth < dtEndExclusive ? dtNextMonth : dtEndExclusive;
            ranges.Add((t, dtChunkEnd));
            t = dtChunkEnd;
        }
        return ranges;
    }
}
