// @vitest-environment node
/**
 * Convention gate: one focus style for form controls.
 *
 * A field the package draws itself focuses with the focus tokens
 * (`--tnzi-ai-focus-border`, `--tnzi-ai-focus-ring`). Before, each field picked
 * its own: the raw accent as border (a light brand colour on white is about
 * 1.3:1, so the focused field looked unfocused), a border-strong grey, or a
 * ring of its own width.
 *
 * So: a `:focus` / `:focus-within` rule that paints a border colour or a box
 * shadow takes them from `--tnzi-ai-focus-*`, and the tokens derive from the
 * accent (border: the accent as text, ring: the soft accent).
 */
import { describe, expect, it } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const srcRoot = join(pkgRoot, 'src');

function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? walk(join(dir, entry.name)) : /\.(vue|css)$/.test(entry.name) ? [join(dir, entry.name)] : [],
  );
}

function styles(path: string): string {
  const text = readFileSync(path, 'utf8');
  const css = path.endsWith('.vue') ? [...text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1]).join('\n') : text;
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

const rules = walk(srcRoot).flatMap((path) =>
  [...styles(path).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({
    file: relative(pkgRoot, path).replace(/\\/g, '/'),
    selector: m[1]!.trim().replace(/\s+/g, ' '),
    body: m[2]!,
  })),
);

/* `:focus` or `:focus-within`, not `:focus-visible` (keyboard rings on buttons). */
const FOCUS = /:focus(?:-within)?(?![-\w])/;
const decl = (body: string, prop: string) => new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`).exec(body)?.[1]?.trim();

describe('one focus style for form controls', () => {
  const focusRules = rules.filter((r) => FOCUS.test(r.selector));

  it('finds the focus rules it checks', () => {
    expect(focusRules.length).toBeGreaterThanOrEqual(10);
  });

  it('a focus rule that paints a border or a shadow takes the focus tokens', () => {
    const off = focusRules.flatMap((r) => {
      const problems: string[] = [];
      const border = decl(r.body, 'border-color');
      const shadow = decl(r.body, 'box-shadow');
      if (border && !/^var\(--tnzi-ai-focus-border(?:-error|-warning)?\)$/.test(border)) problems.push(`border-color: ${border}`);
      if (shadow && shadow !== 'none' && !/^var\(--tnzi-ai-focus-ring(?:-error|-warning)?\)$/.test(shadow)) problems.push(`box-shadow: ${shadow}`);
      return problems.map((p) => `${r.file} ${r.selector} -> ${p}`);
    });
    expect(off).toEqual([]);
  });

  it('a focus rule that draws a border also draws the ring', () => {
    const half = focusRules
      .filter((r) => decl(r.body, 'border-color') && !decl(r.body, 'box-shadow'))
      .map((r) => `${r.file} ${r.selector}`);
    expect(half).toEqual([]);
  });

  it('derives the tokens from the accent', () => {
    const sheet = readFileSync(join(srcRoot, 'styles/tokens.css'), 'utf8');
    const value = (name: string) => new RegExp(`--tnzi-ai-${name}:\\s*([^;]+);`).exec(sheet)?.[1]?.trim();
    expect(value('focus-border')).toBe('var(--tnzi-ai-accent-text)');
    expect(value('focus-ring-color')).toBe('var(--tnzi-ai-accent-soft)');
    expect(value('focus-ring')).toBe('0 0 0 var(--tnzi-ai-focus-ring-width) var(--tnzi-ai-focus-ring-color)');
    expect(value('focus-border-error')).toBe('var(--tnzi-ai-danger)');
  });
});

/*
 * Keyboard focus of buttons, rows, tabs and options: one line, `--tnzi-ai-focus-outline`
 * (the accent as text). It used to be the raw accent in 17 places, which a light brand
 * colour (a yellow) makes invisible on a light surface, about 1.3:1.
 */
describe('one keyboard focus line', () => {
  const ANY_FOCUS = /:focus(?:-visible|-within)?(?![-\w])/;
  const outlined = rules.filter((r) => ANY_FOCUS.test(r.selector) && decl(r.body, 'outline'));

  it('finds the outlines it checks', () => {
    expect(outlined.length).toBeGreaterThanOrEqual(15);
  });

  it('an outline drawn on focus is the focus-outline token, or none', () => {
    const off = outlined
      .map((r) => ({ r, value: decl(r.body, 'outline')! }))
      .filter(({ value }) => !['none', '0', 'var(--tnzi-ai-focus-outline)'].includes(value))
      .map(({ r, value }) => `${r.file} ${r.selector} -> outline: ${value}`);
    expect(off).toEqual([]);
  });

  it('the token is the accent as text', () => {
    const sheet = readFileSync(join(srcRoot, 'styles/tokens.css'), 'utf8');
    expect(/--tnzi-ai-focus-outline:\s*([^;]+);/.exec(sheet)?.[1]?.trim()).toBe('2px solid var(--tnzi-ai-accent-text)');
  });
});
