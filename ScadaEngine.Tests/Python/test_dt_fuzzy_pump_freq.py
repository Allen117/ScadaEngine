# -*- coding: utf-8 -*-
"""
dt_fuzzy_pump_freq（溫差Fuzzy水泵頻率）單元測試 harness。

執行方式（不需 pytest，直接跑）：
    python ScadaEngine.Tests/Python/test_dt_fuzzy_pump_freq.py

放在 Tests 專案下而非 Algorithms/ 內，避免被演算法服務的 _discover_algorithms 掃描 import。
時間視窗輪次（決策 3）以 FakeTime 替身控制，不依賴真實時鐘。
"""

import importlib
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_ALGO_DIR = os.path.abspath(os.path.join(_HERE, "..", "..", "ScadaEngine.Engine", "Algorithms"))
sys.path.insert(0, _ALGO_DIR)                                   # _status.py
sys.path.insert(0, os.path.join(_ALGO_DIR, "控制演算法"))        # dt_fuzzy_pump_freq.py

import dt_fuzzy_pump_freq as algo  # noqa: E402


class FakeTime:
    """monotonic() 替身：advance() 控制輪次邊界（>= 0.5s 即新輪）。"""
    def __init__(self):
        self.t = 1000.0

    def monotonic(self):
        return self.t

    def advance(self, sec):
        self.t += sec


def fresh():
    """重載模組（清跨呼叫狀態）並注入 FakeTime。"""
    importlib.reload(algo)
    ft = FakeTime()
    algo.time = ft
    return ft


def call(t_in=12.0, t_out=7.0, dt_target=5.0,
         e_scale=3.0, de_scale=1.0, step_max=2.0, deadband=0.3,
         run=1, freq_fb=45.0, freq_min=20.0, freq_max=60.0):
    r = algo.evaluate_one(t_out, t_in, dt_target, e_scale, de_scale, step_max, deadband,
                          run, freq_fb, freq_min, freq_max)
    return r["result"]["freq"], r["status"]["statusCodeName"]


def warm_up(ft, **kw):
    """跑完 WARMUP_ROUNDS 輪（每輪推進 1 秒）。"""
    for _ in range(algo.WARMUP_ROUNDS):
        call(**kw)
        ft.advance(1.0)


_passed = 0
_failed = []


def check(name, cond, detail=""):
    global _passed
    if cond:
        _passed += 1
        print(f"  PASS  {name}")
    else:
        _failed.append(name)
        print(f"  FAIL  {name}  {detail}")


# ── 1. WARMUP：啟動前 N 輪 status=WARMUP、頻率維持原值 ──
ft = fresh()
freq, st = call(freq_fb=45.0)
check("WARMUP 第 1 輪 status", st == "WARMUP", f"got {st}")
check("WARMUP 第 1 輪頻率維持原值", freq == 45.0, f"got {freq}")
ft.advance(1.0)
freq, st = call(freq_fb=45.0)
check("WARMUP 第 2 輪 status", st == "WARMUP", f"got {st}")
ft.advance(1.0)
freq, st = call(freq_fb=45.0)
check("第 3 輪起脫離 WARMUP", st != "WARMUP", f"got {st}")

# ── 2. 控制方向：溫差 > 目標（e>0，流量不足）→ 頻率上升 ──
ft = fresh()
warm_up(ft, t_in=14.0)          # e = (14-7) - 5 = 2
freq, st = call(t_in=14.0, freq_fb=45.0)
check("e>0 → 頻率上升", freq > 45.0, f"got {freq}")
check("e>0 且未觸界 status=OK", st == "OK", f"got {st}")

# ── 3. 控制方向：溫差 < 目標（e<0）→ 頻率下降 ──
ft = fresh()
warm_up(ft, t_in=10.0)          # e = (10-7) - 5 = -2
freq, st = call(t_in=10.0, freq_fb=45.0)
check("e<0 → 頻率下降", freq < 45.0, f"got {freq}")

# ── 4. deadband：|e| 低於不動作帶 → 頻率不動、status=OK ──
ft = fresh()
warm_up(ft, t_in=12.2)          # e = 0.2 < deadband 0.3
freq, st = call(t_in=12.2, freq_fb=45.0)
check("deadband 內頻率不動", freq == 45.0, f"got {freq}")
check("deadband 內 status=OK", st == "OK", f"got {st}")

# ── 5. 限幅 + SATURATED：觸頂夾在 freq_max ──
ft = fresh()
warm_up(ft, t_in=17.0)          # e = 5，大誤差
freq, st = call(t_in=17.0, freq_fb=59.5, freq_max=60.0)
check("觸頂夾在 Max", freq == 60.0, f"got {freq}")
check("觸頂回 SATURATED", st == "SATURATED", f"got {st}")

ft = fresh()
warm_up(ft, t_in=8.0)           # e = -4，大負誤差
freq, st = call(t_in=8.0, freq_fb=20.5, freq_min=20.0)
check("觸底夾在 Min", freq == 20.0, f"got {freq}")
check("觸底回 SATURATED", st == "SATURATED", f"got {st}")

# ── 6. 停機泵（run=0）：輸出維持原值、status=OK（決策 5）──
ft = fresh()
warm_up(ft, t_in=17.0)
freq, st = call(t_in=17.0, run=0, freq_fb=33.3)
check("run=0 頻率維持原值", freq == 33.3, f"got {freq}")
check("run=0 status=OK", st == "OK", f"got {st}")

# ── 7. 同輪多泵一致性（決策 3）：同輪第 2 台泵沿用同一份 (e, de) ──
ft = fresh()
warm_up(ft, t_in=13.0)          # e = 1
call(t_in=14.0, freq_fb=45.0)   # 新輪：e = 2、de = 1
freq1, _ = call(t_in=14.0, freq_fb=50.0)   # 同輪（未推進時鐘）泵 1
freq2, _ = call(t_in=14.0, freq_fb=50.0)   # 同輪泵 2 — 若 de 被重算成 0，Δf 會不同
check("同輪兩泵 Δf 一致", freq1 == freq2, f"got {freq1} vs {freq2}")
delta_same_round = freq1 - 50.0

ft = fresh()
warm_up(ft, t_in=13.0)
call(t_in=14.0, freq_fb=45.0)
ft.advance(1.0)                 # 推進成新輪：e 不變 → de = 0
freq3, _ = call(t_in=14.0, freq_fb=50.0)
check("跨輪 de 歸零後 Δf 不同（驗證同輪快取確實生效）",
      abs((freq3 - 50.0) - delta_same_round) > 1e-9,
      f"same-round Δf={delta_same_round}, new-round Δf={freq3 - 50.0}")

# ── 8. Min/Max 對調防呆 ──
ft = fresh()
warm_up(ft, t_in=17.0)
freq, st = call(t_in=17.0, freq_fb=59.5, freq_min=60.0, freq_max=20.0)  # 對調
check("Min/Max 對調仍正確夾住", freq == 60.0, f"got {freq}")

# ── 結果 ──
print(f"\n{_passed} passed, {len(_failed)} failed")
if _failed:
    sys.exit(1)
