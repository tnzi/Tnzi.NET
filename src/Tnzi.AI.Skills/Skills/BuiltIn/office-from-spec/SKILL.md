---
name: Office From Spec
slug: office-from-spec
description: "Use this skill when the user asks to create an Excel (.xlsx) or Word (.docx) file from a structured specification (table data, multiple sheets, headers, simple formatting). Generates the file via Python in the sandbox using openpyxl / python-docx and writes the artifact to the workspace. This is the lightweight successor to the deleted .NET-based Tnzi.AI.Office tools — use the more specialised xlsx-processing or docx-processing skills when the task involves complex existing-file edits (formula recalculation, comments, tracked changes)."
agents: "*"
---

# Office From Spec

Create Excel or Word artifacts from a JSON-shaped spec the agent constructs from
the user's request.

## When to use

- "Generate a spreadsheet of …"
- "Build a Word document with the following headings and bullet points…"
- "Export this table to .xlsx"

For complex edits to existing files (recalculate formulas, accept tracked changes,
manage comments, multi-sheet pivots) prefer the dedicated `xlsx-processing` /
`docx-processing` skills which include richer scripts.

## Requirements

- bins:
  - python (>= 3.11)
- envs: []
- toolGroups: [sandbox]

## Workflow

1. Parse the user request into a spec — pick **one** of the templates below.
2. Persist the spec as JSON in the sandbox workspace (`/workspace/spec.json`).
3. Run the matching python script via `bash`.
4. Return the `/workspace/<file>.xlsx` (or `.docx`) path so the host can stream it
   to storage and surface a download URL to the user.

## Excel — single sheet

```python
# Save the following as /workspace/build_xlsx.py and invoke with:
#   python /workspace/build_xlsx.py /workspace/spec.json /workspace/out.xlsx
import json, sys
from openpyxl import Workbook
from openpyxl.styles import Font

spec_path, out_path = sys.argv[1], sys.argv[2]
spec = json.load(open(spec_path))

wb = Workbook()
ws = wb.active
ws.title = spec.get("sheet", "Sheet1")

headers = spec.get("headers", [])
for col, h in enumerate(headers, start=1):
    cell = ws.cell(row=1, column=col, value=h)
    cell.font = Font(bold=True)

for r, row in enumerate(spec.get("rows", []), start=2):
    for c, val in enumerate(row, start=1):
        ws.cell(row=r, column=c, value=val)

for col_idx in range(1, len(headers) + 1):
    ws.column_dimensions[ws.cell(row=1, column=col_idx).column_letter].width = max(
        12, max((len(str(r[col_idx-1])) for r in spec.get("rows", [])), default=12)
    )

wb.save(out_path)
print(out_path)
```

Spec shape:

```json
{
  "sheet": "Sales",
  "headers": ["Region", "Q1", "Q2", "Q3", "Q4"],
  "rows": [
    ["North", 100, 120, 140, 160],
    ["South", 90, 110, 130, 150]
  ]
}
```

## Word — heading + paragraphs + table

```python
# /workspace/build_docx.py
import json, sys
from docx import Document
from docx.shared import Pt

spec_path, out_path = sys.argv[1], sys.argv[2]
spec = json.load(open(spec_path))

doc = Document()
if title := spec.get("title"):
    doc.add_heading(title, level=1)

for block in spec.get("blocks", []):
    kind = block["type"]
    if kind == "heading":
        doc.add_heading(block["text"], level=block.get("level", 2))
    elif kind == "paragraph":
        p = doc.add_paragraph(block["text"])
        for run in p.runs:
            run.font.size = Pt(11)
    elif kind == "bullet":
        for item in block["items"]:
            doc.add_paragraph(item, style="List Bullet")
    elif kind == "table":
        rows = block["rows"]
        if not rows: continue
        t = doc.add_table(rows=len(rows), cols=len(rows[0]))
        t.style = "Light Grid Accent 1"
        for r, row in enumerate(rows):
            for c, val in enumerate(row):
                t.cell(r, c).text = str(val)

doc.save(out_path)
print(out_path)
```

Spec shape:

```json
{
  "title": "Quarterly Report",
  "blocks": [
    { "type": "heading", "text": "Summary", "level": 2 },
    { "type": "paragraph", "text": "Revenue grew 12% YoY..." },
    { "type": "bullet", "items": ["Q1 best ever", "Hiring on track"] },
    { "type": "table", "rows": [["Region", "Revenue"], ["North", "$1.2M"]] }
  ]
}
```

## Notes

- The sandbox container `tnzi/sandbox:python3.12` (see `docker/sandbox/Dockerfile`)
  ships with `openpyxl`, `python-docx`, `pandas`, `reportlab`, `weasyprint` — no
  extra installs required at runtime.
- The output path is what the agent returns to the user; the host wraps it in a
  Storage upload so the response includes a downloadable URL rather than the raw
  binary blob.
