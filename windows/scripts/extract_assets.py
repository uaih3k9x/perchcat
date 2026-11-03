#!/usr/bin/env python3
"""从 Swift 版内置常量重新生成 PerchCat.Core/Assets/*.json.

用法 (在仓库根目录): python3 windows/scripts/extract_assets.py
Swift 端 (Sources/PerchCatCore/BuiltinSkin.swift) 才是源头, 这里的 JSON 不要手改.
"""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]
src = (root / "Sources/PerchCatCore/BuiltinSkin.swift").read_text(encoding="utf-8")
out = root / "windows/PerchCat.Core/Assets"
names = {"builtinSkinJSON": "skin.json", "builtinScruffJSON": "scruff.json", "builtinShakeJSON": "shake.json"}

for const, fname in names.items():
    m = re.search(r'let ' + const + r' = #"""\n(.*?)\n"""#', src, re.S)
    if not m:
        sys.exit(f"找不到 {const}")
    json.loads(m.group(1))  # 必须是合法 JSON
    (out / fname).write_text(m.group(1), encoding="utf-8", newline="")
    print(f"{fname}: {len(m.group(1))} bytes")
