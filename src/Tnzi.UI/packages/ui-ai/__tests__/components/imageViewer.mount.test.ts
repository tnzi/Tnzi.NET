/**
 * `TImageViewer` on its own, imported from `@tnzi/ui-ai/dialogs` as a page of controls imports it:
 * a modal dialog that holds focus (Tab and Shift+Tab stay among its buttons), steps through its
 * set with the arrow keys, zooms (buttons, keys, wheel, double click) and pans a zoomed picture
 * (drag, arrow keys), locks the page's scroll while open, and closes by `update:index` = null.
 * Opened from a `TDialog`, it is drawn above it and one Escape closes the viewer alone.
 *
 * happy-dom does no layout: the window and the fitted picture are given sizes here (a 1000 x 800
 * area, a 400 x 300 picture) so the pan bounds have something to hold to.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { TDialog, TImageViewer } from '../../src/dialogs';
import { AI_I18N_KEY } from '../../src/i18n/key';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

let app: App | null = null;
let host: HTMLElement | null = null;
const restore: Array<() => void> = [];

function stub<T extends object>(target: T, key: PropertyKey, descriptor: PropertyDescriptor): void {
  const previous = Object.getOwnPropertyDescriptor(target, key);
  Object.defineProperty(target, key, { configurable: true, ...descriptor });
  restore.push(() => (previous ? Object.defineProperty(target, key, previous) : delete (target as Record<PropertyKey, unknown>)[key]));
}

beforeEach(() => {
  const area = { left: 0, top: 0, right: 1000, bottom: 800, width: 1000, height: 800, x: 0, y: 0, toJSON: () => ({}) };
  const original = HTMLElement.prototype.getBoundingClientRect;
  stub(HTMLElement.prototype, 'getBoundingClientRect', {
    value(this: HTMLElement) {
      return this.hasAttribute('data-image-viewer') ? area : original.call(this);
    },
  });
  stub(HTMLImageElement.prototype, 'offsetWidth', { get: () => 400 });
  stub(HTMLImageElement.prototype, 'offsetHeight', { get: () => 300 });
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  document.body.removeAttribute('style');
  while (restore.length) restore.pop()!();
});

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const IMAGES = [
  { src: 'blob:a', alt: 'a.png' },
  { src: 'blob:b', alt: 'b.png' },
  { src: 'blob:c', alt: 'c.png' },
];

function mount(start: number | null = 0, catalogue?: unknown) {
  const index = ref<number | null>(start);
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(
    defineComponent({
      setup: () => () =>
        h(TImageViewer, {
          images: IMAGES,
          index: index.value,
          'onUpdate:index': (next: number | null) => (index.value = next),
        }),
    }),
  );
  if (catalogue) app.provide(AI_I18N_KEY, ref(catalogue) as never);
  app.mount(host);
  return index;
}

const viewer = () => document.querySelector<HTMLElement>('[data-image-viewer]');
const picture = () => viewer()!.querySelector<HTMLImageElement>('[data-image-viewer-image]')!;
const button = (name: string) => viewer()!.querySelector<HTMLButtonElement>(`[data-image-viewer-${name}]`)!;
const level = () => button('fit').textContent!.trim();
const key = (k: string) => {
  const event = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
  viewer()!.dispatchEvent(event);
  return event;
};
const tab = (shiftKey = false) => {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
  (document.activeElement ?? document.body).dispatchEvent(event);
  return event;
};
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

/** The picture's transform as numbers. */
function transform(): { x: number; y: number; scale: number } {
  const match = /translate\((\S+)px, (\S+)px\) scale\(([^)]+)\)/.exec(picture().style.transform);
  if (!match) throw new Error(`no transform in "${picture().style.transform}"`);
  return { x: Number(match[1]), y: Number(match[2]), scale: Number(match[3]) };
}

function pointer(type: string, x: number, y: number, target: Element = picture()): void {
  target.dispatchEvent(new PointerEvent(type, { pointerId: 1, button: 0, clientX: x, clientY: y, bubbles: true, cancelable: true }));
}

