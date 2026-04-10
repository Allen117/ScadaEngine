# -*- coding: utf-8 -*-
# @algorithm: COP計算
# @inputs: cooling_capacity, power
# @outputs: out
# @description: 計算冷凍效率 COP = 冷凍能力 / 功率

def evaluate(inputs: dict) -> dict:
    """
    COP (Coefficient of Performance) = 冷凍能力 / 輸入功率
    inputs:
        cooling_capacity: 冷凍能力 (kW)
        power: 輸入功率 (kW)
    outputs:
        out: COP 值
    """
    cp = inputs.get("cooling_capacity", 0)
    pw = inputs.get("power", 0)
    return {"out": cp / pw if pw != 0 else 0}
