/**
 * useVisualViewportInsets: while enabled, the element gets how much of it lies
 * above and below the visual viewport as CSS variables, kept up to date on the
 * viewport's resize and scroll; nothing outside the element is touched, and
 * disabling, removing the element or unmounting takes the variables away.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import {
  useVisualViewportInsets,
  visualViewportInsets,
  VIEWPORT_INSET_BOTTOM_VAR,
  VIEWPORT_INSET_TOP_VAR,
  type ViewportInsets,
} from '../../src/headless/useVisualViewportInsets';

class FakeViewport extends EventTarget {
  height = 844;
  offsetTop = 0;
}

let app: App | null = null;
let root: HTMLElement | null = null;
let viewport: FakeViewport;
const original = Object.getOwnPropertyDescriptor(window, 'visualViewport');

/** A full-window box, as a `position: fixed; inset: 0` overlay has on an 844px-high phone. */
const WINDOW_BOX = { top: 0, bottom: 844, left: 0, right: 390, width: 390, height: 844, x: 0, y: 0 };

beforeEach(() => {
  viewport = new FakeViewport();
  Object.defineProperty(window, 'visualViewport', { value: viewport, configurable: true });
});

afterEach(() => {
  app?.unmount();
  root?.remove();
  app = null;
  root = null;
  if (original) Object.defineProperty(window, 'visualViewport', original);
  else delete (window as { visualViewport?: unknown }).visualViewport;
});

function mount(initiallyEnabled: boolean, onChange?: (insets: ViewportInsets) => void) {
  const enabled = ref(initiallyEnabled);
  const shown = ref(true);
  const target = ref<HTMLElement | null>(null);
  root = document.createElement('div');
  document.body.appendChild(root);
  app = createApp(
    defineComponent({
      setup() {
        useVisualViewportInsets(target, enabled, { onChange });
        return () =>
          shown.value
            ? h('div', {
                ref: target,
                class: 'overlay',
                onVnodeMounted: (vnode: { el: HTMLElement }) => {
                  vnode.el.getBoundingClientRect = () => ({ ...WINDOW_BOX, toJSON: () => WINDOW_BOX }) as DOMRect;
                },
              })
            : null;
      },
    }),
  );
  app.mount(root);
  return { enabled, shown, el: () => root!.querySelector('.overlay') as HTMLElement | null };
}

const top = (el: HTMLElement) => el.style.getPropertyValue(VIEWPORT_INSET_TOP_VAR);
const bottom = (el: HTMLElement) => el.style.getPropertyValue(VIEWPORT_INSET_BOTTOM_VAR);

describe('visualViewportInsets', () => {
  it('is what of the box is above and below the visible part, rounded, never negative', () => {
    expect(visualViewportInsets({ top: 0, bottom: 844 }, { offsetTop: 0, height: 844 })).toEqual({ top: 0, bottom: 0 });
    /* Keyboard open, page scrolled 120px under it. */
    expect(visualViewportInsets({ top: 0, bottom: 844 }, { offsetTop: 120.4, height: 450.2 })).toEqual({ top: 120, bottom: 273 });
    /* A visual viewport larger than the box (pinch-out on a desktop) is not a negative inset. */
    expect(visualViewportInsets({ top: 10, bottom: 500 }, { offsetTop: 0, height: 800 })).toEqual({ top: 0, bottom: 0 });
  });
});

describe('useVisualViewportInsets', () => {
  it('writes zero insets with the keyboard closed', async () => {
    const { el } = mount(true);
    await nextTick();
    expect(top(el()!)).toBe('0px');
    expect(bottom(el()!)).toBe('0px');
  });

  it('follows the keyboard opening, the page scrolling under it and the keyboard closing', async () => {
    const onChange = vi.fn();
    const { el } = mount(true, onChange);
    await nextTick();
    onChange.mockClear();

    viewport.height = 508;
    viewport.dispatchEvent(new Event('resize'));
    expect(bottom(el()!)).toBe('336px');
    expect(onChange).toHaveBeenLastCalledWith({ top: 0, bottom: 336 });

    viewport.offsetTop = 100;
    viewport.dispatchEvent(new Event('scroll'));
    expect(top(el()!)).toBe('100px');
    expect(bottom(el()!)).toBe('236px');

    viewport.height = 844;
    viewport.offsetTop = 0;
    viewport.dispatchEvent(new Event('resize'));
    expect(top(el()!)).toBe('0px');
    expect(bottom(el()!)).toBe('0px');
    expect(onChange).toHaveBeenCalledTimes(3);
  });

  it('does not call onChange when an event leaves the insets as they were', async () => {
    const onChange = vi.fn();
    mount(true, onChange);
    await nextTick();
    expect(onChange).toHaveBeenCalledTimes(1);
    viewport.dispatchEvent(new Event('scroll'));
    viewport.dispatchEvent(new Event('resize'));
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('does nothing while disabled and takes the variables away when disabled', async () => {
    const { el, enabled } = mount(false);
    await nextTick();
    expect(top(el()!)).toBe('');
    enabled.value = true;
    await nextTick();
    expect(top(el()!)).toBe('0px');
    enabled.value = false;
    await nextTick();
    expect(top(el()!)).toBe('');
    viewport.height = 300;
    viewport.dispatchEvent(new Event('resize'));
    expect(bottom(el()!)).toBe('');
  });

  it('stops listening when the element goes away, and touches nothing on the document', async () => {
    const before = [document.documentElement.getAttribute('style'), document.body.getAttribute('style')];
    const remove = vi.spyOn(viewport, 'removeEventListener');
    const { shown } = mount(true);
    await nextTick();
    shown.value = false;
    await nextTick();
    expect(remove).toHaveBeenCalledWith('resize', expect.any(Function));
    expect(remove).toHaveBeenCalledWith('scroll', expect.any(Function));
    expect([document.documentElement.getAttribute('style'), document.body.getAttribute('style')]).toEqual(before);
  });

  it('writes nothing without a visual viewport', async () => {
    delete (window as { visualViewport?: unknown }).visualViewport;
    Object.defineProperty(window, 'visualViewport', { value: undefined, configurable: true });
    const { el } = mount(true);
    await nextTick();
    expect(top(el()!)).toBe('');
    expect(bottom(el()!)).toBe('');
  });
});
