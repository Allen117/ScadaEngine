using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ScadaEngine.Engine.Services;

/// <summary>
/// C# 演算法動態編譯服務：啟動時掃描 Algorithms/**/*.cs，
/// 使用 Roslyn 編譯為 in-memory assembly，快取 Evaluate delegate。
/// </summary>
public class CSharpAlgorithmService
{
    private readonly ILogger<CSharpAlgorithmService> _logger;
    private readonly ConcurrentDictionary<string, AlgorithmEntry> _registry = new();

    public CSharpAlgorithmService(ILogger<CSharpAlgorithmService> logger)
    {
        _logger = logger;
        CompileAllAlgorithms();
    }

    // ─── 公開 API ─────────────────────────

    /// <summary>嘗試以 C# 演算法執行。找不到或失敗回傳 false。</summary>
    public bool TryEvaluate(string szAlgoName, Dictionary<string, double> inputs,
                            out Dictionary<string, double> result)
    {
        result = new();
        if (!_registry.TryGetValue(szAlgoName, out var entry)) return false;
        try
        {
            result = entry.EvaluateDelegate(inputs);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "C# 演算法 {Algo} 執行失敗", szAlgoName);
            return false;
        }
    }

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

        var csFiles = Directory.GetFiles(szAlgoDir, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
        {
            _logger.LogInformation("Algorithms 中無 .cs 檔案");
            return;
        }

        var references = GetMetadataReferences();

        foreach (var szFilePath in csFiles)
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

        var syntaxTree = CSharpSyntaxTree.ParseText(szSourceCode);
        var compilation = CSharpCompilation.Create(
            assemblyName: $"Algo_{szFileName}_{Guid.NewGuid():N}",
            syntaxTrees: new[] { syntaxTree },
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

        // 尋找含 public static Evaluate(Dictionary<string,double>) 的方法
        MethodInfo? evalMethod = null;
        foreach (var type in assembly.GetExportedTypes())
        {
            var method = type.GetMethod("Evaluate",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Dictionary<string, double>) },
                null);
            if (method != null && method.ReturnType == typeof(Dictionary<string, double>))
            {
                evalMethod = method;
                break;
            }
        }

        if (evalMethod == null)
        {
            _logger.LogWarning("C# 演算法 {Name} 找不到 public static Dictionary<string,double> Evaluate(Dictionary<string,double>) 方法",
                szFileName);
            return;
        }

        var del = (Func<Dictionary<string, double>, Dictionary<string, double>>)
            Delegate.CreateDelegate(
                typeof(Func<Dictionary<string, double>, Dictionary<string, double>>),
                evalMethod);

        _registry[szFileName] = new AlgorithmEntry { EvaluateDelegate = del };

        _logger.LogInformation("C# 演算法已載入: {Name}", szFileName);
    }

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
        public required Func<Dictionary<string, double>, Dictionary<string, double>> EvaluateDelegate { get; init; }
    }
}
