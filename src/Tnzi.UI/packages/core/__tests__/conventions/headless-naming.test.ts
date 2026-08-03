// @vitest-environment node
/**
 * Convention gate: **one name per concept across all five frontend packages**.
 *
 * This exists because the ecosystem spent a long time with the same concept
 * living under two names, and nothing ever failed because of it:
 *
 *   - no-render logic was `headless/` in `ui-admin`, `composables/` in `ui` and
 *     `ui-ai`, and **both** in `core` and `mobile` (split by "is it a class?",
 *     a distinction that answers nothing a consumer ever asks)
 *   - message catalogues were `locales/` in three packages and `locale/` in one
 *   - the theme layer was `theme/` in three and `themes/` in one
 *
 * All of that was collapsed on 2026-08-02. A behavioural test cannot notice a
 * second name growing back - the code works fine under either - so the only
 * thing that can hold the line is a test that reads the directory layout.
 *
 * Scope note: this deliberately checks **first-level** directories only. Nested
 * folders are dev-time categorisation and carry no contract (`headless/auth/`,
 * `components/display/` and friends are free to be named whatever reads best).
 */
import { describe, it, expect } from 'vitest';
import { readdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const packagesDir = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');

/** Directory names that must never appear again, mapped to what to use instead. */
const BANNED: Record<string, string> = {
  composables: 'headless',
  locale: 'locales',
  themes: 'theme',
  hooks: 'headless',
  lib: 'utils',
};

const PACKAGES = ['core', 'ui', 'ui-ai', 'ui-admin', 'mobile'];

function firstLevelDirs(pkg: string): string[] {
  const src = resolve(packagesDir, pkg, 'src');
  if (!existsSync(src)) return [];
  return readdirSync(src, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => e.name);
}

describe('frontend package directory naming', () => {
  // Guards the guard: if the layout lookup silently found nothing, every
  // assertion below would pass vacuously and this file would be decoration.
  it('can see the sibling packages', () => {
    const found = PACKAGES.filter((p) => firstLevelDirs(p).length > 0);
    expect(found).toEqual(PACKAGES);
  });

  it.each(PACKAGES)('%s uses no superseded first-level directory name', (pkg) => {
    const offenders = firstLevelDirs(pkg)
      .filter((name) => name in BANNED)
      .map((name) => `packages/${pkg}/src/${name}/ -> rename to ${BANNED[name]}/`);

    expect(
      offenders,
      offenders.length
        ? `Superseded directory names found:\n  ${offenders.join('\n  ')}\n` +
            'One concept, one name, in every package - see docs/coding-standards/ui-frontend.md.'
        : '',
    ).toEqual([]);
  });

  it('every package that has no-render logic calls it headless', () => {
    // Not every package must have one; but any package that has such a layer
    // must name it `headless` - which is what the BANNED check above enforces.
    // This asserts the positive side so the convention is visible, not merely
    // prohibited.
    const withHeadless = PACKAGES.filter((p) => firstLevelDirs(p).includes('headless'));
    expect(withHeadless).toEqual(PACKAGES);
  });
});
