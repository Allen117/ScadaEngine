# -*- coding: utf-8 -*-
# @algorithm: PID控制
# @inputs: setpoint, current
# @outputs: out
# @description: PID 控制器輸出

from _status import AlgoStatus, make_status, make_result

# PID 狀態（跨呼叫保持）
_prev_error = 0.0
_integral = 0.0
_call_count = 0

# PID 參數（可依需求調整）
KP = 1.0
KI = 0.1
KD = 0.05
DT = 0.2  # 取樣週期（秒）

# 致動器輸出限幅
OUT_MIN = -100.0
OUT_MAX = 100.0

# 首次呼叫後多少 tick 內視為 WARMUP（讓積分項穩定）
WARMUP_TICKS = 5


def evaluate_one(setpoint, current):
    """
    PID 控制器。

    Status（業務語意，由演算法主動回傳）:
        WARMUP (Info)        — 啟動前 WARMUP_TICKS 個 tick
        SATURATED (Warning)  — 輸出觸頂 / 觸底
    """
    global _prev_error, _integral, _call_count

    error = setpoint - current
    _integral += error * DT
    derivative = (error - _prev_error) / DT if DT > 0 else 0
    _prev_error = error

    output = KP * error + KI * _integral + KD * derivative

    # 限幅 + 飽和偵測（先偵測再限幅，回原始輸出 4 位小數）
    is_saturated = output >= OUT_MAX or output <= OUT_MIN
    output = max(OUT_MIN, min(OUT_MAX, output))

    _call_count += 1
    if _call_count <= WARMUP_TICKS:
        status = make_status(AlgoStatus.WARMUP)
    elif is_saturated:
        status = make_status(AlgoStatus.SATURATED)
    else:
        status = make_status(AlgoStatus.OK)

    return make_result({"out": round(output, 4)}, status)
