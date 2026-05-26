using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using NCalc;
using ScadaEngine.Common.Data.Models;
using ScadaEngine.Engine.Data.Interfaces;

namespace ScadaEngine.Web.Services;

/// <summary>
/// 計算點位 Web 端服務 — 封裝 CRUD 操作與公式預覽
/// </summary>
public class CalcPointService
{
    private readonly IDataRepository _repository;
    private readonly ILogger<CalcPointService> _logger;
    private readonly IStringLocalizer<CalcPointService> _l;

    public CalcPointService(
        IDataRepository repository,
        ILogger<CalcPointService> logger,
        IStringLocalizer<CalcPointService> localizer)
    {
        _repository = repository;
        _logger = logger;
        _l = localizer;
    }

    /// <summary>
    /// 取得所有計算點位
    /// </summary>
    public async Task<IEnumerable<CalculatedPointModel>> GetAllAsync()
    {
        return await _repository.GetAllCalculatedPointsAsync();
    }

    /// <summary>
    /// 新增計算點位（SID 自動產生）
    /// </summary>
    public async Task<(bool isSuccess, string szMessage, string? szSID)> CreateAsync(
        string szName, string? szUnit, string? szGroupName, string szFormula, string szInputMappings)
    {
        if (string.IsNullOrWhiteSpace(szName))
            return (false, _l["calcpoint.svc.name_empty"].Value, null);
        if (string.IsNullOrWhiteSpace(szFormula))
            return (false, _l["calcpoint.svc.formula_empty"].Value, null);

        // 驗證 InputMappings JSON
        try
        {
            var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(szInputMappings);
            if (mappings == null || mappings.Count == 0)
                return (false, _l["calcpoint.svc.no_variables"].Value, null);
        }
        catch (JsonException)
        {
            return (false, _l["calcpoint.svc.input_format_error"].Value, null);
        }

        // 產生下一個 SID
        var nMaxIndex = await _repository.GetCalculatedPointMaxIndexAsync();
        var szSID = $"CALC-S{nMaxIndex + 1}";

        var model = new CalculatedPointModel
        {
            szSID = szSID,
            szName = szName,
            szUnit = szUnit ?? "",
            szGroupName = szGroupName ?? "Calculated",
            szFormula = szFormula,
            szInputMappings = szInputMappings,
            isEnabled = true
        };

        var isSuccess = await _repository.CreateCalculatedPointAsync(model);
        return isSuccess
            ? (true, _l["calcpoint.svc.add_success"].Value, szSID)
            : (false, _l["calcpoint.svc.add_failed"].Value, null);
    }

    /// <summary>
    /// 更新計算點位
    /// </summary>
    public async Task<(bool isSuccess, string szMessage)> UpdateAsync(
        string szSID, string szName, string? szUnit, string? szGroupName,
        string szFormula, string szInputMappings, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(szSID))
            return (false, _l["calcpoint.svc.sid_empty"].Value);
        if (string.IsNullOrWhiteSpace(szName))
            return (false, _l["calcpoint.svc.name_empty"].Value);
        if (string.IsNullOrWhiteSpace(szFormula))
            return (false, _l["calcpoint.svc.formula_empty"].Value);

        var model = new CalculatedPointModel
        {
            szSID = szSID,
            szName = szName,
            szUnit = szUnit ?? "",
            szGroupName = szGroupName ?? "Calculated",
            szFormula = szFormula,
            szInputMappings = szInputMappings,
            isEnabled = isEnabled
        };

        var isSuccess = await _repository.UpdateCalculatedPointAsync(model);
        return isSuccess
            ? (true, _l["calcpoint.svc.update_success"].Value)
            : (false, _l["calcpoint.svc.update_failed"].Value);
    }

    /// <summary>
    /// 刪除計算點位
    /// </summary>
    public async Task<(bool isSuccess, string szMessage)> DeleteAsync(string szSID)
    {
        if (string.IsNullOrWhiteSpace(szSID))
            return (false, _l["calcpoint.svc.sid_empty"].Value);

        var isSuccess = await _repository.DeleteCalculatedPointAsync(szSID);
        return isSuccess
            ? (true, _l["calcpoint.svc.delete_success"].Value)
            : (false, _l["calcpoint.svc.delete_failed"].Value);
    }

    /// <summary>
    /// 公式即時預覽 — 帶入 LatestData 的值試算結果
    /// </summary>
    public async Task<(bool isSuccess, string szMessage, float? fResult)> PreviewAsync(string szFormula, string szInputMappings)
    {
        try
        {
            var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(szInputMappings);
            if (mappings == null || mappings.Count == 0)
                return (false, _l["calcpoint.svc.no_variables"].Value, null);

            // 從 LatestData 取得最新值
            var latestDataList = await _repository.GetLatestDataAsync(10000);
            var latestDict = latestDataList.ToDictionary(d => d.szSID, d => d.fValue);

            var szSafeFormula = WrapFormulaVariables(szFormula, mappings.Keys);
            var expression = new Expression(szSafeFormula);
            foreach (var kvp in mappings)
            {
                if (latestDict.TryGetValue(kvp.Value, out var fValue))
                {
                    expression.Parameters[kvp.Key] = (double)fValue;
                }
                else
                {
                    expression.Parameters[kvp.Key] = 0.0;
                }
            }

            var objResult = expression.Evaluate();
            var fResult = Convert.ToSingle(objResult);

            if (float.IsNaN(fResult) || float.IsInfinity(fResult))
                return (false, _l["calcpoint.svc.calc_invalid"].Value, null);

            return (true, _l["calcpoint.svc.calc_success"].Value, fResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "公式預覽失敗: {Formula}", szFormula);
            return (false, _l["calcpoint.svc.calc_failed_with", ex.Message].Value, null);
        }
    }

    /// <summary>
    /// 將公式中的變數名稱包裹為 NCalc bracket 語法 [varName]，
    /// 解決變數名稱以數字開頭或含特殊字元時 NCalc 無法解析的問題
    /// </summary>
    private static string WrapFormulaVariables(string szFormula, IEnumerable<string> varNames)
    {
        var szResult = szFormula;
        foreach (var szName in varNames.OrderByDescending(n => n.Length))
        {
            var szPattern = @"(?<!\[)\b" + Regex.Escape(szName) + @"\b(?!\])";
            szResult = Regex.Replace(szResult, szPattern, "[" + szName + "]");
        }
        return szResult;
    }
}
