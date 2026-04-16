# -*- coding: utf-8 -*-
# @algorithm: kW計算
# @inputs: V,A,PF
# @outputs: out
# @description: 計算冷凍效率 COP = 冷凍能力 / 功率

def evaluate(inputs: dict) -> dict:
    """
    kW = V*A*PF*1.732/1000
    inputs:
        V: V (V)
        A: A (A)
	PF:PF
    outputs:
        out: kW 值
    """
    V = inputs.get("V", 0)
    A = inputs.get("A", 0)
    PF = inputs.get("PF", 0)
    return {"out": V*A*PF*1.732/1000}
