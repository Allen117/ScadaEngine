# -*- coding: utf-8 -*-
"""
SCADA Engine 演算法狀態碼共用模組。

對照表單一來源：docs/功能說明書_演算法服務.md §3
雙語言同步：C# 端為 ScadaEngine.Engine/Algorithms/_AlgorithmStatus.cs
"""


class Severity:
    INFO = "Info"
    WARNING = "Warning"
    ERROR = "Error"


# Severity 嚴重度排序（merge_status 用）
SEVERITY_RANK = {
    Severity.INFO: 1,
    Severity.WARNING: 2,
    Severity.ERROR: 3,
}


class AlgoStatus:
    """演算法狀態碼定義（codeId + codeName + 預設 severity）。"""
    OK                  = (0,  "OK",                   Severity.INFO)
    DIVIDE_BY_ZERO      = (10, "DIVIDE_BY_ZERO",       Severity.ERROR)
    INPUT_MISSING       = (11, "INPUT_MISSING",        Severity.ERROR)
    INPUT_OUT_OF_RANGE  = (12, "INPUT_OUT_OF_RANGE",   Severity.WARNING)
    SATURATED           = (20, "SATURATED",            Severity.WARNING)
    WARMUP              = (21, "WARMUP",               Severity.INFO)
    NUMERIC_OVERFLOW    = (30, "NUMERIC_OVERFLOW",     Severity.ERROR)
    DB_ACCESS_FAILED    = (40, "DB_ACCESS_FAILED",     Severity.ERROR)
    API_CALL_FAILED     = (41, "API_CALL_FAILED",      Severity.ERROR)
    INTERNAL_ERROR      = (90, "INTERNAL_ERROR",       Severity.ERROR)


# 例外類型 → AlgoStatus 對照表（main.py 框架層自動套用）
EXCEPTION_STATUS_MAP = {
    ZeroDivisionError: AlgoStatus.DIVIDE_BY_ZERO,
    KeyError:          AlgoStatus.INPUT_MISSING,
    TypeError:         AlgoStatus.INPUT_MISSING,
    ValueError:        AlgoStatus.INPUT_MISSING,
    OverflowError:     AlgoStatus.NUMERIC_OVERFLOW,
}


def make_status(code, severity=None):
    """
    回傳 status dict。code 為 AlgoStatus 的 tuple，severity 可選 (預設使用 code 內建)。

    例:
        make_status(AlgoStatus.DIVIDE_BY_ZERO)
        make_status(AlgoStatus.SATURATED, Severity.WARNING)
    """
    code_id, code_name, default_severity = code
    return {
        "statusCodeId": code_id,
        "statusCodeName": code_name,
        "severity": severity or default_severity,
    }


def make_result(result_dict, status=None):
    """
    包裝演算法回傳值。`result_dict` 為輸出 dict，`status` 為 make_status() 結果。

    若 status 為 None，視為 OK。
    """
    if status is None:
        status = make_status(AlgoStatus.OK)
    return {
        "result": result_dict,
        "status": status,
    }


def merge_status(a, b):
    """比較兩個 status dict，回傳 severity 較高者（同級先到先用）。"""
    rank_a = SEVERITY_RANK.get(a.get("severity", Severity.INFO), 0)
    rank_b = SEVERITY_RANK.get(b.get("severity", Severity.INFO), 0)
    return b if rank_b > rank_a else a
