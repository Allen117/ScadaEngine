# @algorithm: kW 計算
# @variadic: true
# @inputs_repeat: V:Voltage, A:Current, PF:Power Factor
# @outputs_repeat: out:kW
# @description: 三相 kW = V * A * PF * 1.732 / 1000
from _status import AlgoStatus, make_status, make_result


def evaluate_one(V, A, PF):
    """
    三相 kW = V * A * PF * 1.732 / 1000

    Status（業務語意，由演算法主動回傳）:
        INPUT_OUT_OF_RANGE (Warning) — PF 不在 [0, 1] 範圍（仍可算，給警告）

    框架處理：缺輸入 / 型別錯 → INPUT_MISSING；其他例外 → INTERNAL_ERROR。
    """
    out = V * A * PF * 1.732 / 1000
    if PF < 0 or PF > 1:
        return make_result({"out": out}, make_status(AlgoStatus.INPUT_OUT_OF_RANGE))
    return {"out": out}
