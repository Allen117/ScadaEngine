#!/usr/bin/env bash
# Claude Code Stop hook — 收尾時若工作區有 .cs 變更就跑 dotnet test。
#   通過 / 沒動 .cs → 靜默放行
#   失敗           → 擋下 stop，把錯誤摘要餵回 Claude 當場修正
#
# 註：Stop hook 無法只看「本輪」的編輯，故以「工作區未提交的 .cs 變更」為判準
#     （tracked 已改 + staged + untracked）。長期掛著未提交的 .cs 會每次觸發，屬預期。
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0
cd "$ROOT" || exit 0

# 有無 .cs 變更（bin/obj 由 .gitignore 排除，不會誤觸）
changed="$(
  { git diff --name-only -- '*.cs'
    git diff --name-only --cached -- '*.cs'
    git ls-files --others --exclude-standard -- '*.cs'
  } 2>/dev/null
)"
[ -z "$changed" ] && exit 0   # 沒動 .cs → 靜默略過

# 跑測試（安靜輸出）
out="$(dotnet test ScadaEngine.Tests/ScadaEngine.Tests.csproj --nologo -v quiet 2>&1)"
rc=$?

if [ "$rc" -eq 0 ]; then
  # 全綠：靜默放行（如需每次提示可改成輸出 systemMessage）
  exit 0
fi

# 紅燈：擋下 stop 並回報 Claude
summary="$(printf '%s' "$out" | grep -iE 'error|failed|失敗|Passed!' | tail -20)"
msg="dotnet test 失敗，請修正後再收尾：
${summary}"

if command -v jq >/dev/null 2>&1; then
  reason="$(printf '%s' "$msg" | jq -Rs .)"
  printf '{"decision":"block","reason":%s}\n' "$reason"
else
  printf '{"decision":"block","reason":"dotnet test 失敗，請查看終端輸出並修正後再收尾。"}\n'
fi
exit 0
