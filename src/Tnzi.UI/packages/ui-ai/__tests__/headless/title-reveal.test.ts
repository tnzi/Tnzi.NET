import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, withDirectives } from 'vue';
import {
  createTitleReveal,
  vTitleReveal,
  TITLE_REVEAL_DELAY_MS,
  TITLE_REVEAL_HOST_ATTR,
} from '../../src/headless/title-reveal';

/* happy-dom does no layout: give the title a measured overflow and a plain
   scrollLeft, and drive animation frames by hand with explicit timestamps. */
let frames: Array<{ id: number; cb: FrameRequestCallback }> = [];
let nextFrameId = 1;

function runFrame(now: number): void {
  const due = frames;
  frames = [];
  for (const f of due) f.cb(now);
}

function makeTitle(scrollWidth: number, clientWidth: number): HTMLElement {
  const el = document.createElement('span');
  let left = 0;
  Object.defineProperty(el, 'scrollWidth', { configurable: true, get: () => scrollWidth });
  Object.defineProperty(el, 'clientWidth', { configurable: true, get: () => clientWidth });
  Object.defineProperty(el, 'scrollLeft', {
    configurable: true,
    get: () => left,
    set: (v: number) => { left = v; },
  });
  return el;
}

function pointer(target: HTMLElement, type: 'pointerenter' | 'pointerleave', pointerType = 'mouse'): void {
  const event = new Event(type) as PointerEvent;
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  target.dispatchEvent(event);
}

const noMotion = { prefersReducedMotion: () => false };

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  frames = [];
  vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
    const id = nextFrameId++;
    frames.push({ id, cb });
    return id;
  });
  vi.stubGlobal('cancelAnimationFrame', (id: number) => {
    frames = frames.filter((f) => f.id !== id);
  });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('createTitleReveal', () => {
  it('waits out the delay, then scrolls at 30px/s and stops at the end', () => {
    const title = makeTitle(160, 100); // range 60px
    createTitleReveal(title, title, noMotion);

    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS - 1);
    expect(frames).toHaveLength(0);
    expect(title.hasAttribute('data-clipped')).toBe(false);

    vi.advanceTimersByTime(1);
    // Motion is about to start: the right fade replaces the ellipsis first.
    expect(title.hasAttribute('data-clipped')).toBe(true);
    expect(title.hasAttribute('data-scrolled')).toBe(false);

    runFrame(1000); // first frame anchors the clock
    expect(title.scrollLeft).toBe(0);
    runFrame(2000); // 1s at 0.03px/ms
    expect(title.scrollLeft).toBeCloseTo(30);
    expect(title.hasAttribute('data-scrolled')).toBe(true);
    expect(title.hasAttribute('data-clipped')).toBe(true);

    runFrame(4000); // past the end: clamps and stops
    expect(title.scrollLeft).toBe(60);
    expect(title.hasAttribute('data-scrolled')).toBe(true);
    expect(title.hasAttribute('data-clipped')).toBe(false);
    expect(frames).toHaveLength(0);
  });

  it('does nothing for an overflow of 8px or less', () => {
    const title = makeTitle(108, 100);
    createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(frames).toHaveLength(0);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });

  it('ignores touch pointers', () => {
    const title = makeTitle(200, 100);
    createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter', 'touch');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });

  it('a pointer passing through before the delay ends sets nothing in motion', () => {
    const title = makeTitle(200, 100);
    createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS / 2);
    pointer(title, 'pointerleave');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(frames).toHaveLength(0);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });

  it('snaps back to 0 and clears both fades on leave', () => {
    const title = makeTitle(200, 100);
    createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    runFrame(0);
    runFrame(1000);
    expect(title.scrollLeft).toBeGreaterThan(0);

    pointer(title, 'pointerleave');
    expect(title.scrollLeft).toBe(0);
    expect(title.hasAttribute('data-scrolled')).toBe(false);
    expect(title.hasAttribute('data-clipped')).toBe(false);
    expect(frames).toHaveLength(0);
  });

  it('jumps straight to the end under reduced motion', () => {
    const title = makeTitle(200, 100);
    createTitleReveal(title, title, { prefersReducedMotion: () => true });
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(frames).toHaveLength(0);
    expect(title.scrollLeft).toBe(100);
    expect(title.hasAttribute('data-scrolled')).toBe(true);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });

  it('listens on the host, so hovering anywhere in the row counts', () => {
    const host = document.createElement('div');
    const title = makeTitle(200, 100);
    host.appendChild(title);
    createTitleReveal(title, host, noMotion);
    pointer(host, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(title.hasAttribute('data-clipped')).toBe(true);
  });

  it('keeps revealing while held, and ends when the hold is released', () => {
    const title = makeTitle(200, 100);
    const reveal = createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    runFrame(0);
    runFrame(1000);
    reveal.setHold(true);

    pointer(title, 'pointerleave');
    expect(title.scrollLeft).toBeCloseTo(30);
    runFrame(2000);
    expect(title.scrollLeft).toBeCloseTo(60);

    reveal.setHold(false);
    expect(title.scrollLeft).toBe(0);
    expect(title.hasAttribute('data-scrolled')).toBe(false);
  });

  it('releasing the hold while still hovered keeps the reveal', () => {
    const title = makeTitle(200, 100);
    const reveal = createTitleReveal(title, title, noMotion);
    pointer(title, 'pointerenter');
    reveal.setHold(true);
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    runFrame(0);
    runFrame(1000);
    reveal.setHold(false);
    expect(title.scrollLeft).toBeCloseTo(30);
  });

  it('destroy cancels the pending frame, removes listeners and restores the title', () => {
    const title = makeTitle(200, 100);
    const reveal = createTitleReveal(title, title, noMotion);
    expect(title.hasAttribute('data-title-reveal')).toBe(true);
    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    runFrame(0);
    expect(frames).toHaveLength(1);

    reveal.destroy();
    expect(frames).toHaveLength(0);
    expect(title.hasAttribute('data-title-reveal')).toBe(false);
    expect(title.hasAttribute('data-clipped')).toBe(false);

    pointer(title, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });
});

describe('vTitleReveal', () => {
  it('binds to the nearest host, follows the hold value, and cleans up on unmount', async () => {
    const held = ref(false);
    const Row = defineComponent({
      setup: () => () =>
        h('div', { class: 'row', [TITLE_REVEAL_HOST_ATTR]: '' }, [
          withDirectives(h('span', { class: 'title' }, 'A long title'), [[vTitleReveal, held.value]]),
        ]),
    });
    const root = document.createElement('div');
    document.body.appendChild(root);
    const app = createApp(Row);
    app.mount(root);
    const host = root.querySelector('.row') as HTMLElement;
    const title = root.querySelector('.title') as HTMLElement;
    let left = 0;
    Object.defineProperty(title, 'scrollWidth', { configurable: true, get: () => 200 });
    Object.defineProperty(title, 'clientWidth', { configurable: true, get: () => 100 });
    Object.defineProperty(title, 'scrollLeft', { configurable: true, get: () => left, set: (v: number) => { left = v; } });
    expect(title.hasAttribute('data-title-reveal')).toBe(true);

    pointer(host, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(title.hasAttribute('data-clipped')).toBe(true);

    held.value = true;
    await nextTick();
    pointer(host, 'pointerleave');
    expect(title.hasAttribute('data-clipped')).toBe(true);

    held.value = false;
    await nextTick();
    expect(title.hasAttribute('data-clipped')).toBe(false);

    pointer(host, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(frames.length).toBeGreaterThan(0);
    app.unmount();
    root.remove();
    expect(frames).toHaveLength(0);
  });
});
