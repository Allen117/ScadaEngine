# -*- coding: utf-8 -*-
"""
SCADA Engine — Python 演算法服務
自動掃描同目錄下所有 *.py 演算法模組，包裝為 HTTP API。

啟動方式:
    pip install -r requirements.txt
    uvicorn main:app --host 127.0.0.1 --port 8100

API:
    GET  /                          → 健康檢查
    GET  /algorithms                → 列出所有已註冊的演算法
    POST /algorithms/{name}/evaluate → 執行演算法

演算法介面（唯一支援）：
    def evaluate_one(arg1, arg2, ...):
        return {"output_key": value, ...}                       # 視為 OK
        # 或
        return make_result({"output_key": value}, make_status(...))  # 含業務語意 status

    框架負責：從 inputs dict 取對應參數、變參迭代、try/except 對應 status code、結果 inf/nan 偵測、多組 status 聚合。
"""

import importlib
import glob
import inspect
import math
import os
import sys
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from _status import (
    AlgoStatus,
    Severity,
    EXCEPTION_STATUS_MAP,
    make_status,
    merge_status,
)

app = FastAPI(title="SCADA Algorithm Service")

# ── 演算法註冊表 ──────────────────────────────────
_registry: dict[str, dict] = {}


class EvalRequest(BaseModel):
    inputs: dict[str, float]
    n: int | None = None


def _parse_kv_list(raw: str) -> list[dict]:
    """解析 'key:label, key2:label2' 為 [{key, label}, ...]；無冒號時 label 等同 key"""
    items = []
    for seg in raw.split(","):
        seg = seg.strip()
        if not seg:
            continue
        if ":" in seg:
            k, lbl = seg.split(":", 1)
            items.append({"key": k.strip(), "label": lbl.strip() or k.strip()})
        else:
            items.append({"key": seg, "label": seg})
    return items


def _parse_metadata(filepath: str) -> dict:
    """解析 .py 前 15 行的 @metadata comment（含 variadic 標記）"""
    meta = {
        "label": "",
        "inputs": [{"key": "in", "label": "in"}],
        "outputs": [{"key": "out", "label": "out"}],
        "description": "",
        "variadic": False,
        "inputs_repeat": [],   # [{key, label}, ...]
        "inputs_fixed": [],    # [{key, label}, ...]
        "outputs_repeat": [],
        "outputs_fixed": [],
    }
    with open(filepath, encoding="utf-8") as f:
        for i, line in enumerate(f):
            if i >= 15:
                break
            line = line.strip()
            if line.startswith("# @algorithm:"):
                meta["label"] = line[len("# @algorithm:"):].strip()
            elif line.startswith("# @inputs:"):
                meta["inputs"] = _parse_kv_list(line[len("# @inputs:"):])
            elif line.startswith("# @outputs:"):
                meta["outputs"] = _parse_kv_list(line[len("# @outputs:"):])
            elif line.startswith("# @description:"):
                meta["description"] = line[len("# @description:"):].strip()
            elif line.startswith("# @variadic:"):
                v = line[len("# @variadic:"):].strip().lower()
                meta["variadic"] = v in ("true", "1", "yes")
            elif line.startswith("# @inputs_repeat:"):
                meta["inputs_repeat"] = _parse_kv_list(line[len("# @inputs_repeat:"):])
            elif line.startswith("# @inputs_fixed:"):
                meta["inputs_fixed"] = _parse_kv_list(line[len("# @inputs_fixed:"):])
            elif line.startswith("# @outputs_repeat:"):
                meta["outputs_repeat"] = _parse_kv_list(line[len("# @outputs_repeat:"):])
            elif line.startswith("# @outputs_fixed:"):
                meta["outputs_fixed"] = _parse_kv_list(line[len("# @outputs_fixed:"):])
    return meta


