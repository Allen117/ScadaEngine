using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// C# 演算法狀態（與 Engine 內部呼叫端共用的中性 DTO，與動態編譯出的 ScadaEngine.Algorithms.* 型別解耦）。
/// </summary>
public record CSharpAlgorithmStatus(int CodeId, string CodeName, string Severity)
{
    public static CSharpAlgorithmStatus Ok { get; } = new(0, "OK", "Info");
    public static CSharpAlgorithmStatus DivideByZero { get; } = new(10, "DIVIDE_BY_ZERO", "Error");
    public static CSharpAlgorithmStatus InputMissing { get; } = new(11, "INPUT_MISSING", "Error");
    public static CSharpAlgorithmStatus NumericOverflow { get; } = new(30, "NUMERIC_OVERFLOW", "Error");
    public static CSharpAlgorithmStatus InternalError { get; } = new(90, "INTERNAL_ERROR", "Error");

    private static int RankOf(string severity) => severity switch
    {
        "Info"    => 1,
        "Warning" => 2,
        "Error"   => 3,
        _         => 0,
    };

    /// <summary>取嚴重度較高者；同級回傳 a。</summary>
    public static CSharpAlgorithmStatus Merge(CSharpAlgorithmStatus a, CSharpAlgorithmStatus b)
        => RankOf(b.Severity) > RankOf(a.Severity) ? b : a;
}

/// <summary>
/// C# 演算法動態編譯服務：啟動時掃描 Algorithms/**/*.cs，
/// 使用 Roslyn 編譯為 in-memory assembly，快取 EvaluateOne 反射呼叫資訊。
///
/// 唯一支援的演算法簽章：
///   public static AlgorithmResult EvaluateOne(double arg1, double arg2, ...)
///
/// 框架負責：
///   - 用 MethodInfo.GetParameters() 取參數名 → 從 inputs dict 取對應 key
///   - 變參演算法 (@variadic: true) 跑 1..n 迴圈（n 由 inputDict 內 numbered suffix 自動推導）
///   - 包 try/catch，例外 → AlgorithmStatus.FromException
///   - 結果 dict 任意值 IsInfinity / IsNaN → 升為 NumericOverflow
///   - 多組迭代用 Merge 取嚴重者
///
/// 共用源碼：_*.cs（如 _AlgorithmStatus.cs）不視為演算法，會被加入每個演算法的 syntaxTree。
/// </summary>
public class CSharpAlgorithmService
{
    private readonly ILogger<CSharpAlgorithmService> _logger;
    private readonly ConcurrentDictionary<string, AlgorithmEntry> _registry = new();
    private readonly List<SyntaxTree> _sharedSyntaxTrees = new();

    private static readonly Regex _reInputsRepeat = new(@"^\s*//\s*@inputs_repeat:\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex _reVariadic = new(@"^\s*//\s*@variadic:\s*(.+)$", RegexOptions.Compiled);

    public CSharpAlgorithmService(ILogger<CSharpAlgorithmService> logger)
    {
        _logger = logger;
        CompileAllAlgorithms();
    }

    // ─── 公開 API ─────────────────────────

    /// <summary>嘗試以 C# 演算法執行。找不到回傳 false；執行失敗仍回 true 但 status 帶錯誤碼。
    /// perOutput：每個輸出 key (含 variadic suffix) 的 status；非變參時所有 key 共用同一 status。</summary>
    public bool TryEvaluate(string szAlgoName, Dictionary<string, double> inputs,
                            out Dictionary<string, double> result,
                            out CSharpAlgorithmStatus status,
                            out Dictionary<string, CSharpAlgorithmStatus> perOutput)
    {
        result = new();
        status = CSharpAlgorithmStatus.Ok;
        perOutput = new();
        if (!_registry.TryGetValue(szAlgoName, out var entry)) return false;
        try
        {
            (result, status, perOutput) = entry.Invoke(inputs);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "C# 演算法 {Algo} dispatch 失敗", szAlgoName);
            status = CSharpAlgorithmStatus.InternalError;
            result = new Dictionary<string, double> { ["out"] = 0 };
            perOutput = new Dictionary<string, CSharpAlgorithmStatus> { ["out"] = status };
            return true;
        }
    }

