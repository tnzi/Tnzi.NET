/**
 * `useToast` + `TToastHost`: what a note says and how it is announced, how long
 * it stays (errors longer, held while pointed at or focused), how many show at
 * once (3 on a wide screen, 1 on a phone, the oldest making room), Escape (dismisses a focused note only) and focus (never taken, given
 * back after a note that held it leaves), and one host drawing a toaster.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TToastHost } = await import('../../src/components/controls/TToastHost.vue');
const { createToaster, useToast, TOAST_DURATION_MS, TOAST_ERROR_DURATION_MS, TOAST_MAX_VISIBLE_WIDE, TOAST_MAX_VISIBLE_NARROW } = await import(
  '../../src/components/controls/toast'
);

type Toaster = ReturnType<typeof createToaster>;

let apps: App[] = [];
const initialWidth = window.innerWidth;

function resizeTo(width: number): void {
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true });
  window.dispatchEvent(new Event('resize'));
}

function mountHost(toaster?: Toaster, props: () => Record<string, unknown> = () => ({})): void {
  const host = document.createElement('div');
  document.body.appendChild(host);
  const app = createApp({ render: () => h(TToastHost, { ...(toaster ? { toaster } : {}), ...props() }) });
  app.mount(host);
  apps.push(app);
}

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

/** The notes on screen; one on its way out (leave transition) no longer counts. */
const notes = () => [...document.querySelectorAll<HTMLElement>('[data-toast]:not(.t-toast-leave-active)')];
const texts = () => notes().map((note) => note.querySelector('.t-toast__text')?.textContent);

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  for (const app of apps) app.unmount();
  apps = [];
  vi.useRealTimers();
  document.body.innerHTML = '';
  resizeTo(initialWidth);
});