def _discover_algorithms():
    """遞迴掃描同目錄及子資料夾下所有 *.py（排除 main.py / _*.py），動態 import 並註冊"""
    algo_dir = os.path.dirname(os.path.abspath(__file__))
    if algo_dir not in sys.path:
        sys.path.insert(0, algo_dir)

    for filepath in sorted(glob.glob(os.path.join(algo_dir, "**", "*.py"), recursive=True)):
        filename = os.path.basename(filepath)
        # 排除 main.py 本身，以及任何 _ 開頭的共用模組 (如 _status.py)
        if filename == "main.py" or filename.startswith("_"):
            continue
        name = filename[:-3]  # 去掉 .py

        # 將子資料夾加入 sys.path 以便 importlib 找到模組
        sub_dir = os.path.dirname(filepath)
        if sub_dir != algo_dir and sub_dir not in sys.path:
            sys.path.insert(0, sub_dir)

        try:
            mod = importlib.import_module(name)
            if not hasattr(mod, "evaluate_one"):
                print(f"[WARN] {name} 缺 evaluate_one() 介面，已跳過")
                continue
            meta = _parse_metadata(filepath)
            _registry[name] = {
                "module": mod,
                "label": meta["label"] or name,
                "inputs": meta["inputs"],
                "outputs": meta["outputs"],
                "description": meta["description"],
                "variadic": meta["variadic"],
                "inputs_repeat": meta["inputs_repeat"],
                "inputs_fixed": meta["inputs_fixed"],
                "outputs_repeat": meta["outputs_repeat"],
                "outputs_fixed": meta["outputs_fixed"],
            }
        except Exception as e:
            print(f"[WARN] 載入演算法 {name} 失敗: {e}")


# 啟動時掃描
_discover_algorithms()


# ── API 路由 ──────────────────────────────────────

@app.get("/")
def health():
    return {"status": "ok", "algorithms": len(_registry)}


@app.get("/algorithms")
def list_algorithms():
    return [
        {
            "name": name,
            "label": info["label"],
            "inputs": info["inputs"],
            "outputs": info["outputs"],
            "description": info["description"],
            "variadic": info["variadic"],
            "inputsRepeat": info["inputs_repeat"],
            "inputsFixed": info["inputs_fixed"],
            "outputsRepeat": info["outputs_repeat"],
            "outputsFixed": info["outputs_fixed"],
        }
        for name, info in _registry.items()
    ]


# ── 框架核心：例外 → status / 結果 inf/nan / 單次呼叫 / 多組聚合 ──

def _exception_to_status(exc: BaseException) -> dict:
    """對應例外類型 → status dict；找不到對應 → INTERNAL_ERROR。"""
    code = EXCEPTION_STATUS_MAP.get(type(exc), AlgoStatus.INTERNAL_ERROR)
    return make_status(code)


def _scrub_nonfinite(d):
    """把 inf / nan 換成 0；回傳是否有任何非有限值（與 C# 行為一致，亦避免 JSON 序列化失敗）。"""
    if not isinstance(d, dict):
        return False
    has = False
    for k, v in d.items():
        if isinstance(v, (int, float)):
            try:
                if math.isinf(v) or math.isnan(v):
                    has = True
                    d[k] = 0
            except (TypeError, ValueError):
                pass
    return has


def _zero_fill(info: dict, suffix: str) -> dict:
    """失敗時的預設 result：以 meta 推導應有的輸出 key（含 suffix），值填 0。"""
    out = {}
    if info["outputs_repeat"] or info["outputs_fixed"]:
        for item in info["outputs_repeat"]:
            out[f'{item["key"]}{suffix}'] = 0
        for item in info["outputs_fixed"]:
            out[item["key"]] = 0
    else:
        for item in info["outputs"]:
            out[item["key"]] = 0
    return out


def _apply_output_suffix(inner: dict, repeat_output_keys: set, suffix: str) -> dict:
    """把 evaluate_one 回傳的 inner dict 套上 suffix（只有 outputs_repeat 中的 key 才套）。"""
    out = {}
    for k, v in inner.items():
        out[f"{k}{suffix}" if k in repeat_output_keys else k] = v
    return out


