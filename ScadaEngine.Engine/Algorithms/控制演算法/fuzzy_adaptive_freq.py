# -*- coding: utf-8 -*-
# @algorithm: 自適應模糊控制(溫濕度)
# @inputs: t_set:溫度設定, t_cur:溫度量測, h_set:濕度設定, h_cur:濕度量測, ku_base:輸出增益基準, out_min:輸出下限, out_max:輸出上限
# @outputs: out:頻率輸出
# @description: 溫度+濕度雙目標主導誤差選擇法 + 3x3 Mamdani + Self-Tuning Ku（範圍夾在 ku_base*0.8~1.2）

from _status import AlgoStatus, make_status, make_result

# ── 演算法設計參數（非使用者整定）─────────────────
DT = 0.2                # 取樣週期（秒）
T_ERROR_SCALE = 5.0     # 溫度誤差 ±5°C 即視為飽和
H_ERROR_SCALE = 15.0    # 濕度誤差 ±15%RH 即視為飽和
DERR_SCALE = 5.0        # 主導誤差變化率歸一化分母（單位/秒）
HYSTERESIS = 0.10       # 主導切換滯後：新候選需大過現任 10% 才換手
WARMUP_TICKS = 3        # WARMUP 視窗（讓 d_error / 滑動視窗穩定）

# Self-Tuning 視窗與步長
TUNE_WINDOW = 30        # 滑動視窗 tick 數（DT=0.2s → 6 秒）
TUNE_ALPHA = 0.05       # Ku 朝 target 收斂的步長（每 tick）
KU_FALLBACK = 50.0      # ku_base <= 0 時的防呆回退值

# Self-Tuning 規則的隸屬度斷點
AVG_E_LOW_END = 0.5     # 平均 |e_n|：(0~0.5) 走 Low
AVG_E_HIGH_BEGIN = 0.2  # (0.2~0.7) 走 High（與 Low 重疊形成模糊）
AVG_E_HIGH_END = 0.7
FLIP_LOW_END = 0.2      # 翻轉比例：(0~0.2) 走 Low
FLIP_HIGH_BEGIN = 0.1   # (0.1~0.4) 走 High
FLIP_HIGH_END = 0.4


# ── 跨呼叫狀態 ───────────────────────────────────
_initialized = False
_call_count = 0
_prev_e_t = 0.0
_prev_e_h = 0.0
_dominant = "t"             # "t" 或 "h"
_e_dom_window: list[float] = []  # 滑動視窗：最近 TUNE_WINDOW 個 e_dom_n
_ku = None                  # 當前 Ku（首次依 ku_base 初始化）
_last_ku_base = None        # 偵測使用者改 ku_base 即重置 _ku


def _tri(x, a, b, c):
    if x <= a or x >= c:
        return 0.0
    if x == b:
        return 1.0
    if x < b:
        return (x - a) / (b - a)
    return (c - x) / (c - b)


def _fuzzify_pn(x):
    """主 Fuzzy：將歸一化輸入分解為 (N, Z, P) 三個隸屬度。"""
    return (
        _tri(x, -2.0, -1.0, 0.0),
        _tri(x, -1.0,  0.0, 1.0),
        _tri(x,  0.0,  1.0, 2.0),
    )


# 主 Fuzzy 規則表：rule[i_err][j_derr]，輸出單例 ∈ [-1, 1]
_RULES = (
    (-1.0, -1.0, -0.5),
    (-0.5,  0.0,  0.5),
    ( 0.5,  1.0,  1.0),
)


def _ramp_down(x, lo, hi):
    """x<=lo → 1，x>=hi → 0，中間線性。"""
    if x <= lo:
        return 1.0
    if x >= hi:
        return 0.0
    return (hi - x) / (hi - lo)


def _ramp_up(x, lo, hi):
    """x<=lo → 0，x>=hi → 1，中間線性。"""
    if x <= lo:
        return 0.0
    if x >= hi:
        return 1.0
    return (x - lo) / (hi - lo)


def _self_tune_target(avg_e, flip_ratio, ku_base):
    """
    Self-Tuning：依（平均 |e_n|, 符號翻轉比例）以 4 條規則加權平均出 target_Ku。
    Singleton 範圍夾在 [ku_base*0.8, ku_base*1.2]，極端情況落在 ku_base*0.8。
    """
    e_low = _ramp_down(avg_e, 0.0, AVG_E_LOW_END)
    e_high = _ramp_up(avg_e, AVG_E_HIGH_BEGIN, AVG_E_HIGH_END)
    f_low = _ramp_down(flip_ratio, 0.0, FLIP_LOW_END)
    f_high = _ramp_up(flip_ratio, FLIP_HIGH_BEGIN, FLIP_HIGH_END)

    ku_hi = ku_base * 1.2
    ku_lo = ku_base * 0.8

    rules = (
        (min(e_high, f_low),  ku_hi),    # 誤差大、無振盪 → Ku ↑
        (min(e_high, f_high), ku_lo),    # 誤差大、有振盪 → Ku ↓
        (min(e_low,  f_low),  ku_base),  # 誤差小、無振盪 → Ku 持平
        (min(e_low,  f_high), ku_lo),    # 誤差小、有振盪 → Ku ↓↓（夾持後等同 ↓）
    )
    num = sum(w * s for w, s in rules)
    den = sum(w for w, _ in rules)
    return (num / den) if den > 0.0 else ku_base