describe('TToastHost', () => {
  it('shows each tone, announcing errors as alerts and the rest as status in a polite region', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    toaster.toast.success('Saved');
    toaster.toast.error('Could not save');
    await settle();
    const stack = document.querySelector('[data-toast-stack]')!;
    expect(stack.getAttribute('aria-live')).toBe('polite');
    const [ok, failed] = notes();
    expect(ok!.getAttribute('role')).toBe('status');
    expect(ok!.className).toContain('t-toast--success');
    expect(failed!.getAttribute('role')).toBe('alert');
    expect(texts()).toEqual(['Saved', 'Could not save']);
    expect(failed!.querySelector('[data-toast-close]')?.getAttribute('aria-label')).toBe('Close');
  });

  it('takes a note away after its time, an error later than the others', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    toaster.toast.info('Copied');
    toaster.toast.error('Failed');
    await settle();
    vi.advanceTimersByTime(TOAST_DURATION_MS);
    await settle();
    expect(texts()).toEqual(['Failed']);
    vi.advanceTimersByTime(TOAST_ERROR_DURATION_MS - TOAST_DURATION_MS);
    await settle();
    expect(texts()).toEqual([]);
  });

  it('keeps a note with duration 0 until it is dismissed', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    const id = toaster.toast.warning('Offline', { duration: 0 });
    await settle();
    vi.advanceTimersByTime(60_000);
    await settle();
    expect(texts()).toEqual(['Offline']);
    toaster.toast.dismiss(id);
    await settle();
    expect(texts()).toEqual([]);
  });

  it('holds a note while the pointer is on it, and lets it run out what was left after', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    toaster.toast.info('Saved');
    await settle();
    vi.advanceTimersByTime(1000);
    notes()[0]!.dispatchEvent(new MouseEvent('mouseenter'));
    vi.advanceTimersByTime(10_000);
    await settle();
    expect(texts()).toEqual(['Saved']);
    notes()[0]!.dispatchEvent(new MouseEvent('mouseleave'));
    vi.advanceTimersByTime(TOAST_DURATION_MS - 1000 - 1);
    await settle();
    expect(texts()).toEqual(['Saved']);
    vi.advanceTimersByTime(1);
    await settle();
    expect(texts()).toEqual([]);
  });

  it('shows at most 3 on a wide screen, the oldest making room', async () => {
    resizeTo(1024);
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    expect(TOAST_MAX_VISIBLE_WIDE).toBe(3);
    for (let i = 1; i <= 5; i += 1) toaster.toast.info(`n${i}`);
    await settle();
    expect(texts()).toEqual(['n3', 'n4', 'n5']);
  });

  it('shows one at a time below 768px, a new note replacing the one shown', async () => {
    resizeTo(390);
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    expect(TOAST_MAX_VISIBLE_NARROW).toBe(1);
    toaster.toast.success('Saved');
    await settle();
    toaster.toast.error('Failed');
    await settle();
    expect(texts()).toEqual(['Failed']);
  });

  it('follows the viewport: turning narrow drops the extra notes at once, wide allows 3 again', async () => {
    resizeTo(1024);
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    toaster.toast.info('a');
    toaster.toast.info('b');
    await settle();
    resizeTo(767);
    await settle();
    expect(texts()).toEqual(['b']);
    resizeTo(768);
    await settle();
    toaster.toast.info('c');
    toaster.toast.info('d');
    await settle();
    expect(texts()).toEqual(['b', 'c', 'd']);
  });

  it('takes `narrow` over the viewport, and `narrowBreakpoint` for its own width', async () => {
    resizeTo(1024);
    const forced = createToaster();
    mountHost(forced, () => ({ narrow: true }));
    const wider = createToaster();
    mountHost(wider, () => ({ narrowBreakpoint: 1200 }));
    await settle();
    forced.toast.info('a');
    forced.toast.info('b');
    wider.toast.info('c');
    wider.toast.info('d');
    await settle();
    expect(texts()).toEqual(['b', 'd']);
  });

  it('never takes focus when a note appears or leaves', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    const field = document.createElement('input');
    document.body.appendChild(field);
    field.focus();
    toaster.toast.success('Saved');
    await settle();
    expect(document.activeElement).toBe(field);
    vi.advanceTimersByTime(TOAST_DURATION_MS);
    await settle();
    expect(document.activeElement).toBe(field);
  });

  it('dismisses a focused note on Escape and gives focus back to where it was', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    const field = document.createElement('input');
    document.body.appendChild(field);
    field.focus();
    toaster.toast.error('Failed');
    await settle();
    // Vue drops an event on a handler attached after the event began (by Date.now()), and
    // the fake clock stands still: let a moment pass, as it does between a note and a Tab.
    vi.advanceTimersByTime(1);
    const close = notes()[0]!.querySelector<HTMLButtonElement>('[data-toast-close]')!;
    close.dispatchEvent(new FocusEvent('focusin', { bubbles: true, relatedTarget: field }));
    close.focus();
    // Focused, the note stays past its time.
    vi.advanceTimersByTime(TOAST_ERROR_DURATION_MS * 2);
    await settle();
    expect(texts()).toEqual(['Failed']);

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    close.dispatchEvent(escape);
    await settle();
    expect(escape.defaultPrevented).toBe(true);
    expect(texts()).toEqual([]);
    expect(document.activeElement).toBe(field);
  });

  it('gives focus back after the close button too', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    const button = document.createElement('button');
    document.body.appendChild(button);
    button.focus();
    toaster.toast.info('Copied');
    await settle();
    // Vue drops an event on a handler attached after the event began (by Date.now()), and
    // the fake clock stands still: let a moment pass, as it does between a note and a Tab.
    vi.advanceTimersByTime(1);
    const close = notes()[0]!.querySelector<HTMLButtonElement>('[data-toast-close]')!;
    close.dispatchEvent(new FocusEvent('focusin', { bubbles: true, relatedTarget: button }));
    close.focus();
    close.click();
    await settle();
    expect(texts()).toEqual([]);
    expect(document.activeElement).toBe(button);
  });

  it('leaves Escape pressed elsewhere alone', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    await settle();
    toaster.toast.info('Copied');
    await settle();
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    document.body.dispatchEvent(escape);
    await settle();
    expect(escape.defaultPrevented).toBe(false);
    expect(texts()).toEqual(['Copied']);
  });

  it('draws a toaster in one host only when two are mounted, and in the next when the first goes', async () => {
    const toaster = createToaster();
    mountHost(toaster);
    mountHost(toaster);
    await settle();
    toaster.toast.info('Once');
    await settle();
    expect(texts()).toEqual(['Once']);
    apps.shift()!.unmount();
    await settle();
    expect(texts()).toEqual(['Once']);
  });

  it('useToast talks to the toaster a default host shows', async () => {
    mountHost();
    await settle();
    const toast = useToast();
    toast.success('From a store');
    await settle();
    expect(texts()).toEqual(['From a store']);
    toast.clear();
    await settle();
    expect(texts()).toEqual([]);
  });
});
