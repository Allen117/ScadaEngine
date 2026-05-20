# -*- coding: utf-8 -*-
"""
inspect_cs.py — 解析 SCADA C# 演算法檔案，輸出 JSON 供 skill 使用。

用法:
    python inspect_cs.py --file path/to/algo.cs

C# 演算法格式高度受限（必有 `public static AlgorithmResult EvaluateOne(...)`、
return 必是 `AlgorithmResult.Ok(new() { ["key"] = ... })` 或同型），正則完全可勝任。

輸出 (stdout):
    {
      "ok": true,
      "language": "csharp",
      "function": "EvaluateOne",
      "inputs": ["cooling_capacity", "power"],
      "outputs": ["out"],
      "outputs_uncertain": false,
      "outputs_inconsistent": false,
      "all_output_sets": [["out"]],
      "existing": {...},
      "candidate_funcs": []
    }
"""

import argparse
import json
import re
import sys
from typing import Any

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


METADATA_KEYS = {
    "algorithm", "inputs", "outputs", "description", "variadic",
    "inputs_repeat", "inputs_fixed", "outputs_repeat", "outputs_fixed",
}

# `public static AlgorithmResult EvaluateOne(double a, double b, ...)`
RE_EVALUATE_ONE = re.compile(
    r"public\s+static\s+AlgorithmResult\s+EvaluateOne\s*\(([^)]*)\)",
    re.DOTALL,
)
# 任何 `public static AlgorithmResult Xxx(`
RE_ANY_ALGO_FUNC = re.compile(
    r"public\s+static\s+AlgorithmResult\s+(\w+)\s*\(",
)
# 字典初始化 key：`["key"] =`
RE_DICT_KEY = re.compile(r'\[\s*"([^"]+)"\s*\]\s*=')
# return 語句（粗略；C# 演算法都是 single-statement Body，不會誤抓）
RE_RETURN = re.compile(r"\breturn\b\s+([\s\S]+?);", re.MULTILINE)
# header line: `// @key: value`
RE_HEADER_LINE = re.compile(r"^\s*//\s*@(\w+)\s*:\s*(.*)$")


def parse_existing_header(source: str) -> dict[str, str]:
    existing: dict[str, str] = {}
    for line in source.splitlines()[:15]:
        m = RE_HEADER_LINE.match(line)
        if not m:
            continue
        key = m.group(1).strip()
        if key in METADATA_KEYS:
            existing[key] = m.group(2).strip()
    return existing


def extract_param_names(params_block: str) -> list[str]:
    """`double a, double b = 0.0` → ['a', 'b']"""
    names: list[str] = []
    if not params_block.strip():
        return names
    for raw in params_block.split(","):
        seg = raw.strip()
        if not seg:
            continue
        # 去預設值
        if "=" in seg:
            seg = seg.split("=", 1)[0].strip()
        # `double name` 或 `in double name` 等 → 取最後一個 token
        tokens = seg.split()
        if tokens:
            names.append(tokens[-1].lstrip("@"))
    return names


def inspect(filepath: str) -> dict[str, Any]:
    with open(filepath, "r", encoding="utf-8") as f:
        source = f.read()
    existing = parse_existing_header(source)

    m = RE_EVALUATE_ONE.search(source)
    if not m:
        candidates = list(dict.fromkeys(RE_ANY_ALGO_FUNC.findall(source)))
        return {
            "ok": False,
            "reason": "evaluate_one_missing",
            "candidate_funcs": candidates,
            "existing": existing,
        }

    inputs = extract_param_names(m.group(1))

    # 找出函式主體範圍：從 EvaluateOne 簽名結束往後，直到平衡的 `}` 或檔案結尾
    body_start = m.end()
    body = extract_method_body(source, body_start)

    # 取所有 return 語句，每個 return 內部抽 dict keys
    all_sets: list[list[str]] = []
    uncertain = False
    for rm in RE_RETURN.finditer(body):
        expr = rm.group(1)
        keys = RE_DICT_KEY.findall(expr)
        if keys:
            # 去重保留順序
            uniq: list[str] = []
            seen: set[str] = set()
            for k in keys:
                if k not in seen:
                    seen.add(k)
                    uniq.append(k)
            all_sets.append(uniq)
        else:
            # return 語句但找不到 ["key"]= 字典字面 → uncertain
            uncertain = True

    # 同時也支援 expression-bodied `=> AlgorithmResult.Ok(new() { ["out"] = ... });`
    if not all_sets and not uncertain:
        # 從整個 body 直接抓
        keys = RE_DICT_KEY.findall(body)
        if keys:
            uniq2: list[str] = []
            seen2: set[str] = set()
            for k in keys:
                if k not in seen2:
                    seen2.add(k)
                    uniq2.append(k)
            all_sets.append(uniq2)

    # 一致性判斷
    inconsistent = False
    merged: list[str] = []
    if all_sets:
        first = all_sets[0]
        for s in all_sets[1:]:
            if s != first:
                inconsistent = True
                break
        seen3: set[str] = set()
        for s in all_sets:
            for k in s:
                if k not in seen3:
                    seen3.add(k)
                    merged.append(k)
    elif not uncertain:
        # 完全找不到 return / dict → uncertain
        uncertain = True

    return {
        "ok": True,
        "language": "csharp",
        "function": "EvaluateOne",
        "inputs": inputs,
        "outputs": merged,
        "outputs_uncertain": uncertain,
        "outputs_inconsistent": inconsistent,
        "all_output_sets": all_sets,
        "existing": existing,
        "candidate_funcs": [],
    }


def extract_method_body(source: str, start: int) -> str:
    """從 EvaluateOne(...) 後面開始，找出 method body（含 expression-bodied =>）。"""
    i = start
    n = len(source)
    # 跳空白
    while i < n and source[i] in " \t\r\n":
        i += 1
    if i >= n:
        return ""
    # expression-bodied: `=> ... ;`
    if source[i:i+2] == "=>":
        j = source.find(";", i)
        if j < 0:
            return source[i:]
        return source[i:j+1]
    # block body: `{ ... }` (平衡 brace)
    if source[i] == "{":
        depth = 0
        for j in range(i, n):
            if source[j] == "{":
                depth += 1
            elif source[j] == "}":
                depth -= 1
                if depth == 0:
                    return source[i:j+1]
        return source[i:]
    return source[i:]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", required=True)
    args = parser.parse_args()
    result = inspect(args.file)
    json.dump(result, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
