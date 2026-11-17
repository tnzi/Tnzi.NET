/**
 * The sidebar's rows, mounted:
 *
 * - recents rows and nav rows carry the title reveal (the row is the hover
 *   host, the title the scrolled element), and a recents row keeps its title
 *   revealed while its "..." menu is open;
 * - the account row's trailing icon buttons are named and tooltipped by their
 *   label, emit `action` without toggling the account menu, and are dropped
 *   in the rail's compact form.
 *
 * Motion and fades are pinned in `headless/title-reveal.test.ts`; layout is not
 * visible to happy-dom.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import { TITLE_REVEAL_DELAY_MS } from '../../src/headless/title-reveal';
import type { ThreadItem } from '../../src/components/chat/TThreadList.vue';

const { default: TThreadList } = await import('../../src/components/chat/TThreadList.vue');
const { default: TSidebarNav } = await import('../../src/components/layout/TSidebarNav.vue');
const { default: TUserMenu } = await import('../../src/components/overlay/TUserMenu.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => ReturnType<typeof h>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function pointer(target: Element, type: 'pointerenter' | 'pointerleave'): void {
  const event = new Event(type) as PointerEvent;
  Object.defineProperty(event, 'pointerType', { value: 'mouse' });
  target.dispatchEvent(event);
}

function overflow(el: Element, scrollWidth: number, clientWidth: number): void {
  let left = 0;
  Object.defineProperty(el, 'scrollWidth', { configurable: true, get: () => scrollWidth });
  Object.defineProperty(el, 'clientWidth', { configurable: true, get: () => clientWidth });
  Object.defineProperty(el, 'scrollLeft', { configurable: true, get: () => left, set: (v: number) => { left = v; } });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  vi.stubGlobal('requestAnimationFrame', () => 1);
  vi.stubGlobal('cancelAnimationFrame', () => undefined);
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const now = new Date().toISOString();
const threads: ThreadItem[] = [
  { id: 'a', title: 'A conversation title far too long for the sidebar', updatedAt: now },
  { id: 'b', title: 'Short', updatedAt: now },
];

describe('title reveal on sidebar rows', () => {
  it('a recents row is the hover host and its title is the revealed element', () => {
    const el = mount(() => h(TThreadList, { threads }));
    const rows = el.querySelectorAll('.t-thread-list__row');
    expect(rows).toHaveLength(2);
    rows.forEach((row) => {
      expect(row.hasAttribute('data-title-reveal-host')).toBe(true);
      expect(row.querySelector('.t-thread-list__title')!.hasAttribute('data-title-reveal')).toBe(true);
    });
  });

  it('a recents row stays revealed while its menu is open, and resets when it closes', async () => {
    const el = mount(() => h(TThreadList, { threads, enableRename: true, enableArchive: true }));
    const row = el.querySelector('.t-thread-list__row')!;
    const title = row.querySelector('.t-thread-list__title')!;
    overflow(title, 300, 100);

    pointer(row, 'pointerenter');
    vi.advanceTimersByTime(TITLE_REVEAL_DELAY_MS);
    expect(title.hasAttribute('data-clipped')).toBe(true);

    (row.querySelector('.t-thread-list__row-act') as HTMLButtonElement).click();
    await settle();
    expect(row.classList.contains('is-menu-open')).toBe(true);

    // The menu floats elsewhere, so the pointer leaves the row to reach it.
    pointer(row, 'pointerleave');
    expect(title.hasAttribute('data-clipped')).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    (document.activeElement as HTMLElement | null)?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(row.classList.contains('is-menu-open')).toBe(false);
    expect(title.hasAttribute('data-clipped')).toBe(false);
  });

  it('nav rows carry it too', () => {
    const el = mount(() =>
      h(TSidebarNav, { groups: [{ id: 'main', items: [{ id: 'apps', label: 'Apps and a long label', icon: 'lucide:grid' }] }] }),
    );
    const item = el.querySelector('.t-sidebar-nav__item')!;
    expect(item.hasAttribute('data-title-reveal-host')).toBe(true);
    expect(item.querySelector('.t-sidebar-nav__item-label')!.hasAttribute('data-title-reveal')).toBe(true);
  });
});

describe('account row icon buttons', () => {
  const actions = [{ id: 'apps', icon: 'lucide:layout-grid', label: 'Get apps' }];

  it('are named and tooltipped by their label, and emit without opening the menu', async () => {
    const onAction = vi.fn();
    const el = mount(() => h(TUserMenu, { name: 'Ada', actions, onAction }));
    const button = el.querySelector('.t-user-menu__action') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toBe('Get apps');
    expect(button.getAttribute('title')).toBe('Get apps');

    button.click();
    await settle();
    expect(onAction).toHaveBeenCalledWith('apps');
    expect(el.querySelector('.t-user-menu__trigger')!.getAttribute('aria-expanded')).toBe('false');
  });

  it('are dropped in the compact (rail) form', () => {
    const el = mount(() => h(TUserMenu, { name: 'Ada', actions, compact: true }));
    expect(el.querySelector('.t-user-menu__action')).toBeNull();
  });
});
