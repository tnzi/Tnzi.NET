/**
 * `TDialog` opening from an element and closing back into it, the hook that
 * every close runs through, and the switch that turns the built-in motion off.
 *
 * happy-dom has no layout and no Web Animations, so `animate` is replaced by a
 * recorder whose animations finish when the test says so; the geometry is
 * checked on the pure functions.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { default: TDialog } = await import('../../src/components/overlay/TDialog.vue');
const motion = await import('../../src/components/overlay/dialog-motion');
const { chooseMotion, originTransform, resolveOrigin, rotationOf } = motion;

interface Recorded {
  el: Element;
  keyframes: Keyframe[];
  options: KeyframeAnimationOptions;
  finish: () => void;
}

let recorded: Recorded[] = [];
let autoFinish = true;
const originalAnimate = (Element.prototype as { animate?: unknown }).animate;
const originalMatchMedia = window.matchMedia;

beforeEach(() => {
  recorded = [];
  autoFinish = true;
  (Element.prototype as unknown as { animate: unknown }).animate = function (this: Element, keyframes: Keyframe[], options: KeyframeAnimationOptions) {
    let finish!: () => void;
    const finished = new Promise<void>((resolve) => (finish = resolve));
    recorded.push({ el: this, keyframes, options, finish });
    if (autoFinish) finish();
    return { finished } as unknown as Animation;
  };
});

let app: App | null = null;
afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
  (Element.prototype as unknown as { animate: unknown }).animate = originalAnimate;
  window.matchMedia = originalMatchMedia;
});

function mount(props: Record<string, unknown>, initiallyOpen = false) {
  const open = ref(initiallyOpen);
  const closed = vi.fn();
  const opened = vi.fn();
  const dialog = ref<{ close: () => Promise<boolean>; panel: HTMLElement | null } | null>(null);
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(
    defineComponent({
      setup: () => () =>
        h(TDialog, {
          ref: dialog,
          modelValue: open.value,
          'onUpdate:modelValue': (v: boolean) => (open.value = v),
          onClosed: closed,
          onOpened: opened,
          title: 'Note',
          ...props,
        }, { default: () => h('p', 'body') }),
    }),
  );
  app.mount(host);
  return { open, closed, opened, dialog };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

function card(rect: Partial<DOMRect>, size = { w: 200, h: 160 }, transform = 'none'): HTMLElement {
  const el = document.createElement('div');
  el.style.transform = transform;
  document.body.appendChild(el);
  el.getBoundingClientRect = () => ({ left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0, ...rect }) as DOMRect;
  Object.defineProperty(el, 'offsetWidth', { value: size.w, configurable: true });
  Object.defineProperty(el, 'offsetHeight', { value: size.h, configurable: true });
  return el;
}

describe('origin geometry', () => {
  it('reads the tilt out of a computed transform', () => {
    expect(rotationOf('none')).toBe(0);
    expect(rotationOf('matrix(0.866025, 0.5, -0.5, 0.866025, 0, 0)')).toBe(30);
    expect(rotationOf('matrix(0.996195, -0.0871557, 0.0871557, 0.996195, 0, 0)')).toBe(-5);
  });

  it('takes an element’s centre from its box and its size from its layout, and is null when there is nothing to grow from', () => {
    const viewport = { width: 1000, height: 800 };
    const el = card({ left: 90, top: 40, right: 310, bottom: 220, width: 220, height: 180 });
    expect(resolveOrigin(el, viewport)).toEqual({ left: 100, top: 50, width: 200, height: 160, rotate: 0 });
    expect(resolveOrigin({ element: el, rotate: -3 }, viewport)?.rotate).toBe(-3);
    expect(resolveOrigin(() => el, viewport)?.width).toBe(200);
    el.remove();
    expect(resolveOrigin(el, viewport)).toBeNull();
    const offscreen = card({ left: 0, top: 900, right: 100, bottom: 1000, width: 100, height: 100 });
    expect(resolveOrigin(offscreen, viewport)).toBeNull();
    expect(resolveOrigin(() => null, viewport)).toBeNull();
    expect(resolveOrigin({ left: 1, top: 2, width: 3, height: 4, rotate: 2 }, viewport)).toEqual({ left: 1, top: 2, width: 3, height: 4, rotate: 2 });
    expect(resolveOrigin({ left: 1, top: 2, width: 0, height: 4 }, viewport)).toBeNull();
  });

  it('draws a panel over the origin box', () => {
    const from = { left: 100, top: 50, width: 200, height: 160, rotate: -2 };
    const to = { left: 300, top: 100, width: 400, height: 320 };
    expect(originTransform(from, to)).toBe('translate(-300px, -130px) rotate(-2deg) scale(0.5, 0.5)');
  });

  it('chooses the origin, a fade with reduced motion, or the rise without an origin', () => {
    const box = { left: 0, top: 0, width: 1, height: 1 };
    expect(chooseMotion(box, false)).toEqual({ kind: 'origin', box });
    expect(chooseMotion(box, true)).toEqual({ kind: 'fade' });
    expect(chooseMotion(null, false)).toEqual({ kind: 'rise' });
  });
});

describe('beforeClose', () => {
  it('runs for Escape, the backdrop, the close button and close(), with the reason', async () => {
    const reasons: string[] = [];
    const { open, dialog } = mount({ beforeClose: (reason: string) => void reasons.push(reason) }, true);
    await settle();
    escape();
    await settle();
    expect(open.value).toBe(false);

    for (const run of [
      () => {
        const host = q('.t-dialog-host')!;
        host.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        host.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      },
      () => q<HTMLButtonElement>('.t-dialog__close')!.click(),
      () => void dialog.value!.close(),
    ]) {
      open.value = true;
      await settle();
      run();
      await settle();
      expect(open.value).toBe(false);
    }
    expect(reasons).toEqual(['escape', 'backdrop', 'close-button', 'programmatic']);
  });

  it('keeps the dialog open when it answers false, and waits for an async answer', async () => {
    let answer: (value: boolean) => void = () => {};
    const beforeClose = vi.fn(() => new Promise<boolean>((resolve) => (answer = resolve)));
    const { open } = mount({ beforeClose }, true);
    await settle();
    escape();
    escape();
    await settle();
    expect(beforeClose).toHaveBeenCalledTimes(1); // a second request joins the first
    expect(open.value).toBe(true);
    answer(false);
    await settle();
    expect(open.value).toBe(true);
    escape();
    await settle();
    answer(true);
    await settle();
    expect(open.value).toBe(false);
  });

  it('keeps the dialog open when it throws, and close() rejects with the error', async () => {
    const { open, dialog } = mount({ beforeClose: () => Promise.reject(new Error('unsaved')) }, true);
    await settle();
    await expect(dialog.value!.close()).rejects.toThrow('unsaved');
    expect(open.value).toBe(true);
  });

  it('is not asked when the parent closes the dialog; the close cannot be cancelled', async () => {
    const beforeClose = vi.fn(() => false);
    const { open, closed } = mount({ beforeClose }, true);
    await settle();
    open.value = false;
    await settle();
    await new Promise((resolve) => setTimeout(resolve, 50)); // the CSS leave ends on a timer in happy-dom
    expect(beforeClose).not.toHaveBeenCalled();
    expect(closed).toHaveBeenCalled();
  });
});

describe('opening from an origin', () => {
  it('grows out of the element and shrinks back into it before it is removed', async () => {
    const el = card({ left: 90, top: 40, right: 310, bottom: 220, width: 220, height: 180 }, { w: 200, h: 160 }, 'matrix(0.996195, -0.0871557, 0.0871557, 0.996195, 0, 0)');
    const { open, closed, opened } = mount({ origin: () => el });
    open.value = true;
    await settle();
    const panel = q('.t-dialog')!;
    const lift = recorded.find((r) => r.el === panel)!;
    expect(String(lift.keyframes[0]!.transform)).toMatch(/^translate\(.+\) rotate\(-5deg\) scale\(/);
    expect(lift.keyframes.at(-1)!.transform).toBe('none');
    expect(recorded.some((r) => r.el === q('.t-dialog-host'))).toBe(true); // the backdrop dims in step
    expect(opened).toHaveBeenCalled();

    recorded = [];
    autoFinish = false;
    open.value = false;
    await settle();
    const settleInto = recorded.find((r) => r.el === panel)!;
    expect(settleInto.keyframes[0]!.transform).toBe('none');
    expect(String(settleInto.keyframes.at(-1)!.transform)).toMatch(/rotate\(-5deg\)/);
    expect(settleInto.options.fill).toBe('forwards');
    expect(closed).not.toHaveBeenCalled(); // still animating: not removed yet
    expect(q('.t-dialog-host')).not.toBeNull();
    recorded.forEach((r) => r.finish());
    await settle();
    expect(closed).toHaveBeenCalledTimes(1);
    expect(q('.t-dialog-host')).toBeNull();
  });

  it('falls back to its own rise when the origin is gone by the time it closes', async () => {
    let el: HTMLElement | null = card({ left: 90, top: 40, right: 310, bottom: 220, width: 220, height: 180 });
    const { open } = mount({ origin: () => el });
    open.value = true;
    await settle();
    el.remove();
    el = null;
    recorded = [];
    open.value = false;
    await settle();
    const panelLeave = recorded.find((r) => r.el.classList.contains('t-dialog'))!;
    expect(panelLeave.keyframes.at(-1)!.transform).toBe('translateY(12px) scale(0.98)');
  });

  it('only fades with reduced motion', async () => {
    window.matchMedia = ((query: string) => ({ matches: query.includes('reduce'), addEventListener() {}, removeEventListener() {} })) as never;
    const el = card({ left: 90, top: 40, right: 310, bottom: 220, width: 220, height: 180 });
    const { open } = mount({ origin: el });
    open.value = true;
    await settle();
    expect(recorded).toHaveLength(1);
    expect(recorded[0]!.el.classList.contains('t-dialog-host')).toBe(true);
    expect(recorded[0]!.keyframes).toEqual([{ opacity: 0 }, { opacity: 1 }]);
  });
});

describe('transition="none"', () => {
  it('runs no built-in motion and exposes the panel, keeping the rest', async () => {
    const { open, closed, dialog } = mount({ transition: 'none' });
    open.value = true;
    await settle();
    expect(recorded).toHaveLength(0);
    const host = q('.t-dialog-host')!;
    expect(host.className).not.toMatch(/t-dialog-fade-/);
    expect(dialog.value!.panel).toBe(q('.t-dialog'));
    expect(host.classList.contains('t-dialog-backdrop')).toBe(true);
    expect(document.body.style.overflow).toBe('hidden');
    escape();
    await settle();
    expect(open.value).toBe(false);
    expect(closed).toHaveBeenCalled();
    expect(recorded).toHaveLength(0);
  });
});
