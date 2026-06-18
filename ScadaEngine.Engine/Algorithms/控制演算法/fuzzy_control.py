# -*- coding: utf-8 -*-
# @algorithm: 模糊控制
# @inputs: setpoint, current
# @outputs: out
# @description: Mamdani 模糊控制器輸出（error + d_error 雙輸入，9 條規則，單例重心法）

from _status import AlgoStatus, make_status, make_result

# 狀態（跨呼叫保持）
_prev_error = 0.0
_call_count = 0
_initialized = False

# 取樣週期（秒）
DT = 0.2

# 輸入歸一化縮放：實際 error / d_error 除以此值後落在 [-1, 1]
ERROR_SCALE = 10.0      # 例：溫度誤差 ±10 度即視為飽和
DERR_SCALE = 5.0        # 例：誤差變化率 ±5/秒即視為飽和

# 輸出反歸一化縮放：模糊輸出 [-1, 1] 乘以此值
OUT_SCALE = 100.0

# 限幅
OUT_MIN = -100.0
OUT_MAX = 100.0

# 首次呼叫後多少 tick 內視為 WARMUP（d_error 尚未穩定）
WARMUP_TICKS = 2


def _tri(x, a, b, c):
    """三角隸屬函式：左頂 a、峰 b、右頂 c，回傳 0~1。"""
    if x <= a or x >= c:
        return 0.0
    if x == b:
        return 1.0
    if x < b:
        return (x - a) / (b - a)
    return (c - x) / (c - b)


def _fuzzify(x):
    """將歸一化輸入分解為 (N, Z, P) 三個隸屬度。"""
    return (
        _tri(x, -2.0, -1.0, 0.0),   # Negative
        _tri(x, -1.0,  0.0, 1.0),   # Zero
        _tri(x,  0.0,  1.0, 2.0),   # Positive
    )


# 規則表：rule[i_err][j_derr] = 輸出單例（NB=-1, NS=-0.5, Z=0, PS=0.5, PB=1）
# 列 = error (N, Z, P)；行 = d_error (N, Z, P)
_RULES = (
    (-1.0, -1.0, -0.5),   # error=N
    (-0.5,  0.0,  0.5),   # error=Z
    ( 0.5,  1.0,  1.0),   # error=P
)


def evaluate_one(setpoint, current):
    """
    Mamdani 模糊控制器。

    Status（業務語意）:
        WARMUP (Info)        — 啟動前 WARMUP_TICKS 個 tick（d_error 尚未穩定）
        SATURATED (Warning)  — 輸出觸頂 / 觸底
    """
    global _prev_error, _call_count, _initialized

    error = setpoint - current
    if _initialized:
        d_error = (error - _prev_error) / DT if DT > 0 else 0.0
    else:
        d_error = 0.0
        _initialized = True
    _prev_error = error

    # 歸一化到 [-1, 1]（超出範圍由三角函式自然飽和）
    e_n = max(-1.5, min(1.5, error / ERROR_SCALE)) if ERROR_SCALE > 0 else 0.0
    de_n = max(-1.5, min(1.5, d_error / DERR_SCALE)) if DERR_SCALE > 0 else 0.0

    mu_e = _fuzzify(e_n)
    mu_de = _fuzzify(de_n)

    # 推論 + 單例重心法
    num = 0.0
    den = 0.0
    for i in range(3):
        for j in range(3):
            w = min(mu_e[i], mu_de[j])   # AND = min
            if w > 0.0:
                num += w * _RULES[i][j]
                den += w
    u = (num / den) if den > 0.0 else 0.0

    output = u * OUT_SCALE

    # 限幅 + 飽和偵測
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
