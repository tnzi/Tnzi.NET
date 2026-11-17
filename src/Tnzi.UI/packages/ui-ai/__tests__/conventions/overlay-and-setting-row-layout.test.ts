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

describe('TSettingsDialog flush', () => {
  const css = styleOf('src/components/overlay/TSettingsDialog.vue');

  it('by default the pane scrolls as a whole and the body is padded', () => {
    expect(declaration(ruleBody(css, '.t-settings__content'), 'overflow-y')).toBe('auto');
    expect(declaration(ruleBody(css, '.t-settings__body'), 'padding')).toBe('4px 24px 24px');
  });

  /* The content owns its scrolling and spacing; lead and section name stay on top. */
  it('flush: the pane does not scroll, the body fills the rest with no padding', () => {
    expect(declaration(ruleBody(css, '.t-settings--flush .t-settings__content'), 'overflow')).toBe('hidden');
    const body = ruleBody(css, '.t-settings--flush .t-settings__body');
    expect(declaration(body, 'flex')).toBe('1');
    expect(declaration(body, 'min-height')).toBe('0');
    expect(declaration(body, 'padding')).toBe('0');
    expect(declaration(body, 'display')).toBe('flex');
    expect(declaration(body, 'flex-direction')).toBe('column');
    expect(declaration(ruleBody(css, '.t-settings__heading'), 'flex')).toBe('none');
  });

  it('flush also wins on the phone sheet (declared after its padding)', () => {
    expect(css.indexOf('.t-settings--flush .t-settings__body')).toBeGreaterThan(css.indexOf('.t-settings--sheet .t-settings__body'));
  });
});

describe('TSettingRow label', () => {
  /* The `#label` slot can put an icon or a tag beside the name: a flex line with a gap. */
  it('is a wrapping flex line, 6px between lines and 8px between items', () => {
    const body = ruleBody(styleOf('src/components/layout/TSettingRow.vue'), '.t-setting-row__label');
    expect(declaration(body, 'display')).toBe('flex');
    expect(declaration(body, 'align-items')).toBe('center');
    expect(declaration(body, 'gap')).toBe('6px 8px');
  });
});

describe('TAuthField in error', () => {
  /* A field the sign-in page marks wrong shows it, and focuses with the error ring. */
  it('draws aria-invalid with the danger border and the error focus', () => {
    const css = styleOf('src/auth/TAuthField.vue');
    expect(declaration(ruleBody(css, ".t-auth-field[aria-invalid='true']"), 'border-color')).toBe('var(--tnzi-ai-danger)');
    const focused = ruleBody(css, ".t-auth-field[aria-invalid='true']:focus");
    expect(declaration(focused, 'border-color')).toBe('var(--tnzi-ai-focus-border-error)');
    expect(declaration(focused, 'box-shadow')).toBe('var(--tnzi-ai-focus-ring-error)');
  });
});

