# @algorithm: kW/RT 計算
# @variadic: true
# @inputs_repeat: kW:耗電量, RT:冷凍噸
# @outputs_repeat: kW/RT:kW/RT
# @description: 越低越好
def evaluate_one(kW, RT):
    return {"kW/RT": kW / RT}
