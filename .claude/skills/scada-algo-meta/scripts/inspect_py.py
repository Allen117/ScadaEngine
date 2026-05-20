# -*- coding: utf-8 -*-
"""
inspect_py.py — 解析 SCADA Python 演算法檔案，輸出 JSON 供 skill 使用。

用法:
    python inspect_py.py --file path/to/algo.py

輸出 (stdout):
    {
      "ok": true,
      "language": "python",
      "function": "evaluate_one",
      "inputs": ["cooling_capacity", "power"],
      "outputs": ["cop"],
      "outputs_uncertain": false,
      "docstring": "...",
      "existing": { "algorithm": "...", "variadic": "true", ... },
      "candidate_funcs": []
    }

當找不到 evaluate_one 時:
    {
      "ok": false,
      "reason": "evaluate_one_missing",
      "candidate_funcs": ["calc_cop", "do_pid"],
      "existing": {...}
    }
"""

import argparse
import ast
import json
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


def parse_existing_header(source_lines: list[str]) -> dict[str, str]:
    existing: dict[str, str] = {}
    for i, line in enumerate(source_lines[:15]):
        s = line.strip()
        if not s.startswith("# @"):
            continue
        rest = s[len("# @"):]
        if ":" not in rest:
            continue
        key, value = rest.split(":", 1)
        key = key.strip()
        if key in METADATA_KEYS:
            existing[key] = value.strip()
    return existing


def collect_dict_keys(node: ast.AST) -> list[str] | None:
    """從 Dict 字面量抽 key list；非字面量回 None。"""
    if not isinstance(node, ast.Dict):
        return None
    keys: list[str] = []
    for k in node.keys:
        if isinstance(k, ast.Constant) and isinstance(k.value, str):
            keys.append(k.value)
        else:
            return None
    return keys


def extract_outputs_from_return(ret: ast.Return) -> tuple[list[str] | None, bool]:
    """從 return 抽 output key list；無法解析回 (None, True=uncertain)。"""
    val = ret.value
    if val is None:
        return None, True
    keys = collect_dict_keys(val)
    if keys is not None:
        return keys, False
    if isinstance(val, ast.Call):
        func = val.func
        is_make_result = (
            (isinstance(func, ast.Name) and func.id == "make_result")
            or (isinstance(func, ast.Attribute) and func.attr == "make_result")
        )
        if is_make_result and val.args:
            keys = collect_dict_keys(val.args[0])
            if keys is not None:
                return keys, False
    return None, True


def inspect(filepath: str) -> dict[str, Any]:
    with open(filepath, "r", encoding="utf-8") as f:
        source = f.read()
    source_lines = source.splitlines()
    existing = parse_existing_header(source_lines)

    try:
        tree = ast.parse(source, filename=filepath)
    except SyntaxError as e:
        return {"ok": False, "reason": "syntax_error", "error": str(e), "existing": existing}

    target_func: ast.FunctionDef | None = None
    top_level_funcs: list[str] = []
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            top_level_funcs.append(node.name)
            if node.name == "evaluate_one":
                target_func = node

    if target_func is None:
        candidates = [n for n in top_level_funcs if not n.startswith("_")]
        return {
            "ok": False,
            "reason": "evaluate_one_missing",
            "candidate_funcs": candidates,
            "existing": existing,
        }

    inputs = [a.arg for a in target_func.args.args]
    docstring = (ast.get_docstring(target_func) or "").strip().splitlines()
    docstring_first_line = docstring[0].strip() if docstring else ""

    outputs_set: list[str] = []
    seen: set[str] = set()
    uncertain = False
    return_count = 0
    for sub in ast.walk(target_func):
        if isinstance(sub, ast.Return):
            return_count += 1
            keys, is_uncertain = extract_outputs_from_return(sub)
            if is_uncertain:
                uncertain = True
                continue
            for k in keys or []:
                if k not in seen:
                    seen.add(k)
                    outputs_set.append(k)
    if return_count == 0:
        uncertain = True

    return {
        "ok": True,
        "language": "python",
        "function": "evaluate_one",
        "inputs": inputs,
        "outputs": outputs_set,
        "outputs_uncertain": uncertain,
        "docstring": docstring_first_line,
        "existing": existing,
        "candidate_funcs": [],
    }


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
