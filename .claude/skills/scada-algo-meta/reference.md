# SCADA 演算法 metadata 規範對照

> 此檔給 skill 自己讀，列出三種演算法型態（固定/變參/混合）的完整範例，方便 skill 組正確的 meta 結構。

## 全部 9 種 metadata 標記

| 標記 | 型態 | 用途 | 範例 |
|---|---|---|---|
| `@algorithm` | 字串 | LogicFlow node 顯示名（中文） | `# @algorithm: COP計算` |
| `@inputs` | comma list of keys | 非變參演算法的輸入 key 列 | `# @inputs: V, A, PF` |
| `@outputs` | comma list of keys | 非變參演算法的輸出 key 列 | `# @outputs: out` |
| `@description` | 字串 | UI tooltip 說明文 | `# @description: 三相功率計算` |
| `@variadic` | true / false | 啟用變參迭代（true 啟用） | `# @variadic: true` |
| `@inputs_repeat` | comma list of `key:label` | 變參模式下會加 suffix 的輸入 | `# @inputs_repeat: cooling_capacity:冷凍能力, power:功率` |
| `@inputs_fixed` | comma list of `key:label` | 變參模式下**不**加 suffix 的輸入 | `# @inputs_fixed: setpoint:設定值` |
| `@outputs_repeat` | comma list of `key:label` | 變參會加 suffix 的輸出 | `# @outputs_repeat: cop:COP` |
| `@outputs_fixed` | comma list of `key:label` | 變參不加 suffix 的輸出 | `# @outputs_fixed: total:總和` |

> C# 同名標記，把 `#` 換成 `//`。

## 型態 A：固定輸入輸出（非變參）

`@variadic` 預設 false 或省略，用 `@inputs` / `@outputs`：

```python
# -*- coding: utf-8 -*-
# @algorithm: kW計算
# @inputs: V, A, PF
# @outputs: out
# @description: 三相功率計算 kW = V * A * PF * 1.732 / 1000

def evaluate_one(V, A, PF):
    return {"out": V * A * PF * 1.732 / 1000}
```

C# 對應：

```csharp
// @algorithm: COP計算(C#)
// @inputs: cooling_capacity, power
// @outputs: out
// @description: C# 版 COP 計算
public static class CopCalcCsharp
{
    public static AlgorithmResult EvaluateOne(double cooling_capacity, double power)
        => AlgorithmResult.Ok(new() { ["out"] = cooling_capacity / power });
}
```

## 型態 B：純變參（所有 input/output 都重複）

`@variadic: true` + `@inputs_repeat` + `@outputs_repeat`：

```python
# -*- coding: utf-8 -*-
# @algorithm: COP計算
# @variadic: true
# @inputs_repeat: cooling_capacity:冷凍能力, power:功率
# @outputs_repeat: cop:COP
# @description: 計算冰水機 COP = 冷凍能力 / 功率（可同時計算多組）

def evaluate_one(cooling_capacity, power):
    return {"cop": cooling_capacity / power}
```

執行期框架會以 n=1,2,3,... 重複呼叫 `evaluate_one`，從 `inputs` dict 取 `cooling_capacity1`、`power1` → `cooling_capacity2`、`power2` …，輸出寫成 `cop1`, `cop2`, ...

## 型態 C：混合（部分輸入固定、部分重複）

```python
# @algorithm: 偏差比較
# @variadic: true
# @inputs_repeat: actual:實測值
# @inputs_fixed: setpoint:設定值
# @outputs_repeat: deviation:偏差

def evaluate_one(actual, setpoint):
    return {"deviation": actual - setpoint}
```

執行期：`setpoint` 在每次迭代取相同值，`actual` 依 `actual1`, `actual2`, ... 取。

## 關鍵規則

1. `@variadic: true` 時，**必有** `@inputs_repeat`，且其 key 必對應 `evaluate_one` 參數名
2. `@inputs` 與 `@inputs_repeat` **不要同時出現**（前者非變參用，後者變參用）；同理 outputs
3. `key:label` 的 label 留空（`cop:` 或 `cop`）= label 等於 key
4. `evaluate_one` / `EvaluateOne` 是**框架硬性介面**，函式名不能改
5. 解析器只讀檔案**前 15 行**的 `@xxx:` 標記 — 超過行數會被忽略

## inject_header.py 寫入順序

腳本依以下順序插入新欄位（已存在的跳過）：

```
algorithm
variadic
inputs
inputs_repeat
inputs_fixed
outputs
outputs_repeat
outputs_fixed
description
```

非變參只會用到 `algorithm` / `inputs` / `outputs` / `description`；變參會用到 `algorithm` / `variadic` / `inputs_repeat` / `inputs_fixed` / `outputs_repeat` / `outputs_fixed` / `description`。