def _count_sign_flips(window):
    """計算視窗內相鄰元素「符號從非負轉負或反之」的次數（0 視為與正號同方向）。"""
    flips = 0
    for i in range(1, len(window)):
        a, b = window[i - 1], window[i]
        if (a >= 0) != (b >= 0):
            flips += 1
    return flips


def evaluate_one(t_set, t_cur, h_set, h_cur, ku_base, out_min, out_max):
    """
    多目標自適應模糊控制器：溫度+濕度主導誤差 → 3x3 Mamdani → Self-Tuning Ku → 限幅。

    Status:
        WARMUP (Info)        — 啟動前 WARMUP_TICKS 個 tick
        SATURATED (Warning)  — 輸出觸頂 / 觸底
    """
    global _initialized, _call_count
    global _prev_e_t, _prev_e_h, _dominant, _e_dom_window, _ku, _last_ku_base

    # 防呆：ku_base 非法時回退到模組內預設
    ku_base_eff = ku_base if ku_base and ku_base > 0 else KU_FALLBACK

    # 使用者改 ku_base 即重置 _ku（避免卡在舊範圍）
    if _ku is None or _last_ku_base != ku_base_eff:
        _ku = ku_base_eff
        _last_ku_base = ku_base_eff

    # 1. 兩通道誤差 & 歸一化
    e_t = t_set - t_cur
    e_h = h_set - h_cur
    e_t_n = max(-1.5, min(1.5, e_t / T_ERROR_SCALE)) if T_ERROR_SCALE > 0 else 0.0
    e_h_n = max(-1.5, min(1.5, e_h / H_ERROR_SCALE)) if H_ERROR_SCALE > 0 else 0.0

    # 2. Selector：主導誤差選擇（含滯後）
    if _dominant == "t":
        if abs(e_h_n) > abs(e_t_n) * (1.0 + HYSTERESIS):
            _dominant = "h"
    else:
        if abs(e_t_n) > abs(e_h_n) * (1.0 + HYSTERESIS):
            _dominant = "t"

    if _dominant == "t":
        e_dom = e_t
        e_dom_n = e_t_n
        prev = _prev_e_t
    else:
        e_dom = e_h
        e_dom_n = e_h_n
        prev = _prev_e_h

    # 3. 主導誤差的 d_error（用該通道自己的上一次值，避免換手脈衝）
    if _initialized:
        d_e = (e_dom - prev) / DT if DT > 0 else 0.0
    else:
        d_e = 0.0
        _initialized = True
    _prev_e_t = e_t
    _prev_e_h = e_h

    de_n = max(-1.5, min(1.5, d_e / DERR_SCALE)) if DERR_SCALE > 0 else 0.0

    # 4. 主 Fuzzy：3x3 規則 + 單例重心法
    mu_e = _fuzzify_pn(e_dom_n)
    mu_de = _fuzzify_pn(de_n)
    num = 0.0
    den = 0.0
    for i in range(3):
        for j in range(3):
            w = min(mu_e[i], mu_de[j])
            if w > 0.0:
                num += w * _RULES[i][j]
                den += w
    u = (num / den) if den > 0.0 else 0.0  # u ∈ [-1, 1]

    # 5. Self-Tuning：更新滑動視窗 → 算指標 → 算 target_Ku → 慢速逼近
    _e_dom_window.append(e_dom_n)
    if len(_e_dom_window) > TUNE_WINDOW:
        _e_dom_window.pop(0)

    if len(_e_dom_window) >= 2:
        avg_abs_e = sum(abs(v) for v in _e_dom_window) / len(_e_dom_window)
        flip_ratio = _count_sign_flips(_e_dom_window) / (len(_e_dom_window) - 1)
        target_ku = _self_tune_target(avg_abs_e, flip_ratio, ku_base_eff)
        _ku += TUNE_ALPHA * (target_ku - _ku)
        # 硬夾持（target 已在範圍內，這層擋住極端積誤）
        _ku = max(ku_base_eff * 0.8, min(ku_base_eff * 1.2, _ku))

    # 6. 輸出：u * Ku，套使用者限幅
    if out_max < out_min:
        out_min, out_max = out_max, out_min
    output = u * _ku
    is_saturated = output >= out_max or output <= out_min
    output = max(out_min, min(out_max, output))

    _call_count += 1
    if _call_count <= WARMUP_TICKS:
        status = make_status(AlgoStatus.WARMUP)
    elif is_saturated:
        status = make_status(AlgoStatus.SATURATED)
    else:
        status = make_status(AlgoStatus.OK)

    return make_result({"out": round(output, 4)}, status)