def _call_one(fn, sig: inspect.Signature, inputs: dict,
              repeat_input_keys: set, suffix: str):
    """
    執行一次 evaluate_one：從 inputs 取 signature 對應參數 → 呼叫 → 處理例外 / inf-nan。

    回傳 (inner_result_dict_or_None, status_dict)
    inner_result_dict 為演算法回的 dict（尚未套 output suffix）；失敗為 None。
    """
    kwargs = {}
    for pname in sig.parameters:
        key = f"{pname}{suffix}" if pname in repeat_input_keys else pname
        if key not in inputs:
            return None, make_status(AlgoStatus.INPUT_MISSING)
        kwargs[pname] = inputs[key]

    try:
        raw = fn(**kwargs)
    except Exception as e:
        return None, _exception_to_status(e)

    # 解析回傳值（支援 make_result 包裝或裸 dict）
    if isinstance(raw, dict) and "result" in raw and "status" in raw:
        inner = raw["result"]
        status = raw["status"]
    elif isinstance(raw, dict):
        inner = raw
        status = make_status(AlgoStatus.OK)
    else:
        return None, make_status(AlgoStatus.INTERNAL_ERROR)

    if _scrub_nonfinite(inner):
        status = make_status(AlgoStatus.NUMERIC_OVERFLOW)

    return inner, status


def _build_response(result, status, per_output=None):
    """組裝對外 JSON：頂層展平 statusCodeId / statusCodeName / severity，並由 severity 推導 quality。
    perOutput 為每個輸出 port (含 variadic suffix) 的 status map，供前端 per-port 反灰 / Engine per-port HasUpstreamBad 判斷。"""
    quality = "Bad" if status.get("severity") == Severity.ERROR else "Good"
    return {
        "result": result,
        "statusCodeId": status.get("statusCodeId", 0),
        "statusCodeName": status.get("statusCodeName", "OK"),
        "severity": status.get("severity", Severity.INFO),
        "quality": quality,
        "perOutput": per_output or {},
    }


def _output_keys_for_iter(info: dict, suffix: str) -> list[str]:
    """推導某次迭代會產生的所有 output key（含 suffix）；fixed 不套 suffix。"""
    if info["outputs_repeat"] or info["outputs_fixed"]:
        keys = [f'{item["key"]}{suffix}' for item in info["outputs_repeat"]]
        keys.extend(item["key"] for item in info["outputs_fixed"])
        return keys
    return [f'{item["key"]}{suffix}' if suffix else item["key"] for item in info["outputs"]]


@app.post("/algorithms/{name}/evaluate")
def evaluate(name: str, req: EvalRequest):
    if name not in _registry:
        raise HTTPException(status_code=404, detail=f"演算法 '{name}' 不存在")
    info = _registry[name]
    try:
        fn = info["module"].evaluate_one
        sig = inspect.signature(fn)

        if info["variadic"]:
            n = req.n if req.n is not None else 1
            repeat_input_keys = {item["key"] for item in info["inputs_repeat"]}
            repeat_output_keys = {item["key"] for item in info["outputs_repeat"]}
            merged_result: dict = {}
            merged_status = make_status(AlgoStatus.OK)
            per_output: dict = {}
            for i in range(1, n + 1):
                suffix = str(i)
                inner, status = _call_one(fn, sig, req.inputs, repeat_input_keys, suffix)
                if inner is None:
                    merged_result.update(_zero_fill(info, suffix))
                else:
                    merged_result.update(_apply_output_suffix(inner, repeat_output_keys, suffix))
                merged_status = merge_status(merged_status, status)
                # 此次迭代產生的所有 output key 共用同一 status
                for out_key in _output_keys_for_iter(info, suffix):
                    per_output[out_key] = status
            return _build_response(merged_result, merged_status, per_output)

        # 非變參：直接呼叫一次，所有輸出 key 共用同一 status
        inner, status = _call_one(fn, sig, req.inputs, repeat_input_keys=set(), suffix="")
        if inner is None:
            inner = _zero_fill(info, "")
        per_output = {k: status for k in _output_keys_for_iter(info, "")}
        return _build_response(inner, status, per_output)

    except Exception as e:
        # 兜底：dispatch 本身出包（不該到這裡，正常情況例外已在 _call_one 處理）
        err_status = make_status(AlgoStatus.INTERNAL_ERROR)
        resp = _build_response({"out": 0}, err_status, {"out": err_status})
        resp["error"] = str(e)
        return resp
