# -*- coding: utf-8 -*-
# @algorithm: kW/RT 計算
# @variadic: true
# @inputs_repeat: kW:耗電量, RT:冷凍噸
# @outputs_repeat: kW/RT:kW/RT
# @description: 越低越好；RT=0 由演算法主動回 DIVIDE_BY_ZERO（示範使用者主動回傳 status）
from _status import AlgoStatus, make_status, make_result


def evaluate_one(kW, RT):
    """
    kW/RT = 耗電量 / 冷凍噸。

    Status（業務語意，由演算法主動回傳）:
        DIVIDE_BY_ZERO (Error) — RT=0 時主動回傳，省去框架 ZeroDivisionError 例外路徑

    註：若直接寫 kW / RT 而不檢查，框架也會在 ZeroDivisionError 時自動套 DIVIDE_BY_ZERO；
        此處顯式檢查純粹示範「使用者主動回 status」的寫法。
    """
    if RT == 0:
        return make_result({"kW/RT": 0}, make_status(AlgoStatus.DIVIDE_BY_ZERO))
    return make_result({"kW/RT": kW / RT}, make_status(AlgoStatus.OK))
