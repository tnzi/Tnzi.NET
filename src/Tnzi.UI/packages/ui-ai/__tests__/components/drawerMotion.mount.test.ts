/**
 * The narrow-screen drawer slides without moving the page.
 *
 * Measured before (Chromium, 390x844, 4x CPU slowdown): closing the drawer put
 * the sidebar back in the flow (`position: fixed` -> `relative` the moment its
 * `--drawer` class went) and animated its width 280 -> 0, so the main column
 * jumped to x=280 / 110px wide on the first frame and reflowed its text on each
 * of the next ~190ms. The main column can only move if the sidebar takes room
 * in the row or animates a layout property, so this pins both, in the DOM and
 * in the stylesheet: on a narrow screen the sidebar is out of the flow open or
 * closed, with no width of its own, and its slide animates only `transform`
 * (plus `visibility` and the shadow), the backdrop only `opacity`, on one
 * shared duration that reduced motion sets to zero. Measured after, same
 * setup: opening and closing, every frame (44 and 43) had the main column at
 * x=0, 390px wide, the sidebar `fixed` and moving by its transform only.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { default: TCollapsibleSidebar } = await import('../../src/components/layout/TCollapsibleSidebar.vue');

let app: App | null = null;

function mountSidebar(isMobile: boolean, initial: 'expanded' | 'hidden' | 'icon') {
  const host = document.createElement('div');
  document.body.appendChild(host);
  const mode = ref(initial);
  app = createApp(
    defineComponent({
      setup: () => () =>
        h(
          TCollapsibleSidebar,
          { modelValue: mode.value, isMobile, 'onUpdate:modelValue': (v: typeof initial) => (mode.value = v) },
          { content: () => h('button', { type: 'button', class: 'row' }, 'Row') },
        ),
    }),
  );
  app.mount(host);
  return { host, mode };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

const aside = () => document.querySelector<HTMLElement>('.t-collapsible-sidebar')!;
const backdrop = () => document.querySelector<HTMLElement>('.t-collapsible-sidebar__backdrop');

describe('narrow drawer', () => {
  it('stays out of the flow, with no width of its own, closed and open', async () => {
    const { mode } = mountSidebar(true, 'hidden');
    await settle();
    expect(aside().classList.contains('t-collapsible-sidebar--mobile')).toBe(true);
    expect(aside().style.width).toBe('');
    expect(aside().classList.contains('t-collapsible-sidebar--drawer')).toBe(false);

    mode.value = 'expanded';
    await settle();
    expect(aside().classList.contains('t-collapsible-sidebar--mobile')).toBe(true);
    expect(aside().classList.contains('t-collapsible-sidebar--drawer')).toBe(true);
    expect(aside().style.width).toBe('');

    mode.value = 'hidden';
    await settle();
    expect(aside().classList.contains('t-collapsible-sidebar--mobile')).toBe(true);
    expect(aside().style.width).toBe('');
  });

  it('is inert and hidden from assistive tech while closed, reachable while open', async () => {
    const { mode } = mountSidebar(true, 'hidden');
    await settle();
    expect(aside().hasAttribute('inert')).toBe(true);
    expect(aside().getAttribute('aria-hidden')).toBe('true');
    mode.value = 'expanded';
    await settle();
    expect(aside().hasAttribute('inert')).toBe(false);
    mode.value = 'hidden';
    await settle();
    expect(aside().hasAttribute('inert')).toBe(true);
  });

  it('keeps its backdrop mounted and switches it with the same change as the drawer', async () => {
    const { mode } = mountSidebar(true, 'hidden');
    await settle();
    expect(backdrop()).not.toBeNull();
    expect(backdrop()!.classList.contains('is-open')).toBe(false);
    mode.value = 'expanded';
    await settle();
    expect(backdrop()!.classList.contains('is-open')).toBe(true);
    backdrop()!.click();
    await settle();
    expect(mode.value).toBe('hidden');
    expect(backdrop()!.classList.contains('is-open')).toBe(false);
  });

  it('on a wide screen is the in-flow column it was: an inline width and no backdrop', async () => {
    const { mode } = mountSidebar(false, 'expanded');
    await settle();
    expect(aside().classList.contains('t-collapsible-sidebar--mobile')).toBe(false);
    expect(aside().style.width).toBe('280px');
    expect(backdrop()).toBeNull();
    mode.value = 'icon';
    await settle();
    expect(aside().style.width).toBe('56px');
  });
});

describe('drawer stylesheet', () => {
  const sfc = readFileSync(resolve(import.meta.dirname, '../../src/components/layout/TCollapsibleSidebar.vue'), 'utf8');
  const style = sfc.slice(sfc.indexOf('<style'));
  const rule = (selector: string): string => {
    const start = style.indexOf(`${selector} {`);
    expect(start, selector).toBeGreaterThanOrEqual(0);
    return style.slice(start, style.indexOf('}', start));
  };
  const transitioned = (body: string): string[] => {
    const value = /transition:([^;]+);/.exec(body)?.[1] ?? '';
    return value.split(',').map((part) => part.trim().split(/\s+/)[0]!);
  };

  it('fixes the narrow sidebar and slides it with a transform only', () => {
    const body = rule('.t-collapsible-sidebar--mobile');
    expect(body).toMatch(/position:\s*fixed/);
    expect(body).toMatch(/transform:\s*translateX\(-100%\)/);
    expect(transitioned(body).sort()).toEqual(['box-shadow', 'transform', 'visibility']);
    expect(transitioned(rule('.t-collapsible-sidebar--drawer')).sort()).toEqual(['box-shadow', 'transform', 'visibility']);
  });

  it('fades the backdrop on the same duration and curve', () => {
    expect(transitioned(rule('.t-collapsible-sidebar__backdrop')).sort()).toEqual(['opacity', 'visibility']);
    expect(rule('.t-collapsible-sidebar__backdrop')).toMatch(/var\(--t-drawer-duration\) var\(--t-drawer-easing\)/);
    expect(rule('.t-collapsible-sidebar--mobile')).toMatch(/transform var\(--t-drawer-duration\) var\(--t-drawer-easing\)/);
  });

  it('drops the slide under reduced motion', () => {
    expect(style).toMatch(/@media \(prefers-reduced-motion: reduce\)\s*{\s*\.t-collapsible-sidebar--mobile,\s*\.t-collapsible-sidebar__backdrop\s*{\s*--t-drawer-duration:\s*0ms;/);
  });
});
