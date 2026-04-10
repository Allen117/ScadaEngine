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
"""

import importlib
import glob
import os
import re
import sys
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

app = FastAPI(title="SCADA Algorithm Service")

# ── 演算法註冊表 ──────────────────────────────────
_registry: dict[str, dict] = {}


class EvalRequest(BaseModel):
    inputs: dict[str, float]


def _parse_metadata(filepath: str) -> dict:
    """解析 .py 前 15 行的 @metadata comment"""
    meta = {"label": "", "inputs": ["in"], "outputs": ["out"], "description": ""}
    with open(filepath, encoding="utf-8") as f:
        for i, line in enumerate(f):
            if i >= 15:
                break
            line = line.strip()
            if line.startswith("# @algorithm:"):
                meta["label"] = line[len("# @algorithm:"):].strip()
            elif line.startswith("# @inputs:"):
                meta["inputs"] = [s.strip() for s in line[len("# @inputs:"):].split(",") if s.strip()]
            elif line.startswith("# @outputs:"):
                meta["outputs"] = [s.strip() for s in line[len("# @outputs:"):].split(",") if s.strip()]
            elif line.startswith("# @description:"):
                meta["description"] = line[len("# @description:"):].strip()
    return meta


def _discover_algorithms():
    """遞迴掃描同目錄及子資料夾下所有 *.py（排除 main.py / __*.py），動態 import 並註冊"""
    algo_dir = os.path.dirname(os.path.abspath(__file__))
    if algo_dir not in sys.path:
        sys.path.insert(0, algo_dir)

    for filepath in sorted(glob.glob(os.path.join(algo_dir, "**", "*.py"), recursive=True)):
        filename = os.path.basename(filepath)
        if filename == "main.py" or filename.startswith("__"):
            continue
        name = filename[:-3]  # 去掉 .py

        # 將子資料夾加入 sys.path 以便 importlib 找到模組
        sub_dir = os.path.dirname(filepath)
        if sub_dir != algo_dir and sub_dir not in sys.path:
            sys.path.insert(0, sub_dir)

        try:
            mod = importlib.import_module(name)
            if not hasattr(mod, "evaluate"):
                continue
            meta = _parse_metadata(filepath)
            _registry[name] = {
                "module": mod,
                "label": meta["label"] or name,
                "inputs": meta["inputs"],
                "outputs": meta["outputs"],
                "description": meta["description"],
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
        }
        for name, info in _registry.items()
    ]


@app.post("/algorithms/{name}/evaluate")
def evaluate(name: str, req: EvalRequest):
    if name not in _registry:
        raise HTTPException(status_code=404, detail=f"演算法 '{name}' 不存在")
    try:
        result = _registry[name]["module"].evaluate(req.inputs)
        return {"result": result, "quality": "Good"}
    except Exception as e:
        return {"result": {"out": 0}, "quality": "Bad", "error": str(e)}
