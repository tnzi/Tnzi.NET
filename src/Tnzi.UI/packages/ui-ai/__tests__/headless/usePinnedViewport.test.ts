/**
 * usePinnedViewport: while enabled, the element gets the visual viewport's
 * height and top offset as CSS variables (kept up to date), and the page does
 * not overscroll; disabling or unmounting puts everything back.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { usePinnedViewport, VIEWPORT_HEIGHT_VAR, VIEWPORT_TOP_VAR } from '../../src/headless/usePinnedViewport';

class FakeViewport extends EventTarget {
  height = 844;
  offsetTop = 0;
}

let app: App | null = null;
let root: HTMLElement | null = null;
let viewport: FakeViewport;
const original = Object.getOwnPropertyDescriptor(window, 'visualViewport');

beforeEach(() => {
  viewport = new FakeViewport();
  Object.defineProperty(window, 'visualViewport', { value: viewport, configurable: true });
  document.documentElement.style.removeProperty('overscroll-behavior');
});

afterEach(() => {
  app?.unmount();
  root?.remove();
  app = null;
  root = null;
  if (original) Object.defineProperty(window, 'visualViewport', original);
  else delete (window as { visualViewport?: unknown }).visualViewport;
});

function mount(initiallyEnabled: boolean) {
  const enabled = ref(initiallyEnabled);
  const target = ref<HTMLElement | null>(null);
  root = document.createElement('div');
  document.body.appendChild(root);
  app = createApp(
    defineComponent({
      setup() {
        usePinnedViewport(target, enabled);
        return () => h('div', { ref: target, class: 'shell' });
      },
    }),
  );
  app.mount(root);
  return { enabled, el: root.querySelector('.shell') as HTMLElement };
}

describe('usePinnedViewport', () => {
  it('writes the visual viewport to the element and turns page overscroll off', async () => {
    const { el } = mount(true);
    await nextTick();
    expect(el.style.getPropertyValue(VIEWPORT_HEIGHT_VAR)).toBe('844px');
    expect(el.style.getPropertyValue(VIEWPORT_TOP_VAR)).toBe('0px');
    expect(document.documentElement.style.getPropertyValue('overscroll-behavior')).toBe('none');
  });

  it('follows the keyboard: a smaller, offset viewport is written again', async () => {
    const { el } = mount(true);
    await nextTick();
    viewport.height = 500.4;
    viewport.offsetTop = 120;
    viewport.dispatchEvent(new Event('resize'));
    expect(el.style.getPropertyValue(VIEWPORT_HEIGHT_VAR)).toBe('500px');
    viewport.offsetTop = 140;
    viewport.dispatchEvent(new Event('scroll'));
    expect(el.style.getPropertyValue(VIEWPORT_TOP_VAR)).toBe('140px');
  });

  it('disabling removes the variables and restores the page', async () => {
    document.documentElement.style.setProperty('overscroll-behavior', 'contain');
    const { el, enabled } = mount(true);
    await nextTick();
    enabled.value = false;
    await nextTick();
    expect(el.style.getPropertyValue(VIEWPORT_HEIGHT_VAR)).toBe('');
    expect(document.documentElement.style.getPropertyValue('overscroll-behavior')).toBe('contain');
    viewport.height = 300;
    viewport.dispatchEvent(new Event('resize'));
    expect(el.style.getPropertyValue(VIEWPORT_HEIGHT_VAR)).toBe('');
  });

  it('does nothing while disabled, and cleans up on unmount', async () => {
    const { el } = mount(false);
    await nextTick();
    expect(el.style.getPropertyValue(VIEWPORT_HEIGHT_VAR)).toBe('');
    expect(document.documentElement.style.getPropertyValue('overscroll-behavior')).toBe('');
    app!.unmount();
    app = null;
    expect(document.documentElement.style.getPropertyValue('overscroll-behavior')).toBe('');
  });
});