describe('overlay backdrops cover the top inset; overlay content clears it', () => {
  const INSET = /var\(--tnzi-overlay-inset-top/;
  const SHARED = readFileSync(resolve(pkgRoot, 'src/components/overlay/dialog.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

  /* Drawers draw their own backdrop: [file, backdrop selector, content selector, how the content clears the inset] */
  const DRAWERS: ReadonlyArray<[string, string, string, 'top' | 'padding-top']> = [
    ['src/components/layout/TCollapsibleSidebar.vue', '.t-collapsible-sidebar__backdrop', '.t-collapsible-sidebar--mobile', 'top'],
    ['src/embed/TSidebarChat.vue', '.t-sidebar-chat__overlay', '.t-sidebar-chat__panel', 'top'],
  ];

  it.each(DRAWERS)('%s: the backdrop spans the whole viewport', (file, backdrop) => {
    const body = ruleBody(styleOf(file), backdrop);
    expect(declaration(body, 'position')).toBe('fixed');
    expect(declaration(body, 'inset')).toBe('0');
    expect(declaration(body, 'top')).toBeUndefined();
  });

  it.each(DRAWERS)('%s: the content keeps clear of the inset', (file, _backdrop, content, how) => {
    const value = declaration(ruleBody(styleOf(file), content), how);
    expect(value).toMatch(INSET);
  });

  /* Dialogs share one backdrop, `.t-dialog-backdrop` in dialog.css. */
  it('the shared dialog backdrop spans the viewport and pads for the inset inside it', () => {
    const body = ruleBody(SHARED, '.t-dialog-backdrop');
    expect(declaration(body, 'position')).toBe('fixed');
    expect(declaration(body, 'inset')).toBe('0');
    expect(declaration(body, 'top')).toBeUndefined();
    expect(declaration(body, 'padding-top')).toMatch(INSET);
    /* Without border-box, `inset: 0` plus a top padding makes the backdrop
       taller than the viewport and pushes the centred dialog down by the inset. */
    expect(declaration(body, 'box-sizing')).toBe('border-box');
    expect(declaration(body, 'background')).toMatch(/var\(--tnzi-ai-backdrop,/);
  });

  /* [file, the overlay's own backdrop selector] */
  const DIALOGS: ReadonlyArray<[string, string]> = [
    ['src/components/overlay/TDialog.vue', '.t-dialog-host'],
    ['src/components/overlay/TCommandPalette.vue', '.t-cmdk-backdrop'],
  ];

  it.each(DIALOGS)('%s: uses the shared backdrop and does not redraw it', (file, selector) => {
    const sfc = readFileSync(resolve(pkgRoot, file), 'utf8');
    const template = /<template>([\s\S]*)<\/template>/.exec(sfc)?.[1] ?? '';
    const own = selector.slice(1);
    expect(template).toMatch(new RegExp(`class="[^"]*\\b${own}\\b[^"]*\\bt-dialog-backdrop\\b`));
    const body = ruleBody(styleOf(file), selector);
    for (const prop of ['background', 'backdrop-filter', 'position', 'inset']) {
      expect(declaration(body, prop), `${file} ${selector} ${prop}`).toBeUndefined();
    }
  });

  it.each(DIALOGS)('%s: a padding it sets still clears the inset', (file, selector) => {
    const body = ruleBody(styleOf(file), selector);
    for (const prop of ['padding', 'padding-top']) {
      const value = declaration(body, prop);
      if (value !== undefined) expect(value).toMatch(INSET);
    }
  });

  /* The image viewer draws its own dark layer (darker than a dialog's). */
  it('TImageViewer: the dark layer spans the viewport; the picture, the close and the side buttons clear the inset', () => {
    const css = styleOf('src/components/overlay/TImageViewer.vue');
    const layer = ruleBody(css, '.t-image-viewer');
    expect(declaration(layer, 'position')).toBe('fixed');
    expect(declaration(layer, 'inset')).toBe('0');
    expect(declaration(layer, 'top')).toBeUndefined();
    expect(declaration(layer, 'box-sizing')).toBe('border-box');
    expect(declaration(layer, 'padding')).toMatch(/^calc\(48px \+ var\(--tnzi-overlay-inset-top, 0px\)\) 16px 64px$/);
    expect(declaration(ruleBody(css, '.t-image-viewer__close'), 'top')).toBe('calc(12px + var(--tnzi-overlay-inset-top, 0px))');
    expect(declaration(ruleBody(css, '.t-image-viewer__prev, .t-image-viewer__next'), 'top')).toBe(
      'calc(50% + var(--tnzi-overlay-inset-top, 0px) / 2 - 8px)',
    );
  });

  /* The settings dialog has no backdrop of its own: it is a TDialog. */
  it('TSettingsDialog is drawn on TDialog', () => {
    const sfc = readFileSync(resolve(pkgRoot, 'src/components/overlay/TSettingsDialog.vue'), 'utf8');
    expect(sfc).toMatch(/<TDialog\b/);
    expect(sfc).not.toMatch(/t-dialog-backdrop|<Teleport\b/);
  });

  it('every TDialog layout keeps its content below the inset', () => {
    const css = styleOf('src/components/overlay/TDialog.vue');
    for (const selector of ['.t-dialog-host--sheet', '.t-dialog-host--fullscreen']) {
      expect(declaration(ruleBody(css, selector), 'padding')).toMatch(INSET);
    }
  });

  /* The sheet and the full screen pad the host by what of it is above the
     screen and under the keyboard (`useVisualViewportInsets`), so their
     content fits the visible part while the backdrop still covers the window. */
  it('the sheet and the full screen keep to the visual viewport', () => {
    const css = styleOf('src/components/overlay/TDialog.vue');
    for (const selector of ['.t-dialog-host--sheet', '.t-dialog-host--fullscreen']) {
      const padding = declaration(ruleBody(css, selector), 'padding') ?? '';
      expect(padding, selector).toMatch(/var\(--tnzi-ai-viewport-inset-top, 0px\)/);
      expect(padding, selector).toMatch(/var\(--tnzi-ai-viewport-inset-bottom, 0px\)\s*$/);
    }
    expect(ruleBody(css, '.t-dialog-host')).not.toMatch(/viewport-inset/);
  });
});