describe('TImageViewer', () => {
  it('is closed without an index, and open on one', async () => {
    const index = mount(null);
    await settle();
    expect(viewer()).toBeNull();
    index.value = 2;
    await settle();
    expect(viewer()!.getAttribute('aria-label')).toBe('c.png');
    expect(viewer()!.querySelector('.t-image-viewer__count')!.textContent).toBe('3 / 3');
    expect(level()).toBe('100%');
  });

  it('keeps Tab among its buttons', async () => {
    mount(0);
    await settle();
    const buttons = [...viewer()!.querySelectorAll<HTMLButtonElement>('button')];
    expect(buttons).toHaveLength(6);
    buttons[buttons.length - 1]!.focus();
    expect(tab().defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(buttons[0]);
    expect(tab(true).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(buttons[buttons.length - 1]);
  });

  it('steps with the arrows and closes with Escape', async () => {
    const index = mount(0);
    await settle();
    key('ArrowLeft');
    expect(index.value).toBe(2);
    await settle();
    key('ArrowRight');
    expect(index.value).toBe(0);
    escape();
    await settle();
    expect(index.value).toBeNull();
    expect(viewer()).toBeNull();
  });

  it('zooms with its buttons and keys, between fitted and eight times', async () => {
    mount(0);
    await settle();
    button('zoom-in').click();
    await settle();
    expect(transform().scale).toBeCloseTo(1.5);
    expect(level()).toBe('150%');
    expect(key('+').defaultPrevented).toBe(true);
    await settle();
    expect(level()).toBe('225%');
    button('zoom-out').click();
    await settle();
    expect(level()).toBe('150%');
    key('0');
    await settle();
    expect(transform()).toEqual({ x: 0, y: 0, scale: 1 });
    key('-');
    await settle();
    expect(level()).toBe('100%');
    for (let i = 0; i < 10; i += 1) key('=');
    await settle();
    expect(level()).toBe('800%');
  });

  it('zooms with the wheel around the pointer, and a double click zooms in there or fits again', async () => {
    mount(0);
    await settle();
    // The area's centre is (500, 400): a point right of it pulls the picture left as it grows.
    // happy-dom's WheelEvent is no MouseEvent and drops the pointer's place; a browser's has it.
    const wheel = Object.defineProperties(new WheelEvent('wheel', { deltaY: -400, bubbles: true, cancelable: true }), {
      clientX: { value: 600 },
      clientY: { value: 400 },
    });
    viewer()!.dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBe(true);
    await settle();
    const zoomed = transform();
    expect(zoomed.scale).toBeCloseTo(Math.E, 1);
    expect(zoomed.x).toBeLessThan(0);
    expect(zoomed.y).toBe(0);
    picture().dispatchEvent(new MouseEvent('dblclick', { clientX: 500, clientY: 400, bubbles: true }));
    await settle();
    expect(transform()).toEqual({ x: 0, y: 0, scale: 1 });
    picture().dispatchEvent(new MouseEvent('dblclick', { clientX: 500, clientY: 400, bubbles: true }));
    await settle();
    expect(transform().scale).toBe(2.5);
  });

  it('pans a zoomed picture by dragging, within its edges, and the drag closes nothing', async () => {
    const index = mount(0);
    await settle();
    key('0');
    // Unzoomed, a drag does not move the picture.
    pointer('pointerdown', 500, 400);
    pointer('pointermove', 450, 380);
    pointer('pointerup', 450, 380);
    await settle();
    expect(transform()).toEqual({ x: 0, y: 0, scale: 1 });

    for (let i = 0; i < 4; i += 1) key('+'); // 5.0625: 2025 x 1519, so 512 x 360 to move each way
    await settle();
    pointer('pointerdown', 500, 400);
    pointer('pointermove', 450, 380);
    pointer('pointerup', 450, 380);
    await settle();
    expect(transform()).toMatchObject({ x: -50, y: -20 });
    // The click a drag ends in (on the dark layer when it ended off the picture) is not a close.
    viewer()!.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    expect(index.value).toBe(0);
    // A drag that ended in no click (a touch pan) holds off no later press: the next click closes.
    pointer('pointerdown', 500, 400);
    pointer('pointermove', 450, 380);
    pointer('pointerup', 450, 380);
    viewer()!.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 2, button: 0, clientX: 20, clientY: 20, bubbles: true }));
    viewer()!.dispatchEvent(new PointerEvent('pointerup', { pointerId: 2, button: 0, clientX: 20, clientY: 20, bubbles: true }));
    viewer()!.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    expect(index.value).toBeNull();
    index.value = 0;
    await settle();
    for (let i = 0; i < 4; i += 1) key('+');
    await settle();

    pointer('pointerdown', 500, 400);
    pointer('pointermove', 5000, 5000);
    pointer('pointerup', 5000, 5000);
    await settle();
    const { x, y } = transform();
    expect(x).toBeCloseTo((400 * 1.5 ** 4 - 1000) / 2);
    expect(y).toBeCloseTo((300 * 1.5 ** 4 - 800) / 2);
  });

  it('moves a zoomed picture with the arrow keys instead of stepping, and opens the next picture fitted', async () => {
    const index = mount(0);
    await settle();
    for (let i = 0; i < 3; i += 1) key('+'); // 3.375: 1350 x 1013, so 175 x 106 to move each way
    await settle();
    key('ArrowLeft');
    await settle();
    expect(index.value).toBe(0);
    expect(transform().x).toBe(100);
    key('ArrowUp');
    await settle();
    expect(transform().y).toBe(80);
    button('next').click();
    await settle();
    expect(index.value).toBe(1);
    expect(transform()).toEqual({ x: 0, y: 0, scale: 1 });
  });

  it('closes on a click on the dark layer and on its close button', async () => {
    const index = mount(1);
    await settle();
    viewer()!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await settle();
    expect(index.value).toBeNull();
    index.value = 1;
    await settle();
    button('close').click();
    await settle();
    expect(index.value).toBeNull();
  });

  it('speaks the catalogue an app provides, and English for words an older one lacks', async () => {
    mount(0, zhCn);
    await settle();
    expect(button('zoom-in').getAttribute('aria-label')).toBe('放大');
    expect(button('close').getAttribute('aria-label')).toBe(zhCn.common.close);
    app!.unmount();
    host!.remove();
    const { zoomIn: _zoomIn, ...older } = en.imageViewer;
    mount(0, { ...zhCn, imageViewer: older });
    await settle();
    expect(button('zoom-in').getAttribute('aria-label')).toBe('Zoom in');
    expect(button('next').getAttribute('aria-label')).toBe(en.imageViewer.next);
  });
});

describe('TImageViewer over a TDialog', () => {
  it('is drawn above the dialog, and one Escape closes the viewer alone', async () => {
    const dialogOpen = ref(true);
    const index = ref<number | null>(null);
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp(
      defineComponent({
        setup: () => () =>
          h(
            TDialog,
            { modelValue: dialogOpen.value, 'onUpdate:modelValue': (v: boolean) => (dialogOpen.value = v), title: 'Rows' },
            () =>
              h(TImageViewer, {
                images: IMAGES,
                index: index.value,
                'onUpdate:index': (next: number | null) => (index.value = next),
              }),
          ),
      }),
    );
    app.mount(host);
    await settle();
    index.value = 0;
    await settle();

    const depth = (el: HTMLElement) => Number(/\+ (\d+)\)/.exec(el.style.zIndex)?.[1]);
    expect(depth(viewer()!)).toBeGreaterThan(depth(document.querySelector<HTMLElement>('.t-dialog-host')!));

    escape();
    await settle();
    expect(index.value).toBeNull();
    expect(dialogOpen.value).toBe(true);
    escape();
    await settle();
    expect(dialogOpen.value).toBe(false);
  });
});
