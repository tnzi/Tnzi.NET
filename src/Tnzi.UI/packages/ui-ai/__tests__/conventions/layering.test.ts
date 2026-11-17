// @vitest-environment node
/**
 * Convention gate: one layering scale.
 *
 * Every surface of the package takes its z-index from the scale in
 * `styles/tokens.css` (`--tnzi-ai-z-sticky` < `-drawer` < `-dialog` <
 * `-popover` < `-toast` < `-tooltip`). Before the scale each overlay picked a
 * number of its own: the action menu said 70, the settings dialog 90, the
 * general dialog 1000 and up, so a menu opened from inside a dialog was drawn
 * underneath it, and nothing failed - the menu was simply not visible.
 *
 * So: no z-index of 10 or more is written as a literal (a value below 10
 * orders siblings inside one component and is fine), no `z-NN` utility class,
 * the scale is declared in that order, and the menus sit on the popover layer.
 */
import { describe, expect, it } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const srcRoot = join(pkgRoot, 'src');

function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? walk(join(dir, entry.name)) : /\.(vue|css|ts)$/.test(entry.name) ? [join(dir, entry.name)] : [],
  );
}

const files = walk(srcRoot).map((path) => ({
  path: relative(pkgRoot, path).replace(/\\/g, '/'),
  text: readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/<!--[\s\S]*?-->/g, ''),
}));

const LITERAL_Z = /z-index\s*:\s*(\d+)/g;
const UTILITY_Z = /class="[^"]*(?<![\w-])z-(\d+)\b/g;

describe('the layering scale', () => {
  it('reads the sources it checks', () => {
    expect(files.length).toBeGreaterThan(100);
    expect(files.some((f) => f.text.includes('z-index'))).toBe(true);
  });

  it('writes no z-index of 10 or more as a literal', () => {
    const found = files.flatMap((f) =>
      [...f.text.matchAll(LITERAL_Z)].filter((m) => Number(m[1]) >= 10).map((m) => `${f.path}: ${m[0]}`),
    );
    expect(found).toEqual([]);
  });

  it('uses no z-NN utility class', () => {
    const found = files.flatMap((f) => [...f.text.matchAll(UTILITY_Z)].map((m) => `${f.path}: z-${m[1]}`));
    expect(found).toEqual([]);
  });

  it('declares the scale in order', () => {
    const sheet = readFileSync(join(srcRoot, 'styles/tokens.css'), 'utf8');
    const value = (name: string) => Number(new RegExp(`--tnzi-ai-z-${name}:\\s*(\\d+)`).exec(sheet)?.[1]);
    const order = ['sticky', 'drawer', 'dialog', 'popover', 'toast', 'tooltip'].map(value);
    expect(order.every((n) => Number.isFinite(n))).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
  });

  it('puts menus on the popover layer and modal surfaces on the dialog layer', () => {
    const text = (path: string) => files.find((f) => f.path === path)!.text;
    for (const menu of ['src/components/overlay/TActionMenu.vue', 'src/components/overlay/TPopoverMenu.vue']) {
      expect(text(menu), menu).toMatch(/z-index:\s*var\(--tnzi-ai-z-popover\b/);
    }
    for (const dialog of ['src/components/overlay/TDialog.vue', 'src/components/overlay/TCommandPalette.vue']) {
      expect(text(dialog), dialog).toMatch(/useDialogLayer\(/);
    }
    // The settings dialog and the confirmation are TDialogs, so they take its layer.
    for (const dialog of ['src/components/overlay/TSettingsDialog.vue', 'src/components/overlay/TConfirmHost.vue']) {
      expect(text(dialog), dialog).toMatch(/<TDialog\b/);
    }
  });
});
