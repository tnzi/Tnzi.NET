/**
 * useLatestInView: the "scroll to the latest" button's state is whether the end
 * of the last message is below the visible part of the view, measured again on
 * scroll and on any change of size, not "is the scroller at its bottom".
 *
 * happy-dom does no layout, so positions are stubbed, and ResizeObserver /
 * requestAnimationFrame are replaced with ones the test drives.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { hiddenDistance, useLatestInView, type UseLatestInViewReturn } from '../../src/headless/useLatestInView';

describe('hiddenDistance', () => {
  it('is how far the end lies below the visible bottom', () => {
    expect(hiddenDistance(900, 700)).toBe(200);
  });
  it('is 0 when the end is in view', () => {
    expect(hiddenDistance(650, 700)).toBe(0);
  });
});

class FakeResizeObserver {
  static instances: FakeResizeObserver[] = [];
  observed = new Set<Element>();
  constructor(public callback: () => void) {
    FakeResizeObserver.instances.push(this);
  }
  observe(el: Element) { this.observed.add(el); }
  unobserve(el: Element) { this.observed.delete(el); }
  disconnect() { this.observed.clear(); }
  /** A size change of `el`, as the browser would report it. */
  static resize(el: Element) {
    for (const ro of FakeResizeObserver.instances) if (ro.observed.has(el)) ro.callback();
  }
}

let frames: Array<() => void> = [];
const flushFrames = () => {
  const run = frames;
  frames = [];
  run.forEach((cb) => cb());
};
const originalRO = globalThis.ResizeObserver;
const originalRAF = globalThis.requestAnimationFrame;
const originalCAF = globalThis.cancelAnimationFrame;

beforeEach(() => {
  FakeResizeObserver.instances = [];
  frames = [];
  globalThis.ResizeObserver = FakeResizeObserver as unknown as typeof ResizeObserver;
  globalThis.requestAnimationFrame = ((cb: () => void) => frames.push(cb)) as unknown as typeof requestAnimationFrame;
  globalThis.cancelAnimationFrame = vi.fn();
});

let app: App | null = null;
let root: HTMLElement | null = null;
afterEach(() => {
  app?.unmount();
  root?.remove();
  app = null;
  root = null;
  globalThis.ResizeObserver = originalRO;
  globalThis.requestAnimationFrame = originalRAF;
  globalThis.cancelAnimationFrame = originalCAF;
});

function rect(top: number, bottom: number): DOMRect {
  return { top, bottom, left: 0, right: 0, width: 0, height: bottom - top, x: 0, y: top, toJSON: () => ({}) } as DOMRect;
}

/**
 * A scroller from y=0 to 800 whose visible part ends at the composer (y=700),
 * and a last message whose end is at `end` (viewport y).
 */
function mount() {
  let hook!: UseLatestInViewReturn;
  const container = ref<HTMLElement | null>(null);
  const geometry = { end: 600, composerTop: 700 as number | null };
  root = document.createElement('div');
  document.body.appendChild(root);
  app = createApp(
    defineComponent({
      setup() {
        hook = useLatestInView({
          container,
          latest: () => root!.querySelector('.last') as HTMLElement | null,
          viewportBottom: () => geometry.composerTop,
        });
        return () => h('div', { ref: container, class: 'scroller' }, [h('div', { class: 'col' }, [h('p', { class: 'last' })])]);
      },
    }),
  );
  app.mount(root);
  const scroller = root.querySelector('.scroller') as HTMLElement;
  const col = root.querySelector('.col') as HTMLElement;
  const last = root.querySelector('.last') as HTMLElement;
  scroller.getBoundingClientRect = () => rect(0, 800);
  last.getBoundingClientRect = () => rect(geometry.end - 100, geometry.end);
  Object.defineProperty(scroller, 'scrollTop', { value: 500, configurable: true, writable: true });
  const scrollTo = vi.fn();
  Object.defineProperty(scroller, 'scrollTo', { value: scrollTo, configurable: true });
  return { hook: () => hook, geometry, scroller, col, last, scrollTo };
}

describe('useLatestInView', () => {
  it('is false while the end of the last message is above the composer', async () => {
    const { hook } = mount();
    await nextTick();
    hook().measure();
    expect(hook().hiddenBelow.value).toBe(false);
  });

  it('comes up when a size change pushes the end below the composer, and goes down when it comes back, with no scroll', async () => {
    const { hook, geometry, col, last } = mount();
    await nextTick();
    hook().measure();

    // A tool step opened: the column and the message grew.
    geometry.end = 1300;
    FakeResizeObserver.resize(col);
    flushFrames();
    expect(hook().hiddenBelow.value).toBe(true);

    // ...and closed again. Nothing scrolled and nothing was inserted: only sizes changed.
    geometry.end = 600;
    FakeResizeObserver.resize(last);
    flushFrames();
    expect(hook().hiddenBelow.value).toBe(false);
  });

  it('room below the last message (the reserve after a send) does not count as content below', async () => {
    const { hook, geometry, scroller } = mount();
    await nextTick();
    // The scroller could scroll a long way further (a reserve), but the reply ends in view.
    Object.defineProperty(scroller, 'scrollHeight', { value: 5000, configurable: true });
    geometry.end = 650;
    hook().measure();
    expect(hook().hiddenBelow.value).toBe(false);
  });

  it('a scroll re-measures', async () => {
    const { hook, geometry, scroller } = mount();
    await nextTick();
    hook().measure();
    geometry.end = 900;
    scroller.dispatchEvent(new Event('scroll'));
    flushFrames();
    expect(hook().hiddenBelow.value).toBe(true);
  });

  it('a few pixels hidden still count as in view', async () => {
    const { hook, geometry } = mount();
    await nextTick();
    geometry.end = 703;
    hook().measure();
    expect(hook().hiddenBelow.value).toBe(false);
  });

  it('without a composer the visible part ends at the scroller', async () => {
    const { hook, geometry } = mount();
    await nextTick();
    geometry.composerTop = null;
    geometry.end = 750;
    hook().measure();
    expect(hook().hiddenBelow.value).toBe(false);
    geometry.end = 820;
    hook().measure();
    expect(hook().hiddenBelow.value).toBe(true);
  });

  it('follows a new last message: its size changes are observed, the old one is released', async () => {
    const { hook, geometry, last } = mount();
    await nextTick();
    hook().measure();
    const observer = FakeResizeObserver.instances[0]!;
    expect(observer.observed.has(last)).toBe(true);

    last.classList.remove('last');
    const next = document.createElement('p');
    next.className = 'last';
    next.getBoundingClientRect = () => rect(geometry.end - 100, geometry.end);
    last.parentElement!.appendChild(next);
    hook().measure();
    expect(observer.observed.has(next)).toBe(true);
    expect(observer.observed.has(last)).toBe(false);
  });

  it('scrollToLatest scrolls just far enough to show the end, plus a margin', async () => {
    const { hook, geometry, scrollTo } = mount();
    await nextTick();
    geometry.end = 1300;
    hook().scrollToLatest('auto');
    expect(scrollTo).toHaveBeenCalledWith({ top: 500 + 600 + 16, behavior: 'auto' });
  });

  it('scrollToLatest does nothing when the end is already in view', async () => {
    const { hook, scrollTo } = mount();
    await nextTick();
    hook().scrollToLatest();
    expect(scrollTo).not.toHaveBeenCalled();
  });
});
