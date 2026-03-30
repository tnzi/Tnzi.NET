---
name: Office
slug: office
description: Shared Python utilities for Office document processing (DOCX, XLSX, PPTX). Provides XML unpacking, packing, validation, schema definitions, and LibreOffice integration. Used as a dependency by docx-processing, xlsx-processing, and ppt-generation skills.
internal: true
---

# Office — Shared Utilities

Shared Python infrastructure for Office document processing, used by `docx-processing`, `xlsx-processing`, and `ppt-generation` skills.

## Components

### scripts/
- `unpack.py` — Unpack Office ZIP archives to raw XML
- `pack.py` — Repack modified XML back to Office format
- `validate.py` — Validate Office documents against XSD schemas
- `soffice.py` — LibreOffice headless conversion (doc→docx, etc.)

### scripts/helpers/
- `merge_runs.py` — Merge adjacent text runs
- `simplify_redlines.py` — Simplify tracked changes

### scripts/validators/
- `base.py`, `docx.py`, `pptx.py`, `redlining.py` — Format-specific validation

### scripts/schemas/
- ECMA-376 and ISO/IEC 29500 XSD schemas for Office XML validation

## Usage

Other skills reference these scripts via sandbox path:
```bash
python /mnt/skills/office/scripts/unpack.py document.docx unpacked/
python /mnt/skills/office/scripts/pack.py unpacked/ output.docx
python /mnt/skills/office/scripts/validate.py document.docx
python /mnt/skills/office/scripts/soffice.py --headless --convert-to docx legacy.doc
```
