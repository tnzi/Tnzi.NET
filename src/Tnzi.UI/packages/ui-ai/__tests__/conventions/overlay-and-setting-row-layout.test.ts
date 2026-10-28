// @vitest-environment node
/**
 * Convention gate: two layout rules that only show up in a real browser, read
 * from the declared styles because happy-dom does no layout.
 *
 * ## Stacked setting rows
 *
 * `.t-setting-row__text` has `flex: 1 1 12rem`, a WIDTH basis for the
 * side-by-side row. `--stacked` turns the row into a column, and in a column the
 * basis is the main axis: the text block became 12rem tall and left an empty
 * band (about 190px) between the description and the control in every stacked
 * row. The stacked text must size to its content.
 *
 * ## Backdrops cover the page's top inset
 *
 * `--tnzi-overlay-inset-top` names a strip at the top of the page (a frameless
 * window's title bar) that overlay CONTENT keeps clear of. The backdrops used to
 * start below it too, so the strip stayed bright while everything else dimmed,
 * which users read as a bug. A backdrop / mask now covers the whole viewport,
 * and the panel, sheet, drawer or palette takes the inset.
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

/** Body of the rule whose selector is exactly `selector` (whitespace-normalised). */
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

describe('TSettingRow stacked layout', () => {
  const css = styleOf('src/components/layout/TSettingRow.vue');

  it('the side-by-side text keeps its width basis (wraps on a narrow sheet)', () => {
    const body = ruleBody(css, '.t-setting-row');
    expect(declaration(body, 'flex-wrap')).toBe('wrap');
    expect(declaration(ruleBody(css, '.t-setting-row__text'), 'flex')).toBe('1 1 12rem');
  });

  it('a stacked row is a column whose text does not inherit the basis as a height', () => {
    expect(declaration(ruleBody(css, '.t-setting-row--stacked'), 'flex-direction')).toBe('column');
    const text = ruleBody(css, '.t-setting-row--stacked .t-setting-row__text');
    /* `none` = `0 0 auto`: sized to content on the column axis. */
    expect(declaration(text, 'flex')).toBe('none');
  });
});

describe('overlay backdrops cover the top inset; overlay content clears it', () => {
  const INSET = /var\(--tnzi-overlay-inset-top/;

  /* [file, backdrop selector, content selector, how the content clears the inset] */
  const CASES: ReadonlyArray<[string, string, string, 'top' | 'padding-top']> = [
    ['src/components/overlay/TSettingsDialog.vue', '.t-settings-backdrop', '.t-settings-backdrop', 'padding-top'],
    ['src/components/overlay/TCommandPalette.vue', '.t-cmdk-backdrop', '.t-cmdk-backdrop', 'padding-top'],
    ['src/components/layout/TCollapsibleSidebar.vue', '.t-collapsible-sidebar__backdrop', '.t-collapsible-sidebar--drawer', 'top'],
    ['src/embed/TSidebarChat.vue', '.t-sidebar-chat__overlay', '.t-sidebar-chat__panel', 'top'],
  ];

  it.each(CASES)('%s: the backdrop spans the whole viewport', (file, backdrop) => {
    const body = ruleBody(styleOf(file), backdrop);
    expect(declaration(body, 'position')).toBe('fixed');
    expect(declaration(body, 'inset')).toBe('0');
    expect(declaration(body, 'top')).toBeUndefined();
  });

  it.each(CASES)('%s: the content keeps clear of the inset', (file, _backdrop, content, how) => {
    const value = declaration(ruleBody(styleOf(file), content), how);
    expect(value).toMatch(INSET);
  });

  it('a backdrop that pads for the inset measures its padding inside the viewport', () => {
    /* Without border-box, `inset: 0` plus a top padding makes the backdrop
       taller than the viewport and pushes the centred dialog down by the inset. */
    for (const [file, backdrop] of [
      ['src/components/overlay/TSettingsDialog.vue', '.t-settings-backdrop'],
      ['src/components/overlay/TCommandPalette.vue', '.t-cmdk-backdrop'],
    ] as const) {
      expect(declaration(ruleBody(styleOf(file), backdrop), 'box-sizing')).toBe('border-box');
    }
  });
});
