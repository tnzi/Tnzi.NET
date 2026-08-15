---
name: PPT Generation & Processing
slug: ppt-generation
description: "Use this skill for ANY PowerPoint (.pptx) task: generating new presentations with AI-generated slide images, reading/parsing/extracting content from existing .pptx files, editing or modifying presentations, combining or splitting slides, or working with templates and layouts. Triggers on any mention of 'deck', 'slides', 'presentation', '.pptx', or requests to create, edit, or analyze PowerPoint files."
agents: "*"
---

# PPT Generation & Processing

## Quick Reference

| Task | Guide |
|------|-------|
| Generate new presentation (AI images) | [Step 1-4 below](#workflow-generate-new-presentation) |
| Read/analyze content | `python -m markitdown presentation.pptx` |
| Edit or create from template | Read [editing.md](references/editing.md) |
| Create from scratch (code) | Read [pptxgenjs.md](references/pptxgenjs.md) |

## Workflow: Generate New Presentation

### Step 1: Understand Requirements

When a user requests presentation generation, identify:

- Topic/subject: What is the presentation about
- Number of slides: How many slides are needed (default: 5-10)
- **Style**: Choose from styles below
- Aspect ratio: Standard (16:9) or classic (4:3)
- Content outline: Key points for each slide

### Presentation Styles

| Style | Description | Best For |
|-------|-------------|----------|
| **glassmorphism** | Frosted glass panels with blur effects, floating translucent cards, vibrant gradient backgrounds, depth through layering | Tech products, AI/SaaS demos, futuristic pitches |
| **dark-premium** | Rich black backgrounds (#0a0a0a), luminous accent colors, subtle glow effects, luxury brand aesthetic | Premium products, executive presentations, high-end brands |
| **gradient-modern** | Bold mesh gradients, fluid color transitions, contemporary typography, vibrant yet sophisticated | Startups, creative agencies, brand launches |
| **neo-brutalist** | Raw bold typography, high contrast, intentional "ugly" aesthetic, anti-design as design, Memphis-inspired | Edgy brands, Gen-Z targeting, disruptive startups |
| **3d-isometric** | Clean isometric illustrations, floating 3D elements, soft shadows, tech-forward aesthetic | Tech explainers, product features, SaaS presentations |
| **editorial** | Magazine-quality layouts, sophisticated typography hierarchy, dramatic photography, Vogue/Bloomberg aesthetic | Annual reports, luxury brands, thought leadership |
| **minimal-swiss** | Grid-based precision, Helvetica-inspired typography, bold use of negative space, timeless modernism | Architecture, design firms, premium consulting |
| **keynote** | Apple-inspired aesthetic with bold typography, dramatic imagery, high contrast, cinematic feel | Keynotes, product reveals, inspirational talks |

### Step 2: Create Presentation Plan

Create a JSON file with the presentation structure. **Important**: Include the `style` field to define the overall visual consistency.

```json
{
  "title": "Presentation Title",
  "style": "keynote",
  "style_guidelines": {
    "color_palette": "Deep black backgrounds, white text, single accent color (blue or orange)",
    "typography": "Bold sans-serif headlines, clean body text, dramatic size contrast",
    "imagery": "High-quality photography, full-bleed images, cinematic composition",
    "layout": "Generous whitespace, centered focus, minimal elements per slide"
  },
  "aspect_ratio": "16:9",
  "slides": [
    {
      "slide_number": 1,
      "type": "title",
      "title": "Main Title",
      "subtitle": "Subtitle or tagline",
      "visual_description": "Detailed description for image generation"
    },
    {
      "slide_number": 2,
      "type": "content",
      "title": "Slide Title",
      "key_points": ["Point 1", "Point 2", "Point 3"],
      "visual_description": "Detailed description for image generation"
    }
  ]
}
```

### Step 3: Generate Slide Images Sequentially

**IMPORTANT**: Generate slides **strictly one by one, in order**. Do NOT parallelize or batch image generation. Each slide depends on the previous slide's output as a reference image.

1. Read the image-generation skill to understand how to generate images.

2. **For the FIRST slide (slide 1)**, create a prompt that establishes the visual style:

```json
{
  "prompt": "Professional presentation slide. [style_guidelines from plan]. Title: 'Your Title'. [visual_description]. This slide establishes the visual language for the entire presentation.",
  "style": "[Based on chosen style]",
  "composition": "Clean layout with clear text hierarchy, [style-specific composition]",
  "color_palette": "[From style_guidelines]",
  "typography": "[From style_guidelines]"
}
```

```bash
python scripts/generate.py \
  --prompt-file /path/to/slide-01-prompt.json \
  --output-file /path/to/outputs/slide-01.jpg \
  --aspect-ratio 16:9
```

3. **For subsequent slides (slide 2+)**, use the PREVIOUS slide as a reference image:

```json
{
  "prompt": "Professional presentation slide continuing the visual style from the reference image. Maintain the same color palette, typography style, and overall aesthetic. Title: 'Slide Title'. [visual_description]. Keep visual consistency with the reference.",
  "style": "Match the style of the reference image exactly",
  "composition": "Similar layout principles as reference, adapted for this content",
  "color_palette": "Same as reference image",
  "consistency_note": "This slide must look like it belongs in the same presentation as the reference image"
}
```

```bash
python scripts/generate.py \
  --prompt-file /path/to/slide-02-prompt.json \
  --reference-images /path/to/outputs/slide-01.jpg \
  --output-file /path/to/outputs/slide-02.jpg \
  --aspect-ratio 16:9
```

4. **Continue for all remaining slides**, always referencing the previous slide.

### Step 4: Compose PPT

After all slide images are generated, call the composition script:

```bash
python scripts/generate.py \
  --plan-file /path/to/presentation-plan.json \
  --slide-images /path/to/outputs/slide-01.jpg /path/to/outputs/slide-02.jpg /path/to/outputs/slide-03.jpg \
  --output-file /path/to/outputs/presentation.pptx
```

> [!NOTE]
> Do NOT read the script file, just call it with the parameters.

## Workflow: Read Existing PPTX

```bash
# Text extraction
python -m markitdown presentation.pptx

# Visual overview
python scripts/thumbnail.py presentation.pptx

# Raw XML
python /mnt/skills/office/scripts/unpack.py presentation.pptx unpacked/
```

## Workflow: Edit Existing PPTX

**Read [editing.md](references/editing.md) for full details.**

1. Analyze template with `thumbnail.py`
2. Unpack → manipulate slides → edit content → clean → pack

## Workflow: Create from Code

**Read [pptxgenjs.md](references/pptxgenjs.md) for full details.**

Use PptxGenJS when no template or reference presentation is available and you need programmatic control.

## Design Guidelines

**Don't create boring slides.** Plain bullets on a white background won't impress anyone.

### Color Palettes

Choose colors that match your topic — don't default to generic blue:

| Theme | Primary | Secondary | Accent |
|-------|---------|-----------|--------|
| **Midnight Executive** | `1E2761` (navy) | `CADCFC` (ice blue) | `FFFFFF` (white) |
| **Forest & Moss** | `2C5F2D` (forest) | `97BC62` (moss) | `F5F5F5` (cream) |
| **Coral Energy** | `F96167` (coral) | `F9E795` (gold) | `2F3C7E` (navy) |
| **Warm Terracotta** | `B85042` (terracotta) | `E7E8D1` (sand) | `A7BEAE` (sage) |
| **Ocean Gradient** | `065A82` (deep blue) | `1C7293` (teal) | `21295C` (midnight) |
| **Charcoal Minimal** | `36454F` (charcoal) | `F2F2F2` (off-white) | `212121` (black) |
| **Teal Trust** | `028090` (teal) | `00A896` (seafoam) | `02C39A` (mint) |
| **Berry & Cream** | `6D2E46` (berry) | `A26769` (dusty rose) | `ECE2D0` (cream) |
| **Cherry Bold** | `990011` (cherry) | `FCF6F5` (off-white) | `2F3C7E` (navy) |

### Layout Options

**Every slide needs a visual element** — image, chart, icon, or shape. Text-only slides are forgettable.

- Two-column (text left, illustration on right)
- Icon + text rows (icon in colored circle, bold header, description below)
- 2x2 or 2x3 grid (image on one side, grid of content blocks on other)
- Half-bleed image (full left or right side) with content overlay
- Large stat callouts (big numbers 60-72pt with small labels below)
- Timeline or process flow (numbered steps, arrows)

### Typography

| Header Font | Body Font |
|-------------|-----------|
| Georgia | Calibri |
| Arial Black | Arial |
| Calibri | Calibri Light |
| Cambria | Calibri |
| Trebuchet MS | Calibri |

| Element | Size |
|---------|------|
| Slide title | 36-44pt bold |
| Section header | 20-24pt bold |
| Body text | 14-16pt |
| Captions | 10-12pt muted |

### Common Mistakes to Avoid

- **Don't repeat the same layout** — vary columns, cards, and callouts across slides
- **Don't center body text** — left-align paragraphs and lists; center only titles
- **Don't default to blue** — pick colors that reflect the specific topic
- **Don't create text-only slides** — add images, icons, charts, or visual elements
- **NEVER use accent lines under titles** — hallmark of AI-generated slides
- **Don't generate slides in parallel** — slides MUST be generated one at a time in order

## QA (Required)

**Assume there are problems. Your job is to find them.**

### Content QA

```bash
python -m markitdown output.pptx
```

Check for missing content, typos, wrong order. When using templates, check for leftover placeholder text:

```bash
python -m markitdown output.pptx | grep -iE "\bx{3,}\b|lorem|ipsum|\bTODO|\[insert"
```

### Visual QA

Convert slides to images, then inspect with fresh eyes (use subagents for objectivity):

```bash
python /mnt/skills/office/scripts/soffice.py --headless --convert-to pdf output.pptx
rm -f slide-*.jpg
pdftoppm -jpeg -r 150 output.pdf slide
ls -1 "$PWD"/slide-*.jpg
```

Look for: overlapping elements, text overflow, low contrast, uneven gaps, insufficient margins, misaligned elements.

### Verification Loop

1. Generate → Convert to images → Inspect
2. List issues found
3. Fix issues
4. Re-verify affected slides
5. Repeat until clean

## Critical Quality Guidelines

**Prompt Engineering for Professional Results:**
- Always use English for image prompts regardless of user's language
- Be EXTREMELY specific about visual details — vague prompts produce generic results
- Include exact hex color codes (e.g., #667eea not "purple")
- Specify typography details: font weight, size hierarchy, letter-spacing
- Reference real design systems: "visionOS aesthetic", "Stripe website style"

**Visual Consistency (Most Important):**
- Generate slides sequentially — each MUST reference the previous one
- The first slide establishes the visual language for the entire presentation
- Use SAME, EXACT, MATCH keywords emphatically in prompts
- If a slide looks inconsistent, regenerate with stronger reference emphasis

**Design Principles:**
- Embrace negative space — 40-60% empty space creates premium feel
- Limit elements per slide — one focal point, one message
- Use depth through layering (shadows, transparency, z-depth)
- Typography hierarchy: massive headlines (72pt+), comfortable body (18-24pt)
- Color restraint: one primary palette, 1-2 accent colors maximum

## Dependencies

- `pip install "markitdown[pptx]"` — text extraction
- `pip install Pillow` — thumbnail grids
- `npm install -g pptxgenjs` — creating from scratch
- LibreOffice (`soffice`) — PDF conversion (via `/mnt/skills/office/scripts/soffice.py`)
- Poppler (`pdftoppm`) — PDF to images

## Output Handling

After generation:
- Share the generated presentation with the user
- Also share the individual slide images if requested
- Offer to iterate or regenerate specific slides if needed
