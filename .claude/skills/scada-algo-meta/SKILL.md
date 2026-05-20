---
name: scada-algo-meta
description: "Use this skill when the user wants to generate, complete, or fix metadata headers (the `# @algorithm:` / `// @algorithm:` block) on a SCADA-style algorithm file (`.py` / `.cs`) whose entry function is `evaluate_one` / `EvaluateOne`. Triggers on phrases like 「補 metadata」「補 header」「補上 @algorithm」「補頭」「為 xxx.py / xxx.cs 加上 metadata」. Do NOT use this skill to create a new algorithm from scratch, write the implementation, rename a category folder, or change runtime behavior — this skill only fills in the comment-header metadata that the algorithm host service parses for LogicFlow / UI rendering."
---

# scada-algo-meta — SCADA 演算法 metadata header 補頭工具

## 目的

開發者寫好 `evaluate_one(...)` / `EvaluateOne(...)` 後，常忘記或寫錯檔頂的 `# @xxx:` / `// @xxx:` metadata。這個 skill 由腳本解析簽名 + return + 既有 header，自動補上缺漏欄位；不能靜態推斷的（中文 label、是否 variadic）才問使用者。

**只補缺漏，不覆蓋既存欄位** — 可重複執行（idempotent）。

## 適用範圍

- Python 演算法檔（`.py`）— 主邏輯函式為 `evaluate_one(...)`
- C# 演算法檔（`.cs`）— 主邏輯函式為 `EvaluateOne(...)`
- 檔案必須**已存在**且至少有 `evaluate_one` / `EvaluateOne` 函式（無則 skill 會偵測候選函式並建議 rename）

> 與專案路徑無關 — 不論演算法檔放在哪個資料夾，只要使用者指明檔案位置即可。腳本一律以 `--file <絕對或相對路徑>` 操作。

**不在範圍**：
- 從零產生演算法骨架
- 修演算法實作 / 算法分類資料夾
- 處理 `_*.py` / `_*.cs` 共用模組（這些不會被當演算法載入）

## metadata 規範對照表

| 欄位 | Python | C# | 必填 | 用途 |
|---|---|---|---|---|
| `algorithm` | `# @algorithm: 中文名` | `// @algorithm: 中文名` | 是 | LogicFlow node 顯示名 |
| `inputs` | `# @inputs: a, b` | `// @inputs: a, b` | 固定型必填 | 非變參輸入 key 列 |
| `outputs` | `# @outputs: out` | `// @outputs: out` | 固定型必填 | 非變參輸出 key 列 |
| `description` | `# @description: 說明` | `// @description: 說明` | 否 | UI tooltip |
| `variadic` | `# @variadic: true` | `// @variadic: true` | 變參型必填 | 啟用變參迭代 |
| `inputs_repeat` | `# @inputs_repeat: k:label, ...` | 同左 | 變參必填 | 變參迭代會加 suffix 的輸入 |
| `inputs_fixed` | `# @inputs_fixed: k:label, ...` | 同左 | 變參選填 | 變參模式下**不**加 suffix 的輸入 |
| `outputs_repeat` | `# @outputs_repeat: k:label, ...` | 同左 | 變參必填 | 變參會加 suffix 的輸出 |
| `outputs_fixed` | `# @outputs_fixed: k:label, ...` | 同左 | 變參選填 | 變參模式下不加 suffix 的輸出 |

詳細範例與規則見 [reference.md](reference.md)。

## 工作流

每次 skill 被觸發，依下列步驟逐項執行：

### 1. 確認目標檔案
- 使用者通常會講出檔名（「幫 cop_calc.py 補 metadata」）。若沒給，問清楚。
- 用 Glob / Read 確認檔案存在。
- 判斷副檔名：`.py` → Python 分支；`.cs` → C# 分支。

### 2. 跑 inspect 腳本
從 repo root 執行：

```bash
# Python
python .claude/skills/scada-algo-meta/scripts/inspect_py.py --file <檔案路徑>

# C#
python .claude/skills/scada-algo-meta/scripts/inspect_cs.py --file <檔案路徑>
```

輸出 JSON。解析欄位：

- `ok: false, reason: "evaluate_one_missing"` → 進入 step 3（rename 流程）
- `ok: true` → 繼續 step 4

### 3. evaluate_one 缺失 → 提議 rename

inspect 輸出 `candidate_funcs` 列出檔內所有非 `_` 開頭的 top-level 函式：

- 0 個 → 直接報錯「檔案沒有可作為主邏輯的函式」，請使用者先補實作
- 1 個 → 詢問「要把 `{name}` 改名為 `evaluate_one`/`EvaluateOne` 嗎？(y/n)」
- 多個 → 列出讓使用者挑

確認後，用 **Edit 工具 `replace_all=true`** 改：
- Python：`def {old}(` → `def evaluate_one(`；`{old}(` 函式呼叫也要改
- C#：`{Old}(` → `EvaluateOne(`

改完後**重新 inspect** 一次，繼續 step 4。

> ⚠️ rename 後用 Grep 掃舊名（`{old}\b`）一次，若還有命中要警告使用者人工確認剩下的是否該改。

### 4. 處理 outputs uncertain / inconsistent

- `outputs_uncertain: true`（Python：return 非字面量 dict；C#：找不到任何 `["key"]=` 字面）
  → 印警告 + docstring + signature → **必須問**使用者：「無法靜態解析 output keys，請逐個列出（以逗號分隔）」
- `outputs_inconsistent: true`（C# 多分支 key set 不同）
  → 印 `all_output_sets` → 問使用者「要全聯集（{merged}）還是手動指定？」