    /// <summary>向後相容版本：不關心 perOutput。</summary>
    public bool TryEvaluate(string szAlgoName, Dictionary<string, double> inputs,
                            out Dictionary<string, double> result,
                            out CSharpAlgorithmStatus status)
        => TryEvaluate(szAlgoName, inputs, out result, out status, out _);

    /// <summary>向後相容版本：不關心 status，只取 result。</summary>
    public bool TryEvaluate(string szAlgoName, Dictionary<string, double> inputs,
                            out Dictionary<string, double> result)
        => TryEvaluate(szAlgoName, inputs, out result, out _, out _);

    /// <summary>是否已註冊指定名稱的 C# 演算法</summary>
    public bool IsRegistered(string szAlgoName) => _registry.ContainsKey(szAlgoName);

    // ─── Roslyn 編譯 ─────────────────────

    private void CompileAllAlgorithms()
    {
        var szAlgoDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Algorithms");
        if (!Directory.Exists(szAlgoDir))
            szAlgoDir = Path.Combine(Directory.GetCurrentDirectory(), "Algorithms");
        if (!Directory.Exists(szAlgoDir))
        {
            _logger.LogInformation("找不到 Algorithms 資料夾，C# 演算法服務未載入");
            return;
        }

        var allCsFiles = Directory.GetFiles(szAlgoDir, "*.cs", SearchOption.AllDirectories);
        if (allCsFiles.Length == 0)
        {
            _logger.LogInformation("Algorithms 中無 .cs 檔案");
            return;
        }

        // 共用源碼（_*.cs）先載入為 SyntaxTree，待會編每個演算法時一起編
        var sharedFiles = allCsFiles.Where(p => Path.GetFileName(p).StartsWith("_")).ToList();
        var algoFiles = allCsFiles.Where(p => !Path.GetFileName(p).StartsWith("_")).ToList();

        foreach (var szShared in sharedFiles)
        {
            try
            {
                var szSrc = File.ReadAllText(szShared);
                _sharedSyntaxTrees.Add(CSharpSyntaxTree.ParseText(szSrc));
                _logger.LogInformation("C# 演算法共用源碼已載入: {Name}", Path.GetFileName(szShared));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "C# 共用源碼讀取失敗: {File}", szShared);
            }
        }

        var references = GetMetadataReferences();

