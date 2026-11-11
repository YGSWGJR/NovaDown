#!/usr/bin/env python3
"""Post-process 使用说明.docx (v2):
1. Remove the duplicate first TOC entry that add_toc_placeholders injects
   INSIDE the TOC field paragraph (renders without page number -> duplicate line)
2. Compress TOC entry spacing so the TOC fits on one page
3. Remove empty <w:pgNumType/> (WPS compatibility)
4. Patch footer PAGE fields: first footer (TOC section) -> ROMAN, later -> arabic
"""
import re
import shutil
import zipfile
from pathlib import Path

# 目标固定为项目根下的使用说明.docx（无命令行参数，路径由脚本位置推导）
_ROOT = Path(__file__).resolve().parent.parent
src = _ROOT / "使用说明.docx"
if not src.is_file():
    raise SystemExit(f"未找到文档：{src}")
tmp = src.with_suffix(src.suffix + ".tmp")

zin = zipfile.ZipFile(src)
doc = zin.read("word/document.xml").decode("utf-8")
rels = zin.read("word/_rels/document.xml.rels").decode("utf-8")

# 1. 删除 TOC 域段落内部的 hyperlink（域首条目与后续独立段落重复）
field_para_re = re.compile(r'(<w:p><w:pPr><w:pStyle w:val="TOC1"/>.*?<w:instrText[^>]*>TOC [^<]*</w:instrText>.*?<w:fldChar w:fldCharType="separate"/></w:r>)(.*?)(</w:p>)', re.S)
m = field_para_re.search(doc)
removed = False
if m and "<w:hyperlink" in m.group(2):
    doc = doc[:m.start(2)] + doc[m.end(2):]
    removed = True

# 2. 压缩 TOC 条目行距：before 120 -> 30, after 60 -> 30
def compress_toc_spacing(xml):
    def fix_para(match):
        para = match.group(0)
        if 'w:pStyle w:val="TOC1"' in para or 'w:pStyle w:val="TOC2"' in para:
            para = para.replace('<w:spacing w:before="120" w:after="60"/>',
                                '<w:spacing w:before="30" w:after="30" w:line="280" w:lineRule="atLeast"/>')
        return para
    return re.sub(r'<w:p><w:pPr><w:pStyle w:val="TOC[12]"/>.*?</w:p>', fix_para, xml, flags=re.S)

doc = compress_toc_spacing(doc)

# 3. 移除空 pgNumType
doc = doc.replace("<w:pgNumType/>", "")

# 4. 页脚域格式
ref_ids = re.findall(r'<w:footerReference[^>]*r:id="([^"]+)"', doc)
rid2target = dict(re.findall(r'<Relationship[^>]*Id="([^"]+)"[^>]*Target="([^"]+)"', rels))

footer_files = []
for rid in ref_ids:
    target = rid2target.get(rid, "")
    if "footer" in target:
        full = target if target.startswith("word/") else "word/" + target
        if full not in footer_files:
            footer_files.append(full)

patched = {}
for i, name in enumerate(footer_files):
    xml = zin.read(name).decode("utf-8")
    fmt = "ROMAN" if i == 0 else "arabic"
    xml = re.sub(
        r'(<w:instrText[^>]*>)\s*PAGE\s*(</w:instrText>)',
        lambda mm, f=fmt: mm.group(1) + " PAGE \\* " + f + " \\* MERGEFORMAT " + mm.group(2),
        xml,
    )
    patched[name] = xml

with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
    for item in zin.infolist():
        data = zin.read(item.filename)
        if item.filename == "word/document.xml":
            data = doc.encode("utf-8")
        elif item.filename in patched:
            data = patched[item.filename].encode("utf-8")
        zout.writestr(item, data)
zin.close()
shutil.move(tmp, src)
print("postfix ok | dedup:", removed, "| footers:", footer_files)
