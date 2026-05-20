# -*- coding: utf-8 -*-
"""
inject_header.py — 把 metadata 補到演算法檔案頂端，只補缺漏不覆蓋既存。

用法:
    python inject_header.py --file path/to/algo.py --meta '<json-string>'
    python inject_header.py --file path/to/algo.cs --meta-file meta.json

meta JSON 範例:
    {
      "algorithm": "COP計算",
      "variadic": "true",
      "inputs_repeat": "cooling_capacity:冷凍能力, power:功率",
      "outputs_repeat": "cop:COP",
      "description": "計算冰水機 COP = 冷凍能力 / 功率（可同時計算多組）"
    }

行為:
- 偵測檔案編碼 BOM、line ending（CRLF/LF）並保留
- Python 用 `# @key: value`，C# 用 `// @key: value`
- 已存在的 key 一律跳過（idempotent）
- 插入點：Python 在 `# -*- coding -*-` 之後（無則檔頂）；C# 在檔頂
  若已有 metadata 行，新行接在最後一個 metadata 行之後
- 印 unified diff 到 stdout

退出碼:
    0  成功（含 no-op）
    1  錯誤
"""

import argparse
import difflib
import json
import re
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


METADATA_ORDER = [
    "algorithm",
    "variadic",
    "inputs",
    "inputs_repeat",
    "inputs_fixed",
    "outputs",
    "outputs_repeat",
    "outputs_fixed",
    "description",
]

PY_HEADER_RE = re.compile(r"^\s*#\s*@(\w+)\s*:")
CS_HEADER_RE = re.compile(r"^\s*//\s*@(\w+)\s*:")
CODING_RE = re.compile(r"^\s*#.*coding[:=]")


def detect_bom(raw: bytes) -> tuple[str, bytes]:
    if raw.startswith(b"\xef\xbb\xbf"):
        return "utf-8-sig", raw[3:]
    return "utf-8", raw


def detect_newline(text: str) -> str:
    if "\r\n" in text:
        return "\r\n"
    if "\r" in text and "\n" not in text:
        return "\r"
    return "\n"


def inject(filepath: Path, meta: dict[str, str]) -> tuple[bool, str]:
    raw = filepath.read_bytes()
    encoding, body = detect_bom(raw)
    has_bom = encoding == "utf-8-sig"
    text = body.decode("utf-8")
    newline = detect_newline(text)
    lines = text.split(newline)

    is_python = filepath.suffix.lower() == ".py"
    header_re = PY_HEADER_RE if is_python else CS_HEADER_RE
    prefix = "# @" if is_python else "// @"

    existing_keys: set[str] = set()
    last_header_line = -1
    for i, line in enumerate(lines[:15]):
        m = header_re.match(line)
        if m:
            existing_keys.add(m.group(1))
            last_header_line = i

    # 過濾掉已存在的 key
    to_add = [(k, meta[k]) for k in METADATA_ORDER if k in meta and k not in existing_keys]
    if not to_add:
        return False, "(no missing metadata; nothing to inject)"

    # 決定插入位置：last_header_line+1，或 coding 宣告之後，或檔頂
    if last_header_line >= 0:
        insert_at = last_header_line + 1
    else:
        insert_at = 0
        if is_python and lines and CODING_RE.match(lines[0]):
            insert_at = 1
        # C# 一律插在最頂端

    new_block = [f"{prefix}{k}: {v}" for k, v in to_add]
    new_lines = lines[:insert_at] + new_block + lines[insert_at:]
    new_text = newline.join(new_lines)

    # 寫回
    new_bytes = new_text.encode("utf-8")
    if has_bom:
        new_bytes = b"\xef\xbb\xbf" + new_bytes
    filepath.write_bytes(new_bytes)

    diff = "".join(
        difflib.unified_diff(
            [l + "\n" for l in lines],
            [l + "\n" for l in new_lines],
            fromfile=str(filepath) + " (before)",
            tofile=str(filepath) + " (after)",
            n=2,
        )
    )
    return True, diff


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--meta", help="JSON string")
    group.add_argument("--meta-file", help="path to JSON file")
    args = parser.parse_args()

    if args.meta:
        meta_raw = args.meta
    else:
        meta_raw = Path(args.meta_file).read_text(encoding="utf-8")

    try:
        meta = json.loads(meta_raw)
    except json.JSONDecodeError as e:
        print(f"ERROR: meta JSON invalid: {e}", file=sys.stderr)
        return 1
    if not isinstance(meta, dict):
        print("ERROR: meta must be a JSON object", file=sys.stderr)
        return 1

    filepath = Path(args.file)
    if not filepath.exists():
        print(f"ERROR: file not found: {filepath}", file=sys.stderr)
        return 1

    changed, message = inject(filepath, {k: str(v) for k, v in meta.items()})
    if changed:
        print(message)
    else:
        print(message)
    return 0


if __name__ == "__main__":
    sys.exit(main())
