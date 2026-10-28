// @vitest-environment node
/**
 * Convention gate: the brand colour is never used directly as a text colour.
 *
 * ## Why this exists
 *
 * The accent is whatever brand colour the product (or the person, through an
 * account preference) picked, and a light one - a yellow - is unreadable as
 * text on this package's light canvas (about 1.3:1). Nothing fails when that
 * happens: the link, the active sidebar row and the check mark are simply
 * invisible in that one theme, which no test of the default teal will notice.
 *
 * So text and icons take `--tnzi-ai-accent-text` (the same hue, moved just far
 * enough to read in the current mode), and what sits ON an accent fill takes
 * `--tnzi-ai-on-accent`. Both are recomputed by `@tnzi/ui` for any colour; this
 * gate keeps the raw accent out of `color` / `caret-color`, where only those
 * two belong. Fills, borders and outlines are not text and stay on the accent.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

function sourceFiles(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(full, out);
    else if (/\.(vue|css)$/.test(entry.name)) out.push(full);
  }
  return out;
}

/** Comments removed, so a sentence that quotes the old rule does not count. */
const files = sourceFiles(join(pkgRoot, 'src')).map((path) => ({
  path: relative(pkgRoot, path).replace(/\\/g, '/'),
  text: readFileSync(path, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, ''),
}));

const stylesheet = readFileSync(join(pkgRoot, 'src/styles/index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/**
 * The two shapes the raw accent takes as a text colour:
 *
 * - a CSS declaration: `color:` / `caret-color:` (not `background-color:` or
 *   `border-color:`, whose names end in `-color`) reading `--tnzi-ai-accent` or
 *   `--tnzi-primary`, with or without a fallback;
 * - a UnoCSS atom in a template: `text-primary` (with or without a level, an
 *   opacity or a variant prefix), which compiles to `color: var(--tnzi-primary)`.
 *   The replacement is `text-ai-accent-text`.
 */
const BANNED = [
  /(?:^|[\s;{])(?:caret-)?color\s*:\s*var\(\s*--tnzi-(?:ai-accent|primary)\s*[,)]/,
  /(?:^|[\s"'`])(?:[a-z-]+:)*(?:text|caret)-primary(?:-\d+)?(?:\/\d+)?(?=[\s"'`]|$)/,
];

/** Every banned occurrence in `text`. Fresh global copies, so no `lastIndex` leaks between calls. */
function offendingIn(text: string): string[] {
  return BANNED.flatMap((pattern) =>
    Array.from(text.matchAll(new RegExp(pattern.source, 'gm')), (m) => m[0].trim()),
  );
}

function block(selector: RegExp): string {
  const start = stylesheet.search(selector);
  return start < 0 ? '' : stylesheet.slice(start, stylesheet.indexOf('}', start));
}

describe('accent as text', () => {
  it('has files to check', () => {
    expect(files.filter((f) => f.path.endsWith('.vue')).length).toBeGreaterThan(50);
  });

  // Positive and negative controls: the patterns catch the shapes they ban
  // and nothing next to them.
  it.each([
    'a { color: var(--tnzi-ai-accent); }',
    'a{caret-color:var(--tnzi-primary, #0d9488)}',
    '<i class="size-4 text-primary" />',
    '<a class="hover:text-primary-600 underline">',
  ])('catches %s', (sample) => {
    expect(offendingIn(sample)).toHaveLength(1);
  });

  it.each([
    'a { background-color: var(--tnzi-primary); }',
    'a { border-color: var(--tnzi-ai-accent); }',
    'a { color: var(--tnzi-ai-accent-text); }',
    '<i class="size-4 text-ai-accent-text" />',
    '<div class="border-primary/50 ring-primary/20">',
    '<button class="t-auth__primary">',
  ])('leaves %s alone', (sample) => {
    expect(offendingIn(sample)).toEqual([]);
  });

  it('never paints text or icons with the raw accent', () => {
    const offenders = files.flatMap(({ path, text }) => offendingIn(text).map((hit) => `${path}: ${hit}`));
    expect(offenders).toEqual([]);
  });
});

describe('accent foreground tokens in the stylesheet', () => {
  const root = block(/:root\s*\{/);
  const dark = block(/\.dark,\s*\[data-theme='dark'\]\s*\{/);

  it('finds both blocks', () => {
    expect(root).toContain('--tnzi-ai-bg');
    expect(dark).toContain('--tnzi-ai-bg');
  });

  it('derives the foreground on an accent fill from the theme, not a literal', () => {
    expect(root).toMatch(/--tnzi-ai-on-accent:\s*var\(--tnzi-on-primary\b/);
  });

  it('takes the accent text colour from the readable pair, one per mode', () => {
    expect(root).toMatch(/--tnzi-ai-accent-text:\s*var\(--tnzi-primary-text-on-light\b/);
    expect(dark).toMatch(/--tnzi-ai-accent-text:\s*var\(--tnzi-primary-text-on-dark\b/);
  });

  it('keeps status fills off the accent foreground', () => {
    expect(root).toMatch(/--tnzi-ai-on-status:\s*#ffffff/);
    const statusFillsOnAccentForeground = files
      .filter(({ text }) => /var\(--tnzi-ai-(?:danger|node-completed|node-failed)\)[\s\S]{0,80}?var\(--tnzi-ai-on-accent\)/.test(text))
      .map(({ path }) => path);
    expect(statusFillsOnAccentForeground).toEqual([]);
  });
});
