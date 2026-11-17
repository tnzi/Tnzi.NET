// @vitest-environment node
/**
 * Convention gate: a `TDropdownMenu` placed in a constrained flex row lets its
 * trigger shrink. Read from the declared styles because happy-dom does no layout.
 *
 * The root and the trigger wrapper are `inline-flex` boxes. As flex items their
 * minimum width defaults to their content, so a dropdown given
 * `flex: 1 1 auto; min-width: 0` by a consumer still held its trigger at the
 * full width of a long label: a trigger button with `text-overflow: ellipsis`
 * never ellipsised and ran over the buttons beside it (a long branch name in a
 * toolbar). Consumers cannot reach the wrapper (scoped styles, no `:deep`), so
 * both boxes carry `min-width: 0` and `max-width: 100%` themselves. Unconstrained,
 * both still size to their content, so icon-button triggers are unchanged.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

function styleOf(rel: string): string {
  const sfc = readFileSync(resolve(pkgRoot, rel), 'utf8');
  const blocks = [...sfc.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1]);
  if (blocks.length === 0) throw new Error(`no <style> block in ${rel}`);
  return blocks.join('\n').replace(/\/\*[\s\S]*?\*\//g, '');
}

function ruleBody(css: string, selector: string): string {
  const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g), (m) => ({
    selector: m[1]!.trim().replace(/\s+/g, ' '),
    body: m[2]!,
  }));
  const found = rules.filter((r) => r.selector === selector);
  if (found.length === 0) throw new Error(`no rule with selector exactly "${selector}"`);
  return found.map((r) => r.body).join(';');
}

function declaration(body: string, property: string): string | undefined {
  const match = new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`).exec(body);
  return match?.[1]?.trim();
}

describe('TDropdownMenu in a constrained flex row', () => {
  const css = styleOf('src/components/overlay/TDropdownMenu.vue');

  it.each(['.t-dropdown-menu', '.t-dropdown-menu__trigger'])('%s can shrink below its content and stays inside its container', (selector) => {
    const body = ruleBody(css, selector);
    expect(declaration(body, 'display')).toBe('inline-flex');
    expect(declaration(body, 'min-width')).toBe('0');
    expect(declaration(body, 'max-width')).toBe('100%');
  });

  it('does not set a width or a flex of its own, so unconstrained placements size to the trigger as before', () => {
    for (const selector of ['.t-dropdown-menu', '.t-dropdown-menu__trigger']) {
      const body = ruleBody(css, selector);
      expect(declaration(body, 'width')).toBeUndefined();
      expect(declaration(body, 'flex')).toBeUndefined();
    }
  });
});
