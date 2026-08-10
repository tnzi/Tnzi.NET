// @vitest-environment node
/**
 * Convention gate: no Tailwind / shadcn residue.
 *
 * ## Why this exists
 *
 * This package was built on Tailwind CSS + shadcn. The 2026-04 move to UnoCSS
 * removed the dependency but deliberately kept the shadcn *class vocabulary*
 * alive - `uno.config.ts` mapped `foreground` / `muted` / `accent` / `border`
 * onto `--tnzi-*` tokens so that no template had to be rewritten. It worked,
 * which is why it survived four months and 99 usages across 13 files.
 *
 * "It works" is the trap. A second vocabulary for tokens that already have
 * names is invisible inside this package and only fails outside it: neither
 * `@tnzi/ui` nor `@tnzi/ui-admin` carries that mapping, so markup copied out of
 * here renders **unstyled, with no error anywhere**. Nothing throws, no test
 * goes red, and the page is simply wrong - exactly the failure mode a
 * behavioural test cannot see, and exactly what a convention test is for.
 *
 * The same reasoning covers the other two leftovers cleaned up on 2026-08-03:
 * a `borderRadius` override pointing at `var(--radius)` (shadcn's variable,
 * declared nowhere in this repo, so the whole override was dead plumbing), and
 * a second `:root` block carrying shadcn's default palette in its
 * space-separated `hsl()` notation - the reason chat bubbles rendered in the
 * cool admin palette instead of this package's warm one.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

function sourceFiles(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(full, out);
    else if (/\.(vue|ts|tsx|css)$/.test(entry.name)) out.push(full);
  }
  return out;
}

const files = sourceFiles(join(pkgRoot, 'src')).map((path) => ({
  path: relative(pkgRoot, path).replace(/\\/g, '/'),
  text: readFileSync(path, 'utf8'),
}));

/**
 * shadcn's semantic colour names. Matched only where a utility prefix and a
 * word boundary make it a class rather than a CSS variable - `text-muted` also
 * appears inside `var(--tnzi-base-text-muted)`, which is the correct token and
 * must not trip this.
 */
const SHADCN_ATOMS =
  /(?:^|[\s"'`])(?:(?:hover|focus|focus-visible|active|disabled|group-hover|placeholder|dark|sm|md|lg|xl):)*(?:text|bg|border|ring|fill|stroke|divide|outline|from|via|to)-(?:foreground|muted-foreground|muted|background|card|card-foreground|popover|popover-foreground|accent|accent-foreground|destructive|destructive-foreground|primary-foreground|secondary-foreground|border|input|ring)(?:\/\d+)?(?=[\s"'`]|$)/gm;

describe('no Tailwind / shadcn residue', () => {
  it('has files to check', () => {
    // Either side reading empty would make every assertion below pass against
    // nothing - the same vacuous-green failure the token-map test guards.
    expect(files.length).toBeGreaterThan(100);
    expect(files.some((f) => f.path.endsWith('.vue'))).toBe(true);
  });

  it('uses no shadcn semantic class names', () => {
    const offenders = files.flatMap(({ path, text }) =>
      Array.from(text.matchAll(SHADCN_ATOMS), (m) => `${path}: ${m[0].trim()}`),
    );

    expect(offenders).toEqual([]);
  });

  it('declares no tailwind dependency and ships no tailwind/postcss config', () => {
    const pkg = JSON.parse(readFileSync(join(pkgRoot, 'package.json'), 'utf8'));
    const deps = Object.keys({
      ...pkg.dependencies,
      ...pkg.devDependencies,
      ...pkg.peerDependencies,
      ...pkg.optionalDependencies,
    });

    expect(deps.filter((d) => /tailwind|shadcn/i.test(d))).toEqual([]);
    for (const name of [
      'tailwind.config.js',
      'tailwind.config.ts',
      'tailwind.config.cjs',
      'postcss.config.js',
      'postcss.config.ts',
      'postcss.config.cjs',
      'components.json',
    ]) {
      expect({ [name]: existsSync(join(pkgRoot, name)) }).toEqual({ [name]: false });
    }
  });

  it('uses no Tailwind at-rules in stylesheets', () => {
    const offenders = files
      .filter((f) => f.path.endsWith('.css') || f.text.includes('<style'))
      .flatMap(({ path, text }) =>
        Array.from(text.matchAll(/@(tailwind|apply|screen|variants)\b/g), (m) => `${path}: ${m[0]}`),
      );

    expect(offenders).toEqual([]);
  });

  it('declares no colours in shadcn hsl notation and no shadcn --radius', () => {
    // shadcn writes colours as bare `hsl(H S% L%)` triplets. Every colour in
    // this package is hex / rgba / color-mix / var(), so any space-separated
    // hsl() is imported palette rather than a considered choice.
    const hsl = files.flatMap(({ path, text }) =>
      Array.from(text.matchAll(/hsl\(\s*[\d.]+\s+[\d.]+%\s+[\d.]+%/g), (m) => `${path}: ${m[0]}`),
    );
    expect(hsl).toEqual([]);

    const config = readFileSync(join(pkgRoot, 'uno.config.ts'), 'utf8');
    expect(config).not.toMatch(/var\(--radius/);
  });
});
