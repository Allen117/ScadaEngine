# -*- coding: utf-8 -*-
# @algorithm: 溫差Fuzzy水泵頻率
# @variadic: true
# @inputs_fixed: t_out:出水溫, t_in:入水溫, dt_target:溫差目標, e_scale:誤差範圍, de_scale:變化範圍, step_max:單步最大Hz, deadband:不動作帶
# @inputs_repeat: run:運轉狀態, freq_fb:目前頻率
# @inputs_auto_repeat: freq_min:freq:Min, freq_max:freq:Max
# @inputs_default: e_scale=3, de_scale=1, step_max=2, deadband=0.3
# @outputs_repeat: freq:頻率輸出
# @description: 出回水溫差對目標做 5x5 Fuzzy 增量控制，N 台泵共用溫度與目標、各自輸出頻率；Min/Max 由框架依下游點位自動注入

import time

from _status import AlgoStatus, make_status, make_result

# ── 演算法設計參數（非使用者整定；使用者整定走 tuning 輸入 port）─────────
WARMUP_ROUNDS = 2       # 啟動前 N 輪 de 未穩定 → status WARMUP、頻率維持原值
ROUND_WINDOW_SEC = 0.5  # 同一輪判定窗：變參模式一輪 HTTP request 內連呼 N 次（毫秒級），
                        # 距上次呼叫 < 此秒數視為同輪共用 (e, de)；Engine 輪詢間隔秒級，兩邊皆有量級裕度
E_SCALE_FALLBACK = 3.0      # e_scale <= 0 時的防呆回退（°C）
DE_SCALE_FALLBACK = 1.0     # de_scale <= 0 時的防呆回退
STEP_MAX_FALLBACK = 2.0     # step_max <= 0 時的防呆回退（Hz）

# 5×5 規則矩陣（反對角經典型）：列 = e 隸屬 (NB..PB)、欄 = de 隸屬 (NB..PB)，
# 輸出單例 ∈ [-1, 1]。e 為正（溫差過大 = 流量不足）→ Δf 為正（加速）。
# 矩陣形狀屬工程師調表領域，改此表重啟服務即生效（決策 4）。
_RULES = (
    #  de:  NB     NS     ZO     PS     PB
    (-1.0, -1.0, -0.75, -0.5,   0.0),   # e = NB（溫差遠小於目標 → 大減速）
    (-1.0, -0.75, -0.5,   0.0,  0.5),   # e = NS
    (-0.75, -0.5,  0.0,   0.5,  0.75),  # e = ZO
    (-0.5,   0.0,  0.5,   0.75, 1.0),   # e = PS
    ( 0.0,   0.5,  0.75,  1.0,  1.0),   # e = PB（溫差遠大於目標 → 大加速）
)

# ── 跨呼叫狀態（時間視窗輪次快取，決策 3）─────────────────────────────
_last_call_mono = None   # 上次呼叫的 time.monotonic()
_prev_round_e = None     # 上一輪的 e（算 de 用）
_round_e = 0.0           # 本輪共用的 e
_round_de = 0.0          # 本輪共用的 de
_round_count = 0         # 輪次計數（WARMUP 判斷）


def _tri(x, a, b, c):
    if x <= a or x >= c:
        return 0.0
    if x == b:
        return 1.0
    if x < b:
        return (x - a) / (b - a)
    return (c - x) / (c - b)


def _fuzzify5(x):
    """將歸一化輸入分解為 (NB, NS, ZO, PS, PB) 五個三角隸屬度（頂點 -1, -0.5, 0, 0.5, 1）。"""
    return (
        1.0 if x <= -1.0 else _tri(x, -1.5, -1.0, -0.5),  # NB：左肩型
        _tri(x, -1.0, -0.5, 0.0),
        _tri(x, -0.5,  0.0, 0.5),
        _tri(x,  0.0,  0.5, 1.0),
        1.0 if x >= 1.0 else _tri(x, 0.5, 1.0, 1.5),      # PB：右肩型
    )


