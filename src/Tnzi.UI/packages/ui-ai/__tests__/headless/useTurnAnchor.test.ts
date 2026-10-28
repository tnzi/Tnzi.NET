/**
 * useTurnAnchor: a sent message is scrolled to the top of the thread and the
 * reply grows under it. happy-dom does no layout, so the scroller's metrics
 * and the message's position are stubbed; what is under test is the reserve
 * arithmetic, the single scroll, and release.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { reserveForAnchor, useTurnAnchor, type UseTurnAnchorReturn } from '../../src/headless/useTurnAnchor';

describe('reserveForAnchor', () => {
  it('reserves exactly the shortfall that keeps the message from reaching the top', () => {
    // Message at 900, wanted at 24 from the top: the scroller must reach 876.
    // It can reach 1000 - 400 = 600, so 276 px are missing.
    expect(reserveForAnchor({ anchorTop: 900, topOffset: 24, scrollHeight: 1000, clientHeight: 400, reserve: 0 })).toBe(276);
  });

  it('is a fixed point once applied: measuring again returns the same reserve', () => {
    expect(reserveForAnchor({ anchorTop: 900, topOffset: 24, scrollHeight: 1276, clientHeight: 400, reserve: 276 })).toBe(276);
  });

  it('shrinks by what the reply grew, so the total height does not change', () => {
    // The reply grew by 100 px: scrollHeight with the old reserve is 1376.
    expect(reserveForAnchor({ anchorTop: 900, topOffset: 24, scrollHeight: 1376, clientHeight: 400, reserve: 276 })).toBe(176);
  });

  it('is never negative: a reply taller than the view needs no reserve', () => {
    expect(reserveForAnchor({ anchorTop: 900, topOffset: 24, scrollHeight: 3000, clientHeight: 400, reserve: 0 })).toBe(0);
  });
});

let app: App | null = null;
let root: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  root?.remove();
  app = null;
  root = null;
});

function rect(top: number): DOMRect {
  return { top, bottom: top, left: 0, right: 0, width: 0, height: 0, x: 0, y: top, toJSON: () => ({}) } as DOMRect;
}

function mount(topOffset = 24): { hook: UseTurnAnchorReturn; scroller: HTMLElement; message: HTMLElement; scrollTo: ReturnType<typeof vi.fn> } {
  let hook!: UseTurnAnchorReturn;
  const container = ref<HTMLElement | null>(null);
  root = document.createElement('div');
  document.body.appendChild(root);
  app = createApp(
    defineComponent({
      setup() {
        hook = useTurnAnchor({ container, topOffset });
        return () => h('div', { ref: container, class: 'scroller' }, [h('div', { class: 'col' }, [h('p', { class: 'msg' })])]);
      },
    }),
  );
  app.mount(root);
  const scroller = root.querySelector('.scroller') as HTMLElement;
  const message = root.querySelector('.msg') as HTMLElement;
  // The message sits 900px down the content, the view is 400 tall and scrolled
  // to its old bottom (content 1000 tall).
  Object.defineProperty(scroller, 'clientHeight', { value: 400, configurable: true });
  Object.defineProperty(scroller, 'scrollHeight', { get: () => 1000 + hook.reserve.value, configurable: true });
  Object.defineProperty(scroller, 'scrollTop', { value: 600, configurable: true, writable: true });
  scroller.getBoundingClientRect = () => rect(0);
  message.getBoundingClientRect = () => rect(900 - scroller.scrollTop);
  const scrollTo = vi.fn();
  Object.defineProperty(scroller, 'scrollTo', { value: scrollTo, configurable: true });
  return { hook, scroller, message, scrollTo };
}

describe('useTurnAnchor', () => {
  it('anchor: reserves room, then scrolls the message to the offset once', async () => {
    const { hook, message, scrollTo } = mount();
    await nextTick();
    await hook.anchor(() => message);
    expect(hook.anchored.value).toBe(true);
    expect(hook.reserve.value).toBe(276);
    expect(scrollTo).toHaveBeenCalledTimes(1);
    expect(scrollTo).toHaveBeenCalledWith({ top: 876, behavior: 'smooth' });
  });

  it('measuring again keeps the reserve and never scrolls', async () => {
    const { hook, message, scrollTo } = mount();
    await nextTick();
    await hook.anchor(() => message, 'auto');
    hook.measure();
    hook.measure();
    expect(hook.reserve.value).toBe(276);
    expect(scrollTo).toHaveBeenCalledTimes(1);
  });

  it('release drops the reserve, and measuring afterwards does nothing', async () => {
    const { hook, message } = mount();
    await nextTick();
    await hook.anchor(() => message);
    hook.release();
    expect(hook.anchored.value).toBe(false);
    expect(hook.reserve.value).toBe(0);
    hook.measure();
    expect(hook.reserve.value).toBe(0);
  });

  it('a message that is gone is not scrolled to', async () => {
    const { hook, scrollTo } = mount();
    await nextTick();
    await hook.anchor(() => null);
    expect(hook.reserve.value).toBe(0);
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('a newer anchor supersedes the scroll of an older one', async () => {
    const { hook, message, scrollTo } = mount();
    await nextTick();
    const first = hook.anchor(() => message);
    const second = hook.anchor(() => message, 'auto');
    await Promise.all([first, second]);
    expect(scrollTo).toHaveBeenCalledTimes(1);
    expect(scrollTo).toHaveBeenCalledWith({ top: 876, behavior: 'auto' });
  });
});
