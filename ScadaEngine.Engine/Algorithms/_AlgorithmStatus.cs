// SCADA Engine 演算法狀態碼共用源碼。
// 由 CSharpAlgorithmService 在編譯每個演算法 .cs 時自動帶入，
// 演算法只要 using ScadaEngine.Algorithms; 即可使用。
//
// 對照表單一來源：docs/功能說明書_演算法服務.md §3
// 雙語言同步：Python 端為 ScadaEngine.Engine/Algorithms/_status.py

using System;
using System.Collections.Generic;

namespace ScadaEngine.Algorithms;

public enum AlgorithmSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public enum AlgorithmStatusCode
{
    Ok                = 0,
    DivideByZero      = 10,
    InputMissing      = 11,
    InputOutOfRange   = 12,
    Saturated         = 20,
    Warmup            = 21,
    NumericOverflow   = 30,
    DbAccessFailed    = 40,
    ApiCallFailed     = 41,
    InternalError     = 90,
}

public record AlgorithmStatus(int CodeId, string CodeName, AlgorithmSeverity Severity)
{
    public static AlgorithmStatus Ok => From(AlgorithmStatusCode.Ok);

    public static AlgorithmStatus From(AlgorithmStatusCode code, AlgorithmSeverity? overrideSeverity = null)
    {
        var (name, defaultSeverity) = Lookup(code);
        return new AlgorithmStatus((int)code, name, overrideSeverity ?? defaultSeverity);
    }

    /// <summary>取嚴重度較高者；同級回傳 a。</summary>
    public static AlgorithmStatus Merge(AlgorithmStatus a, AlgorithmStatus b)
        => (int)b.Severity > (int)a.Severity ? b : a;

    /// <summary>例外類型 → AlgorithmStatus（框架層自動套用，與 Python EXCEPTION_STATUS_MAP 同步）。</summary>
    public static AlgorithmStatus FromException(Exception ex) => ex switch
    {
        DivideByZeroException   => From(AlgorithmStatusCode.DivideByZero),
        KeyNotFoundException    => From(AlgorithmStatusCode.InputMissing),
        ArgumentException       => From(AlgorithmStatusCode.InputMissing),
        InvalidCastException    => From(AlgorithmStatusCode.InputMissing),
        OverflowException       => From(AlgorithmStatusCode.NumericOverflow),
        _                       => From(AlgorithmStatusCode.InternalError),
    };

    private static (string name, AlgorithmSeverity sev) Lookup(AlgorithmStatusCode code) => code switch
    {
        AlgorithmStatusCode.Ok              => ("OK",                  AlgorithmSeverity.Info),
        AlgorithmStatusCode.DivideByZero    => ("DIVIDE_BY_ZERO",      AlgorithmSeverity.Error),
        AlgorithmStatusCode.InputMissing    => ("INPUT_MISSING",       AlgorithmSeverity.Error),
        AlgorithmStatusCode.InputOutOfRange => ("INPUT_OUT_OF_RANGE",  AlgorithmSeverity.Warning),
        AlgorithmStatusCode.Saturated       => ("SATURATED",           AlgorithmSeverity.Warning),
        AlgorithmStatusCode.Warmup          => ("WARMUP",              AlgorithmSeverity.Info),
        AlgorithmStatusCode.NumericOverflow => ("NUMERIC_OVERFLOW",    AlgorithmSeverity.Error),
        AlgorithmStatusCode.DbAccessFailed  => ("DB_ACCESS_FAILED",    AlgorithmSeverity.Error),
        AlgorithmStatusCode.ApiCallFailed   => ("API_CALL_FAILED",     AlgorithmSeverity.Error),
        AlgorithmStatusCode.InternalError   => ("INTERNAL_ERROR",      AlgorithmSeverity.Error),
        _                                   => ("UNKNOWN",             AlgorithmSeverity.Error),
    };
}

public record AlgorithmResult(Dictionary<string, double> Result, AlgorithmStatus Status)
{
    public static AlgorithmResult Ok(Dictionary<string, double> result) =>
        new(result, AlgorithmStatus.Ok);

    public static AlgorithmResult From(Dictionary<string, double> result, AlgorithmStatusCode code,
                                       AlgorithmSeverity? overrideSeverity = null) =>
        new(result, AlgorithmStatus.From(code, overrideSeverity));
}
