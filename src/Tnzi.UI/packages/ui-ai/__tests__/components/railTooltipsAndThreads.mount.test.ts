/**
 * `TChatApp`'s collapsed rail, mounted:
 *
 * - every icon control shows a tooltip with its label (brand / expand, nav
 *   items, settings, the account avatar), and keeps its accessible name;
 * - a conversations button opens the recent conversations beside the rail:
 *   on hover (the pointer may travel into the panel), on keyboard focus, on a
 *   click; Escape closes it and returns focus; picking one selects it and
 *   closes it; an empty list says so; `recents` adds View all;
 * - a product whose list is not `threads` supplies the panel through
 *   `#rail-threads` and names the button with `railThreadsLabel`; with neither
 *   threads nor that slot there is no button;
 * - the phone drawer is never the rail, so none of this appears there.
 *
 * Timing rules are pinned in `headless/tooltip.test.ts` and
 * `headless/flyout.test.ts`; this file checks the wiring.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';
import { TOOLTIP_CLASS, TOOLTIP_DELAY_MS } from '../../src/headless/tooltip';
import { FLYOUT_CLOSE_DELAY_MS, FLYOUT_OPEN_DELAY_MS } from '../../src/headless/flyout';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

const KEY = 'test-rail';
let app: App | null = null;
let host: HTMLElement | null = null;
let clock = Date.parse('2026-02-01T00:00:00Z');

function setWidth(px: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: px });
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function pointer(target: Element, type: string, pointerType = 'mouse'): void {
  const event = new Event(type) as PointerEvent;
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  target.dispatchEvent(event);
}

const threads = [
  { id: 'a', title: 'Alpha' },
  { id: 'b', title: 'Bravo' },
  { id: 'c', title: 'Charlie' },
];

function mountRail(props: Record<string, unknown> = {}, slots: Record<string, unknown> = {}): HTMLElement {
  localStorage.setItem(KEY, 'icon');
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () =>
      h(
        TChatApp,
        {
          sidebarStorageKey: KEY,
          autoApplyTheme: false,
          threads,
          activeThreadId: 'b',
          ...props,
        },
        slots,
      ),
  });
  app.mount(host);
  return host;
}

const bubble = (): HTMLElement | null => document.querySelector(`.${TOOLTIP_CLASS}`);
const tooltipText = (): string | null =>
  bubble()?.hasAttribute('data-open') ? bubble()!.textContent : null;

async function hoverForTooltip(el: Element): Promise<string | null> {
  vi.setSystemTime((clock += 60_000));
  pointer(el, 'pointerenter');
  vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
  const text = tooltipText();
  pointer(el, 'pointerleave');
  return text;
}

const flyout = (el: HTMLElement) => el.querySelector('.t-chat-app__rail-threads') as HTMLElement | null;
const flyoutButton = (el: HTMLElement) => flyout(el)?.querySelector('button.t-rail-button') as HTMLButtonElement;
const panel = (el: HTMLElement) => el.querySelector('.t-rail-flyout__panel') as HTMLElement | null;

beforeEach(() => {
  localStorage.clear();
  setWidth(1280);
  vi.useFakeTimers();
  vi.setSystemTime((clock += 60_000));
});
afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
  setWidth(1280);
});

describe('rail tooltips', () => {
  it('the brand button, nav items and settings show their labels and keep their names', async () => {
    const el = mountRail();
    await settle();
    expect(el.querySelector('.t-chat-rail')).not.toBeNull();

    const brand = el.querySelector('.t-chat-rail__brand')!;
    expect(brand.hasAttribute('title')).toBe(false);
    expect(brand.getAttribute('aria-label')).toBe('Expand sidebar');
    expect(await hoverForTooltip(brand)).toBe('Expand sidebar');

    const newChat = el.querySelector('.t-chat-rail__btn[aria-label="New chat"]')!;
    expect(await hoverForTooltip(newChat)).toBe('New chat');

    const settings = el.querySelector('.t-chat-rail__btn[aria-label="Settings"]')!;
    expect(settings).not.toBeNull();
    expect(await hoverForTooltip(settings)).toBe('Settings');
  });

  it("the compact account avatar shows the account's name; its accessible name stays", async () => {
    const el = mountRail({ showAccountBar: true, accountName: 'Ada Lovelace' });
    await settle();
    const avatar = el.querySelector('.t-chat-rail .t-user-menu__trigger')!;
    expect(avatar.getAttribute('aria-label')).toBe('Account menu');
    expect(await hoverForTooltip(avatar)).toBe('Ada Lovelace');
  });

  it('a touch shows no tooltip', async () => {
    const el = mountRail();
    await settle();
    const newChat = el.querySelector('.t-chat-rail__btn[aria-label="New chat"]')!;
    pointer(newChat, 'pointerenter', 'touch');
    vi.advanceTimersByTime(1000);
    expect(tooltipText()).toBeNull();
  });

  it('the expanded sidebar is unchanged: no rail, no conversations flyout, the full list', async () => {
    const el = mountRail();
    localStorage.setItem(KEY, 'expanded');
    app?.unmount();
    app = createApp({ render: () => h(TChatApp, { sidebarStorageKey: KEY, autoApplyTheme: false, threads }) });
    app.mount(el);
    await settle();
    expect(el.querySelector('.t-chat-rail')).toBeNull();
    expect(el.querySelector('.t-rail-flyout')).toBeNull();
    expect(el.querySelectorAll('.t-thread-list__row')).toHaveLength(3);
  });
});

describe('rail conversations', () => {
  it('hover opens the list beside the rail, titles shown and the open one marked', async () => {
    const el = mountRail();
    await settle();
    const root = flyout(el)!;
    expect(flyoutButton(el).getAttribute('aria-label')).toBe('All tasks');
    expect(flyoutButton(el).getAttribute('aria-expanded')).toBe('false');

    pointer(root, 'pointerenter');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    await settle();
    expect(panel(el)).not.toBeNull();
    expect(flyoutButton(el).getAttribute('aria-expanded')).toBe('true');
    expect(panel(el)!.getAttribute('role')).toBe('dialog');
    const rows = Array.from(panel(el)!.querySelectorAll('.t-rail-thread-list__row'));
    expect(rows.map((row) => row.textContent?.trim())).toEqual(['Alpha', 'Bravo', 'Charlie']);
    expect(rows.map((row) => row.getAttribute('aria-current'))).toEqual([null, 'true', null]);
  });

  it('the pointer can travel from the button into the panel without it closing; leaving both closes it', async () => {
    const el = mountRail();
    await settle();
    const root = flyout(el)!;
    pointer(root, 'pointerenter');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    await settle();

    /* The panel is inside the flyout's element, so pointer events treat the
       button and the panel as one region; its padding bridges the gap. */
    expect(root.contains(panel(el))).toBe(true);
    // Crossing out of the region for a moment (cutting a corner) ...
    const shown = panel(el);
    pointer(root, 'pointerleave');
    vi.advanceTimersByTime(FLYOUT_CLOSE_DELAY_MS - 100);
    await settle();
    expect(panel(el)).toBe(shown);
    // ... and arriving over the panel: still open, the same panel (never closed and reopened).
    pointer(root, 'pointerenter');
    vi.advanceTimersByTime(1000);
    await settle();
    expect(panel(el)).toBe(shown);

    pointer(root, 'pointerleave');
    vi.advanceTimersByTime(FLYOUT_CLOSE_DELAY_MS);
    await settle();
    expect(panel(el)).toBeNull();
  });

  it('picking a conversation selects it and closes the panel', async () => {
    const onSelect = vi.fn();
    const el = mountRail({ onSelectThread: onSelect });
    await settle();
    flyoutButton(el).click();
    await settle();
    (panel(el)!.querySelectorAll('.t-rail-thread-list__row')[2] as HTMLButtonElement).click();
    await settle();
    expect(onSelect).toHaveBeenCalledWith('c');
    expect(panel(el)).toBeNull();
  });

  it('keyboard: focus opens it, ArrowDown moves into it, Escape closes it and returns focus', async () => {
    const el = mountRail();
    await settle();
    const button = flyoutButton(el);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }));
    button.focus();
    await settle();
    expect(panel(el)).not.toBeNull();

    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await settle();
    const first = panel(el)!.querySelector('.t-rail-thread-list__row') as HTMLButtonElement;
    expect(document.activeElement).toBe(first);

    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(panel(el)).toBeNull();
    expect(document.activeElement).toBe(button);
  });

  it('a click opens it pinned: the pointer leaving does not close it, a press outside does', async () => {
    const el = mountRail();
    await settle();
    flyoutButton(el).click();
    await settle();
    pointer(flyout(el)!, 'pointerleave');
    vi.advanceTimersByTime(1000);
    await settle();
    expect(panel(el)).not.toBeNull();

    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await settle();
    expect(panel(el)).toBeNull();
  });

  it('with `recents`, a View all row goes to the Recents view', async () => {
    const onView = vi.fn();
    const recents = {
      preferences: { value: undefined },
      page: { threads: { value: [] } },
      setPreferences: () => {},
      rename: async () => true,
      archive: async () => true,
      unarchive: async () => true,
    };
    const el = mountRail({ recents, 'onUpdate:view': onView });
    await settle();
    flyoutButton(el).click();
    await settle();
    const viewAll = panel(el)!.querySelector('.t-rail-thread-list__view-all') as HTMLButtonElement;
    expect(viewAll.textContent?.trim()).toBe('View all');
    viewAll.click();
    await settle();
    expect(onView).toHaveBeenCalledWith('recents');
    expect(panel(el)).toBeNull();
  });

  it('no threads and no slot: no button', async () => {
    const el = mountRail({ threads: [] });
    await settle();
    expect(flyout(el)).toBeNull();
  });

  it('`railThreads: false` hides it', async () => {
    const el = mountRail({ railThreads: false });
    await settle();
    expect(flyout(el)).toBeNull();
  });

  it("a product's own list through #rail-threads: the button appears without threads, named by railThreadsLabel", async () => {
    const onSelect = vi.fn();
    const el = mountRail(
      { threads: [], railThreadsLabel: 'Sessions', onSelectThread: onSelect },
      {
        'rail-threads': ({ close, select }: { close: () => void; select: (id: string) => void }) =>
          h('div', { class: 'own-list' }, [
            h('button', { class: 'own-row', onClick: () => select('s1') }, 'Session one'),
            h('button', { class: 'own-close', onClick: close }, 'Close'),
          ]),
      },
    );
    await settle();
    expect(flyoutButton(el).getAttribute('aria-label')).toBe('Sessions');
    flyoutButton(el).click();
    await settle();
    expect(panel(el)!.getAttribute('aria-label')).toBe('Sessions');
    expect(panel(el)!.querySelector('.t-rail-flyout__heading')?.textContent).toBe('Sessions');
    expect(panel(el)!.querySelector('.own-row')?.textContent).toBe('Session one');
    (panel(el)!.querySelector('.own-row') as HTMLButtonElement).click();
    await settle();
    expect(onSelect).toHaveBeenCalledWith('s1');
    expect(panel(el)).toBeNull();
  });

  it('an empty list says so', async () => {
    const { default: TRailThreadList } = await import('../../src/components/chat/TRailThreadList.vue');
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({ render: () => h(TRailThreadList, { threads: [] }) });
    app.mount(host);
    await settle();
    expect(host.querySelector('.t-rail-thread-list__rows')).toBeNull();
    expect(host.querySelector('.t-rail-thread-list__empty')?.textContent).toBe('No conversations yet.');
  });

  it('the phone drawer is never the rail: no rail buttons, no conversations flyout', async () => {
    localStorage.setItem(KEY, 'icon');
    setWidth(390);
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({ render: () => h(TChatApp, { sidebarStorageKey: KEY, autoApplyTheme: false, threads }) });
    app.mount(host);
    await settle();
    (host.querySelector('.t-chat-app__topbar-toggle') as HTMLButtonElement).click();
    await settle();
    expect(host.querySelector('.t-chat-rail')).toBeNull();
    expect(host.querySelector('.t-rail-flyout')).toBeNull();
    expect(host.querySelector('.t-thread-list__row')).not.toBeNull();
  });
});
