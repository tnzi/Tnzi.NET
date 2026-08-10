// @vitest-environment node
/**
 * Convention gate: highlighted code follows light/dark, and the swap rule
 * exists exactly once.
 *
 * ## The bug this locks out
 *
 * Shiki's dual-theme output puts the light colours in an inline
 * `style="color:#005CC5"` and the dark ones in a `--shiki-dark` custom property
 * on the same element. Nothing reads that property by itself - a stylesheet has
 * to swap them under `.dark`.
 *
 * That rule existed, scoped with `:deep()` inside `TStreamMarkdown` only. The
 * three other components that render highlighted code (`TArtifactCodeView`,
 * `TArtifactPanel`, `TCodeBlock`) therefore painted LIGHT token colours on a
 * near-black canvas - measured `'hello'` at rgb(3,47,98) on rgb(20,20,20),
 * effectively invisible. Nothing threw, light mode was perfect, and no unit
 * test could see it: the markup and the classes were all correct.
 *
 * `TArtifactPanel` had a second, different flavour of the same bug - it went
 * through `useCodeHighlight`, which asked Shiki for a SINGLE theme pinned to
 * `github-light`, so there was no `--shiki-dark` for any rule to swap to.
 *
 * Hence two invariants: one rule, in the shared stylesheet; and every call site
 * asks for both themes.
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
    else if (/\.(vue|ts)$/.test(entry.name)) out.push(full);
  }
  return out;
}

const files = sourceFiles(join(pkgRoot, 'src')).map((path) => ({
  path: relative(pkgRoot, path).replace(/\\/g, '/'),
  text: readFileSync(path, 'utf8'),
}));

const stylesheet = readFileSync(join(pkgRoot, 'src/styles/index.css'), 'utf8');

/** The composable is the one place allowed to render a single pinned theme. */
const PIN_OPT_OUT = 'src/headless/useCodeHighlight.ts';

describe('shiki dark theme', () => {
  it('has files to check', () => {
    expect(files.length).toBeGreaterThan(100);
    expect(stylesheet.length).toBeGreaterThan(1000);
  });

  it('swaps to the dark tokens in the shared stylesheet', () => {
    // Both dark signals this package honours, and the !important that is
    // required to outrank Shiki's inline style.
    expect(stylesheet).toMatch(/\.dark \.shiki span/);
    expect(stylesheet).toMatch(/\[data-theme='dark'\] \.shiki span/);
    expect(stylesheet).toMatch(/color:\s*var\(--shiki-dark\)\s*!important/);
  });

  /**
   * Token colours only. Every component paints the code surface with
   * `--tnzi-ai-code-bg` and forces Shiki's canvas transparent, so swapping in
   * `--shiki-dark-bg` would win on specificity in some components and lose in
   * others (measured), and would out-rank the public `applyAiTheme({ codeBg })`
   * override in dark mode.
   */
  it('does not hijack the code surface background', () => {
    const rule = stylesheet.match(/\.dark \.shiki[\s\S]*?\{([\s\S]*?)\}/)?.[1] ?? '';
    expect(rule).not.toMatch(/background-color/);
    expect(rule).toMatch(/--shiki-dark\b/);
  });

  it('keeps that rule in exactly one place', () => {
    /* Only actual CSS counts - a `<style>` block in an SFC. Prose mentioning
       `var(--shiki-dark)` (this file, and the composable's own JSDoc pointing
       readers at the rule) is documentation, not a second copy. */
    const copies = files
      .filter((f) => f.path.endsWith('.vue'))
      .filter(({ text }) =>
        [...text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].some((m) =>
          /var\(--shiki-dark\)/.test(m[1]!),
        ),
      )
      .map((f) => f.path);

    expect(copies).toEqual([]);
  });

  it('asks shiki for both themes at every call site', () => {
    const singleTheme = files
      .filter((f) => f.path !== PIN_OPT_OUT)
      .flatMap(({ path, text }) => {
        // `codeToHtml(..., { lang, theme: 'x' })` - a single theme means no
        // dark counterpart is emitted at all.
        const calls = [...text.matchAll(/codeToHtml\([\s\S]{0,200}?\}\s*\)/g)];
        return calls
          .filter((m) => /\btheme:/.test(m[0]) && !/\bthemes:/.test(m[0]))
          .map(() => path);
      });

    expect(singleTheme).toEqual([]);
  });

  it('does not pin a single theme by default anywhere', () => {
    /* A component defaulting `theme` to a literal re-introduces the
       TArtifactPanel flavour of the bug: dual-theme CSS with single-theme HTML.
       Scoped to files that actually deal with Shiki - `theme: 'light'` in
       TChatApp is the app's colour scheme and has nothing to do with this. */
    const pinned = files
      .filter((f) => f.path !== PIN_OPT_OUT)
      .filter((f) => /shiki|useCodeHighlight/i.test(f.text))
      .flatMap(({ path, text }) =>
        [...text.matchAll(/^\s*theme:\s*'[a-z0-9-]+',/gm)].map((m) => `${path}: ${m[0].trim()}`),
      );

    expect(pinned).toEqual([]);
  });
});