def _fuzzy_delta(e_n, de_n):
    """5×5 規則 + 單例重心法：回傳歸一化增量 u ∈ [-1, 1]。"""
    mu_e = _fuzzify5(e_n)
    mu_de = _fuzzify5(de_n)
    num = 0.0
    den = 0.0
    for i in range(5):
        if mu_e[i] <= 0.0:
            continue
        for j in range(5):
            w = min(mu_e[i], mu_de[j])
            if w > 0.0:
                num += w * _RULES[i][j]
                den += w
    return (num / den) if den > 0.0 else 0.0


def _update_round(e):
    """時間視窗輪次判定：距上次呼叫 >= ROUND_WINDOW_SEC 視為新一輪，更新共用 (e, de)。
    同輪第 2..N 次呼叫（毫秒內）直接沿用，避免各泵 de 不一致（決策 3）。"""
    global _last_call_mono, _prev_round_e, _round_e, _round_de, _round_count
    now = time.monotonic()
    if _last_call_mono is None or now - _last_call_mono >= ROUND_WINDOW_SEC:
        _round_de = (e - _prev_round_e) if _prev_round_e is not None else 0.0
        _prev_round_e = e
        _round_e = e
        _round_count += 1
    _last_call_mono = now


def evaluate_one(t_out, t_in, dt_target, e_scale, de_scale, step_max, deadband,
                 run, freq_fb, freq_min, freq_max):
    """
    溫差 Fuzzy 水泵頻率（增量式 velocity form）：
    e = (入水溫 − 出水溫) − 溫差目標、de = e − e_prev（跨輪），
    5×5 矩陣查增量 Δf = u × step_max，輸出 freq = clamp(freq_fb + Δf, freq_min, freq_max)。

    freq_min / freq_max 由框架依「freq 輸出 port 下游點位的 Min/Max」自動注入（@inputs_auto_repeat）。

    Status:
        WARMUP (Info)        — 啟動前 WARMUP_ROUNDS 輪（de 未穩定），頻率維持原值
        SATURATED (Warning)  — 輸出觸頂 / 觸底
        OK                   — 含 deadband 內不動作與停機泵維持原值（決策 5）
    """
    # 防呆：tuning 非法值回退到模組內預設
    e_scale_eff = e_scale if e_scale and e_scale > 0 else E_SCALE_FALLBACK
    de_scale_eff = de_scale if de_scale and de_scale > 0 else DE_SCALE_FALLBACK
    step_max_eff = step_max if step_max and step_max > 0 else STEP_MAX_FALLBACK
    deadband_eff = deadband if deadband and deadband > 0 else 0.0
    if freq_max < freq_min:
        freq_min, freq_max = freq_max, freq_min

    e = (t_in - t_out) - dt_target
    _update_round(e)

    # 停機泵：輸出維持原值（不推頻率、開機瞬間無擾動接手），status OK（決策 5）
    if run == 0:
        return make_result({"freq": round(freq_fb, 2)})

    # WARMUP：de 未穩定前不動作，頻率維持原值
    if _round_count <= WARMUP_ROUNDS:
        return make_result({"freq": round(freq_fb, 2)}, make_status(AlgoStatus.WARMUP))

    # deadband：|e| 低於不動作帶 → Δf = 0（仍夾限幅，避免原值本身超界）
    if abs(_round_e) <= deadband_eff:
        delta = 0.0
    else:
        e_n = max(-1.5, min(1.5, _round_e / e_scale_eff))
        de_n = max(-1.5, min(1.5, _round_de / de_scale_eff))
        delta = _fuzzy_delta(e_n, de_n) * step_max_eff

    freq = freq_fb + delta
    is_saturated = freq >= freq_max or freq <= freq_min
    freq = max(freq_min, min(freq_max, freq))

    status = make_status(AlgoStatus.SATURATED) if is_saturated else None
    return make_result({"freq": round(freq, 2)}, status)
