#!/usr/bin/env python3
"""从渲染 PDF 提取每个标题的真实正文页码，生成 docs/entries.json（供 add_toc_placeholders --entries 使用）。
用法：在 docs 目录下执行  python compute_pages.py   （无需参数，输入输出路径由脚本位置推导）
"""
import json
import re
import zipfile
from pathlib import Path

import pymupdf

# 输入输出全部由脚本位置推导（项目根 = docs 的上一级），无命令行参数、无 ../ 字面量
_ROOT = Path(__file__).resolve().parent.parent
_DOCX = _ROOT / "使用说明.docx"
_PDF = _ROOT / "docs" / "render" / "使用说明.pdf"
_ENTRIES = _ROOT / "docs" / "entries.json"

if not _DOCX.is_file():
    raise SystemExit(f"未找到文档：{_DOCX}")
if not _PDF.is_file():
    raise SystemExit(f"未找到渲染 PDF：{_PDF}")

# 1. 从 docx 提取 H1/H2 标题（按文档顺序）
with zipfile.ZipFile(_DOCX) as z:
    xml = z.read("word/document.xml").decode("utf-8")
headings = []
for m in re.finditer(r"<w:p>(?:(?!</w:p>).)*</w:p>", xml, re.S):
    para = m.group(0)
    st = re.search(r'<w:pStyle w:val="Heading([12])"/>', para)
    if not st:
        continue
    text = "".join(re.findall(r"<w:t[^>]*>([^<]*)</w:t>", para))
    if text.strip():
        headings.append({"level": int(st.group(1)), "text": text.strip()})

# 2. 找最后一个目录页（含刷新提示）
pdf = pymupdf.open(_PDF)
last_toc = 0
for i, page in enumerate(pdf):
    if "更新域" in page.get_text():
        last_toc = i + 1  # 1-based；正文第 1 页 = pdf 第 last_toc+1 页

# 3. 在正文页中定位每个标题的真实页码
entries = []
for h in headings:
    needle = re.sub(r"\s+", "", h["text"])
    found = None
    for i in range(last_toc, len(pdf)):
        page_text = re.sub(r"\s+", "", pdf[i].get_text())
        if needle in page_text:
            found = i + 1 - last_toc  # 正文页码（阿拉伯，从 1 起）
            break
    entries.append({"level": h["level"], "text": h["text"], "page": str(found if found else 1)})

_ENTRIES.write_text(json.dumps(entries, ensure_ascii=False), encoding="utf-8")
print("last_toc_pdf_page =", last_toc, "| headings =", len(headings))
for e in entries:
    print(f"  L{e['level']} 第{e['page']}页  {e['text']}")
