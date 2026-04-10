# -*- coding: utf-8 -*-
# @algorithm: PID控制
# @inputs: setpoint, current
# @outputs: out
# @description: PID 控制器輸出

# PID 狀態（跨呼叫保持）
_prev_error = 0.0
_integral = 0.0

# PID 參數（可依需求調整）
KP = 1.0
KI = 0.1
KD = 0.05
DT = 0.2  # 取樣週期（秒）


def evaluate(inputs: dict) -> dict:
    """
    PID 控制器
    inputs:
        setpoint: 目標設定值
        current: 目前量測值
    outputs:
        out: 控制輸出量
    """
    global _prev_error, _integral

    sp = inputs.get("setpoint", 0)
    pv = inputs.get("current", 0)

    error = sp - pv
    _integral += error * DT
    derivative = (error - _prev_error) / DT if DT > 0 else 0
    _prev_error = error

    output = KP * error + KI * _integral + KD * derivative
    return {"out": round(output, 4)}
