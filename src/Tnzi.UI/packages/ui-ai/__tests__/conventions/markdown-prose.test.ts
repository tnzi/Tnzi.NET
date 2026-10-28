/**
 * Convention gate: the markdown in a message follows one reference style
 * (proportioned after claude.ai's message rendering, measured at a 15px body).
 *
 * Read from `TStreamMarkdown`'s declared styles (happy-dom does no layout),
 * plus the renderer's output:
 *
 * - colours come from `--tnzi-ai-*` tokens; the hairline (LINE) and the faint
 *   tint (TINT) are the text token at 10% / 5% alpha, so dark follows. The
 *   admin palette's `--tnzi-base-*` / `--tnzi-border` made headings, quotes and
 *   rules a cooler grey than the text around them;
 * - a table's header is the one tinted row (TINT, weight 500) and the body sits
 *   on the message background with hairlines between rows; the outline is the
 *   table's own rounded border, and a wide table scrolls in its wrapper;
 * - a fenced code block has no header bar: a small language label inside the
 *   frame and an icon-only, sticky copy button;
 * - headings are close to the body size and get smaller level by level; list
 *   markers are set here, because the package's reset removes them;
 * - blocks are spaced by their top margin: paragraphs 0.5em, lists 0.75em,
 *   quote / rule / table / code 1.5em;
 * - task list items render as disabled checkboxes.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const { useStreamMarkdown } = await import('../../src/headless/useStreamMarkdown');

const sfc = readFileSync(resolve(import.meta.dirname, '../../src/components/streaming/TStreamMarkdown.vue'), 'utf8');
const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');

/** The body of the last rule whose selector list contains exactly `selector`. */
function rule(selector: string): string {
  const bodies = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
    .filter((m) => m[1]!.split(',').map((s) => s.trim()).includes(selector))
    .map((m) => m[2]!);
  if (bodies.length === 0) throw new Error(`no rule "${selector}"`);
  return bodies.join(';');
}
/** The last value declared for `prop` across every rule naming `selector`. */
const decl = (selector: string, prop: string) =>
  [...rule(selector).matchAll(new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`, 'g'))].at(-1)?.[1]?.trim();
const deep = (inner: string) => `.t-stream-markdown :deep(${inner})`;

function render(markdown: string): string {
  const { html, append, finish } = useStreamMarkdown();
  append(markdown);
  finish();
  return html.value;
}

describe('markdown palette', () => {
  it('uses only the chat palette, LINE and TINT derived from the text token', () => {
    expect(css).not.toMatch(/var\(--tnzi-base-/);
    expect(css).not.toMatch(/var\(--tnzi-border\b/);
    expect(decl('.t-stream-markdown', '--t-md-line')).toBe('color-mix(in srgb, var(--tnzi-ai-text) 10%, transparent)');
    expect(decl('.t-stream-markdown', '--t-md-tint')).toBe('color-mix(in srgb, var(--tnzi-ai-text) 5%, transparent)');
  });
});

describe('tables', () => {
  it('tints the header row only', () => {
    expect(decl(deep('th'), 'background-color')).toBe('var(--t-md-tint)');
    expect(decl(deep('th'), 'font-weight')).toBe('500');
    expect(css).not.toMatch(/:deep\((?:td|tbody[^)]*)\)\s*\{[^}]*background/);
  });

  it('outlines the table with a rounded hairline and divides rows with hairlines', () => {
    expect(decl(deep('table'), 'border-collapse')).toBe('separate');
    expect(decl(deep('table'), 'border')).toBe('1px solid var(--t-md-line)');
    expect(decl(deep('table'), 'border-radius')).toBe('7px');
    expect(decl(deep('tbody tr + tr td'), 'border-top')).toBe('1px solid var(--t-md-line)');
  });

  it('scrolls a wide table inside its wrapper', () => {
    expect(decl(deep('.t-md-table'), 'overflow-x')).toBe('auto');
    expect(render(['| a | b |', '|---|--:|', '| 1 | 2 |'].join('\n'))).toMatch(/^<div class="t-md-table"><table>[\s\S]*<\/table>\s*<\/div>/);
  });
});

describe('code', () => {
  it('inline code: muted red on TINT with a hairline box', () => {
    expect(decl(deep('code'), 'color')).toBe('var(--tnzi-ai-code-inline-text)');
    expect(decl(deep('code'), 'background-color')).toBe('var(--t-md-tint)');
    expect(decl(deep('code'), 'border')).toBe('1px solid var(--t-md-line)');
  });

  it('a fenced block: frame, language label inside, icon-only sticky copy button, no header bar', () => {
    expect(decl(deep('.t-md-code'), 'border')).toBe('1px solid var(--t-md-line)');
    expect(decl(deep('.t-md-code'), 'border-radius')).toBe('12px');
    expect(decl(deep('.t-md-code'), 'background-color')).toBe('var(--tnzi-ai-code-frame-bg)');
    // The frame must not clip, or the sticky copy row would not stick.
    expect(decl(deep('.t-md-code'), 'overflow')).toBeUndefined();
    expect(decl(deep('.t-md-code__bar'), 'position')).toBe('sticky');
    expect(decl(deep('.t-md-code__bar'), 'border-bottom')).toBeUndefined();

    const html = render(['```python', 'print(1)', '```'].join('\n'));
    const button = /<button[^>]*class="t-md-code__copy"[^>]*>([\s\S]*?)<\/button>/.exec(html);
    expect(button).not.toBeNull();
    expect(button![1]).not.toMatch(/>\s*[A-Za-z]/); // icons only, no text label
    expect(html).toContain('aria-label="Copy code"');
    expect(html).toContain('<div class="t-md-code__lang">python</div>');
  });

  it('a block without a language has no label', () => {
    expect(render(['```', 'plain', '```'].join('\n'))).not.toContain('t-md-code__lang');
  });
});

describe('headings, lists and rhythm', () => {
  it('headings are semi-bold and shrink level by level, close to the body size', () => {
    expect(decl(deep('h1'), 'font-weight')).toBe('580');
    const sizes = ['h1', 'h2', 'h3', 'h4'].map((h) => Number.parseFloat(decl(deep(h), 'font-size') ?? '0'));
    expect(sizes).toEqual([1.373, 1.127, 1, 0.873]);
  });

  it('restores list markers, level by level', () => {
    expect(decl(deep('ul'), 'list-style')).toBe('disc');
    expect(decl(deep('ul ul'), 'list-style')).toBe('circle');
    expect(decl(deep('ul ul ul'), 'list-style')).toBe('square');
    expect(decl(deep('ol'), 'list-style')).toBe('decimal');
    expect(decl(deep('ol ol'), 'list-style')).toBe('lower-alpha');
  });

  it('spaces blocks by their top margin: 0.5em / 0.75em / 1.5em', () => {
    expect(decl(deep('p'), 'margin-top')).toBe('0.5em');
    expect(decl(deep('ul'), 'margin-top')).toBe('0.75em');
    for (const block of ['blockquote', 'hr', '.t-md-table', '.t-md-code']) {
      expect(decl(deep(block), 'margin-top')).toBe('1.5em');
    }
  });
});

describe('task lists', () => {
  const html = render(['- [x] done', '- [ ] todo', '- plain'].join('\n'));

  it('renders [ ] / [x] items as disabled checkboxes in a marker-less list', () => {
    expect(html).toContain('<ul class="t-md-tasks">');
    expect(html).toMatch(/<li class="t-md-task is-checked"><input type="checkbox" class="t-md-task__box" disabled checked>done<\/li>/);
    expect(html).toMatch(/<li class="t-md-task"><input type="checkbox" class="t-md-task__box" disabled>todo<\/li>/);
    expect(html).toContain('<li>plain</li>');
  });

  it('strikes a done item through in the secondary colour', () => {
    expect(decl(deep('.t-md-task.is-checked'), 'text-decoration')).toBe('line-through');
    expect(decl(deep('.t-md-task.is-checked'), 'color')).toBe('var(--tnzi-ai-text-secondary)');
  });

  it('leaves brackets elsewhere alone', () => {
    expect(render('A [ ] in a sentence.')).not.toContain('checkbox');
    expect(render('- [link](https://example.com)')).not.toContain('checkbox');
  });
});

/*
 * One line height for multi-line code everywhere ui-ai shows it (1.5: claude.ai's
 * 1.625 read too loose), so code never reads looser in one place than another.
 * Each surface reads the token rather than a literal of its own.
 */
describe('code line height', () => {
  const read = (rel: string) => readFileSync(resolve(import.meta.dirname, '../../src', rel), 'utf8');

  it('is one token, 1.5', () => {
    expect(read('styles/index.css')).toMatch(/--tnzi-ai-code-line-height:\s*1\.5;/);
  });

  it('every multi-line code surface reads it', () => {
    const surfaces: Record<string, number> = {
      'components/streaming/TStreamMarkdown.vue': 2, // indented block, fenced block body
      'components/streaming/TCodeBlock.vue': 2, // highlighted, fallback
      'components/reasoning/TToolOutput.vue': 1,
      'components/cli/TCliRunTimeline.vue': 1,
      'components/artifact/TArtifactPanel.vue': 1,
      'components/artifact/TArtifactCodeView.vue': 1,
      'components/artifact/TArtifactPreview.vue': 1,
    };
    for (const [file, count] of Object.entries(surfaces)) {
      const uses = read(file).match(/--tnzi-ai-code-line-height/g)?.length ?? 0;
      expect(uses, file).toBe(count);
    }
    expect(css).not.toMatch(/line-height:\s*1\.625/);
  });
});