        foreach (var szFilePath in algoFiles)
        {
            try
            {
                CompileOne(szFilePath, references);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "C# 演算法編譯失敗: {File}", szFilePath);
            }
        }

        _logger.LogInformation("C# 演算法載入完成: {Count} 個", _registry.Count);
    }

    private void CompileOne(string szFilePath, List<MetadataReference> references)
    {
        var szFileName = Path.GetFileNameWithoutExtension(szFilePath);
        var szSourceCode = File.ReadAllText(szFilePath);

        // 解析 .cs 前 15 行的 metadata（@variadic / @inputs_repeat）
        var (isVariadic, repeatInputKeys) = ParseMetadataHeader(szSourceCode);

        var syntaxTrees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(szSourceCode) };
        syntaxTrees.AddRange(_sharedSyntaxTrees);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"Algo_{szFileName}_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            _logger.LogError("C# 演算法 {Name} 編譯錯誤:\n{Errors}",
                szFileName, string.Join("\n", errors));
            return;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        // 尋找 public static AlgorithmResult EvaluateOne(double, double, ...)
        MethodInfo? evalOne = null;
        Type? algorithmResultType = null;
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "EvaluateOne") continue;
                if (method.ReturnType.FullName != "ScadaEngine.Algorithms.AlgorithmResult") continue;
                var prms = method.GetParameters();
                if (prms.Length == 0) continue;
                if (!prms.All(p => p.ParameterType == typeof(double))) continue;
                evalOne = method;
                algorithmResultType = method.ReturnType;
                break;
            }
            if (evalOne != null) break;
        }

        if (evalOne == null || algorithmResultType == null)
        {
            _logger.LogWarning(
                "C# 演算法 {Name} 找不到符合簽章的 EvaluateOne 方法 (期望 public static AlgorithmResult EvaluateOne(double, double, ...))",
                szFileName);
            return;
        }

        var paramInfos = evalOne.GetParameters();
        var paramNames = paramInfos.Select(p => p.Name ?? "").ToArray();

        // 驗證反射可取到參數名稱
        if (paramNames.Any(string.IsNullOrEmpty))
        {
            _logger.LogError(
                "C# 演算法 {Name} 反射取不到 EvaluateOne 參數名稱（Roslyn 預設應保留，請檢查編譯選項）",
                szFileName);
            return;
        }

        var resultProp = algorithmResultType.GetProperty("Result");
        var statusProp = algorithmResultType.GetProperty("Status");
        if (resultProp == null || statusProp == null)
        {
            _logger.LogWarning("C# 演算法 {Name} AlgorithmResult 缺少 Result/Status 屬性", szFileName);
            return;
        }

        var statusType = statusProp.PropertyType;
        var codeIdProp = statusType.GetProperty("CodeId");
        var codeNameProp = statusType.GetProperty("CodeName");
        var severityProp = statusType.GetProperty("Severity");
        if (codeIdProp == null || codeNameProp == null || severityProp == null)
        {
            _logger.LogWarning("C# 演算法 {Name} AlgorithmStatus 缺少必要屬性", szFileName);
            return;
        }

        // 標記哪些參數是「重複參數」（出現在 @inputs_repeat），變參迭代時要加 suffix
        var isRepeatParam = paramNames.Select(n => repeatInputKeys.Contains(n)).ToArray();

        (Dictionary<string, double> result, CSharpAlgorithmStatus status) InvokeOnce(Dictionary<string, double> input, string suffix)
        {
            var args = new object[paramNames.Length];
            for (int i = 0; i < paramNames.Length; i++)
            {
                var key = isRepeatParam[i] ? paramNames[i] + suffix : paramNames[i];
                if (!input.TryGetValue(key, out var v))
                    return (ZeroFill(suffix), CSharpAlgorithmStatus.InputMissing);
                args[i] = v;
            }

            object? raw;
            try
            {
                raw = evalOne.Invoke(null, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                return (ZeroFill(suffix), MapException(tie.InnerException));
            }
            catch (Exception ex)
            {
                return (ZeroFill(suffix), MapException(ex));
            }

            if (raw == null) return (ZeroFill(suffix), CSharpAlgorithmStatus.InternalError);

            var resDict = (Dictionary<string, double>)resultProp.GetValue(raw)!;
            var stObj = statusProp.GetValue(raw)!;
            var codeId = (int)codeIdProp.GetValue(stObj)!;
            var codeName = (string)codeNameProp.GetValue(stObj)!;
            var severity = severityProp.GetValue(stObj)!.ToString() ?? "Info";
            var st = new CSharpAlgorithmStatus(codeId, codeName, severity);

            // inf / nan 檢查：C# double 除以 0 不丟例外（IEEE 754）→ 用 result 推導 status。
            //   - 任一輸入為 0 且結果非有限 → DivideByZero（對應 Python ZeroDivisionError 路徑）
            //   - 其他非有限 → NumericOverflow
            bool hasNonFiniteResult = false;
            foreach (var v in resDict.Values)
            {
                if (double.IsInfinity(v) || double.IsNaN(v)) { hasNonFiniteResult = true; break; }
            }
            if (hasNonFiniteResult)
            {
                bool hasZeroInput = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is double d && d == 0.0) { hasZeroInput = true; break; }
                }
                st = hasZeroInput ? CSharpAlgorithmStatus.DivideByZero : CSharpAlgorithmStatus.NumericOverflow;
                // 把非有限值替換成 0，與 Python _zero_fill 行為一致（HasUpstreamBad 會擋下游，但避免 inf 進入快取 / UI）
                var keys = resDict.Keys.ToList();
                foreach (var k in keys)
                {
                    if (double.IsInfinity(resDict[k]) || double.IsNaN(resDict[k]))
                        resDict[k] = 0;
                }
            }

            // 套 output suffix（變參時，所有 output key 一律加 suffix；目前 C# 無變參演算法，保留行為）
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixed = new Dictionary<string, double>(resDict.Count);
                foreach (var (k, v) in resDict)
                    suffixed[k + suffix] = v;
                resDict = suffixed;
            }

            return (resDict, st);
        }

        Dictionary<string, double> ZeroFill(string suffix)
        {
            // 預設輸出鍵 "out"；變參時 outN
            return new Dictionary<string, double> { [$"out{suffix}"] = 0 };
        }

        Func<Dictionary<string, double>, (Dictionary<string, double>, CSharpAlgorithmStatus, Dictionary<string, CSharpAlgorithmStatus>)> invoke;
        if (isVariadic)
        {
            // 從 inputDict 自動推導 n（找第一個 repeat 參數名 + numeric suffix 的最大 i）
            var firstRepeatName = paramNames.FirstOrDefault(n => repeatInputKeys.Contains(n));
            invoke = (input) =>
            {
                int n = 0;
                if (firstRepeatName != null)
                {
                    while (input.ContainsKey(firstRepeatName + (n + 1))) n++;
                }
                if (n == 0) n = 1;

                var merged = new Dictionary<string, double>();
                var mergedStatus = CSharpAlgorithmStatus.Ok;
                var perOutput = new Dictionary<string, CSharpAlgorithmStatus>();
                for (int i = 1; i <= n; i++)
                {
                    var (res, st) = InvokeOnce(input, i.ToString());
                    foreach (var (k, v) in res)
                    {
                        merged[k] = v;
                        // 該迭代產生的所有 output key 共用同一 status
                        perOutput[k] = st;
                    }
                    mergedStatus = CSharpAlgorithmStatus.Merge(mergedStatus, st);
                }
                return (merged, mergedStatus, perOutput);
            };
        }
        else
        {
            invoke = (input) =>
            {
                var (res, st) = InvokeOnce(input, "");
                var perOutput = new Dictionary<string, CSharpAlgorithmStatus>();
                foreach (var k in res.Keys) perOutput[k] = st;
                return (res, st, perOutput);
            };
        }

        _registry[szFileName] = new AlgorithmEntry { Invoke = invoke };
        _logger.LogInformation("C# 演算法已載入: {Name} (variadic={Variadic}, params=[{Params}])",
            szFileName, isVariadic, string.Join(",", paramNames));
    }

    /// <summary>解析 .cs 前 15 行的 @variadic / @inputs_repeat metadata。</summary>
    private static (bool isVariadic, HashSet<string> repeatInputKeys) ParseMetadataHeader(string szSourceCode)
    {
        bool isVariadic = false;
        var repeatKeys = new HashSet<string>();
        var lines = szSourceCode.Split('\n').Take(15);
        foreach (var line in lines)
        {
            var mv = _reVariadic.Match(line);
            if (mv.Success)
            {
                var v = mv.Groups[1].Value.Trim().ToLowerInvariant();
                isVariadic = v == "true" || v == "1" || v == "yes";
                continue;
            }
            var mr = _reInputsRepeat.Match(line);
            if (mr.Success)
            {
                foreach (var seg in mr.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = seg.IndexOf(':');
                    var key = idx >= 0 ? seg[..idx].Trim() : seg.Trim();
                    if (!string.IsNullOrEmpty(key)) repeatKeys.Add(key);
                }
            }
        }
        return (isVariadic, repeatKeys);
    }

    /// <summary>例外類型 → CSharpAlgorithmStatus（與 AlgorithmStatus.FromException 同步）。</summary>
    private static CSharpAlgorithmStatus MapException(Exception ex) => ex switch
    {
        DivideByZeroException   => CSharpAlgorithmStatus.DivideByZero,
        KeyNotFoundException    => CSharpAlgorithmStatus.InputMissing,
        ArgumentException       => CSharpAlgorithmStatus.InputMissing,
        InvalidCastException    => CSharpAlgorithmStatus.InputMissing,
        OverflowException       => CSharpAlgorithmStatus.NumericOverflow,
        _                       => CSharpAlgorithmStatus.InternalError,
    };

    private static List<MetadataReference> GetMetadataReferences()
    {
        var refs = new List<MetadataReference>();
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var neededAssemblies = new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "netstandard.dll",
        };

        foreach (var name in neededAssemblies)
        {
            var path = Path.Combine(assemblyPath, name);
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        // 確保核心型別 assembly 被載入
        refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location));

        return refs;
    }

    // ─── 內部型別 ─────────────────────

    private class AlgorithmEntry
    {
        public required Func<Dictionary<string, double>, (Dictionary<string, double>, CSharpAlgorithmStatus, Dictionary<string, CSharpAlgorithmStatus>)> Invoke { get; init; }
    }
}