### 5. 互動詢問必要欄位

針對「不在 `existing` 內」的欄位才問：

1. **演算法中文名**（`algorithm`）— 必問
2. **是否 variadic**（`variadic`）— 必問，y/n。若 y，預設所有 inputs/outputs 都視為 repeat（fixed 在 v1 不支援自動分割；使用者需手動編輯）
3. **各 input/output 中文 label** — 逐個問；空白 Enter 跳過則 label = key（與 `_parse_kv_list` 一致）
4. **description**（選填）— 若 inspect 抓到 docstring，建議使用者直接拿來用；否則問一句

`existing` 已有的欄位**不要問也不要改**，靜默跳過。

### 6. 組 meta dict + 呼叫 inject 腳本

依使用者回答組 meta JSON：

- 非變參：`{ "algorithm": "...", "inputs": "a, b", "outputs": "out", "description": "..." }`
- 變參：`{ "algorithm": "...", "variadic": "true", "inputs_repeat": "a:標籤A, b:標籤B", "outputs_repeat": "out:標籤", "description": "..." }`

跑：

```bash
python .claude/skills/scada-algo-meta/scripts/inject_header.py --file <檔案路徑> --meta '<json>'
```

> 注意：bash 下傳 JSON 用單引號包整串，內含的雙引號不用 escape。Windows shell 用 `--meta-file` 避免引號地獄：先 Write 一個 tmp `.json` 再帶路徑。

### 7. 印 diff + 重啟提示

inject 腳本會自動印 unified diff。skill 額外提示使用者：

> ⚠️ metadata 影響演算法載入時 UI / port 對應（LogicFlow node 的 input/output port 與 label）。**載入此檔的演算法 host 服務需重啟，才會 re-scan 新 header**。具體重啟方式依專案而定（例如 Python uvicorn 重啟、C# Worker Service `dotnet run` 重跑等）。

## 範例對話

### 範例 1：cop_calc.py 從零補完整 header（變參型）

```
User: 幫我補 cop_calc.py 的 metadata
Skill:
  → 跑 inspect_py.py
  → 偵測：inputs=[cooling_capacity, power], outputs=[cop], 沒有任何 existing header
  → 提示：偵測到 inputs / outputs，docstring="COP = 冷凍能力 / 功率..."

  問 1：演算法中文名？ → User: COP計算
  問 2：是否 variadic？ → User: y
  問 3：cooling_capacity 中文 label？ → User: 冷凍能力
  問 4：power 中文 label？ → User: 功率
  問 5：cop 中文 label？ → User: COP
  問 6：description 要用 docstring 第一行嗎？ → User: 改成「計算冰水機 COP = 冷凍能力 / 功率（可同時計算多組）」

  → 組 meta：{ "algorithm": "COP計算", "variadic": "true",
              "inputs_repeat": "cooling_capacity:冷凍能力, power:功率",
              "outputs_repeat": "cop:COP",
              "description": "計算冰水機 COP = 冷凍能力 / 功率（可同時計算多組）" }
  → 跑 inject_header.py
  → 印 diff，提示重啟 Python 演算法服務
```

### 範例 2：kWIcalc.py 已部分有 header，只補缺漏

```
User: 補 kWIcalc.py 缺的 description
Skill:
  → 跑 inspect_py.py
  → existing 已有 algorithm/inputs/outputs/description — 全齊
  → inject 印「(no missing metadata; nothing to inject)」
  → 提示使用者「無缺漏，跳過」
```

### 範例 3：函式叫 calc_cop（rename 流程）

```
User: 補 my_algo.py 的 metadata
Skill:
  → 跑 inspect_py.py → ok=false, reason=evaluate_one_missing, candidate_funcs=[calc_cop]
  → 提示：「檔案沒有 evaluate_one，但偵測到 calc_cop。要 rename 為 evaluate_one 嗎？(y/n)」
  → User: y
  → 用 Edit (replace_all) 改 `def calc_cop(` → `def evaluate_one(`，並改檔案內所有 `calc_cop(` 呼叫
  → Grep 確認舊名已清乾淨
  → 重新 inspect_py.py → ok=true
  → 繼續正常 metadata 補頭流程
```

### 範例 4：Python return 非字面量 → outputs_uncertain

```
User: 補 dynamic_algo.py
Skill:
  → 跑 inspect_py.py → outputs_uncertain=true, outputs=[]
  → 提示：「無法從 return 靜態抽出 output key（程式碼用了非字面量 dict）。請列出 output keys，逗號分隔：」
  → User: temp, humidity
  → outputs = ["temp", "humidity"]
  → 繼續詢問 label
```

## 邊界與注意事項

- **檔案有 BOM / CRLF**：inject 腳本會偵測並保留，無須特別處理
- **rename 全引用**：Edit 用 `replace_all=true` 改 `def {old}(` 與 `{old}(`，但若 docstring / 註解內提及舊名，使用者要自己決定要不要改
- **`_*.py` / `_*.cs`**：skill 不處理這些共用模組，若使用者誤指要明白拒絕
- **variadic 不猜**：無論檔名或參數名怎麼長都不從程式碼推測 variadic，**必問使用者**
- **outputs 抓不到一律問**：絕不靜默預設成 `["out"]` — output key 與 LogicFlow port 必須完全一致
- **fixed inputs/outputs 在 v1 不自動分割**：variadic=true 時所有 inputs/outputs 預設都進 `_repeat`，使用者若需要 fixed 要事後手動編輯
