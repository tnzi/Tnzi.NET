// @vitest-environment node
/**
 * Convention gate: the Recents page's layout, read from its declared styles
 * because happy-dom does no layout.
 *
 * - A row is one line: the title and its time sit side by side (the title
 *   ellipsises, the time never shrinks). Two lines per row halved what fits on
 *   a screen.
 * - The search field lives in the tabs row at up to 384px x 32px and can give
 *   way on a narrow screen (`min-width: 0`), so the row never wraps into a
 *   second one.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sfc = readFileSync(resolve(pkgRoot, 'src/components/chat/TRecentsPage.vue'), 'utf8');
const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');

function rule(selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  if (!match) throw new Error(`no rule "${selector}"`);
  return match[1]!;
}
const decl = (body: string, prop: string) => new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`).exec(body)?.[1]?.trim();

describe('Recents page layout', () => {
  it('rows are one line: title flexes and ellipsises, the time keeps its width', () => {
    const open = rule('.t-recents-page__open');
    expect(decl(open, 'display')).toBe('flex');
    expect(decl(open, 'flex-direction')).toBeUndefined();
    const title = rule('.t-recents-page__title');
    expect(decl(title, 'flex')).toBe('1');
    expect(decl(title, 'text-overflow')).toBe('ellipsis');
    expect(decl(rule('.t-recents-page__time'), 'flex-shrink')).toBe('0');
    expect(decl(rule('.t-recents-page__row'), 'min-height')).toBe('53px');
  });

  it('the search field is a 384 x 32 box in the tabs row that can give way', () => {
    const field = rule('.t-recents-page__search');
    expect(decl(field, 'flex')).toBe('0 1 384px');
    expect(decl(field, 'min-width')).toBe('0');
    expect(decl(field, 'height')).toBe('32px');
    expect(decl(field, 'border-radius')).toBe('8px');
    expect(decl(field, 'padding')).toBe('0 5px 0 12px');
  });
});
