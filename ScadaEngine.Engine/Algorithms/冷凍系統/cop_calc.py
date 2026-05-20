# -*- coding: utf-8 -*-
# @algorithm: COP計算
# @variadic: true
# @inputs_repeat: cooling_capacity:冷凍能力, power:功率
# @outputs_repeat: cop:COP
# @description: 計算冰水機 COP = 冷凍能力 / 功率（可同時計算多組）


def evaluate_one(cooling_capacity, power):
    """COP = 冷凍能力 / 功率。框架自動處理 power=0（ZeroDivisionError → DIVIDE_BY_ZERO）與輸入缺漏。"""
    return {"cop": cooling_capacity / power}
