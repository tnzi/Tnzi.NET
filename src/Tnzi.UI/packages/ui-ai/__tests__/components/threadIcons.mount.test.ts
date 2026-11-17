/**
 * A conversation's icon (`threadIcon`), mounted through `TChatApp` in all three
 * places it lists conversations: the expanded sidebar (`TThreadList`), the
 * collapsed rail's flyout (`TRailThreadList`) and the Recents page
 * (`TRecentsPage`, including a page loaded later). One resolver serves the
 * three. The icon sits at the row's end, never before the title, so every title
 * starts at the same x; where the row has a "..." menu the two share one cell
 * and the "..." takes the icon's place on hover, focus, an open menu or a touch
 * screen. The label is the row button's tooltip and the start of its
 * accessible name.
 *
 * Size, colour and alignment are not visible to happy-dom; those were checked in
 * a browser. This pins behaviour, and the stylesheet rules that do the swap.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, ref, shallowRef, type App } from 'vue';
import type { RecentThreadsController } from '../../src/headless/recents';
import { DEFAULT_RECENTS_PREFERENCES } from '../../src/headless/recents';
import type { ThreadItem } from '../../src/components/chat/TThreadList.vue';
import type { ThreadIconResolver } from '../../src/components/chat/thread-icon';
import { resolveThreadIcon } from '../../src/components/chat/thread-icon';
import { FLYOUT_OPEN_DELAY_MS } from '../../src/headless/flyout';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TThreadList } = await import('../../src/components/chat/TThreadList.vue');

const threads: ThreadItem[] = [
  { id: 'a', title: 'Invoice from Acme', origin: 'Email', updatedAt: new Date().toISOString() },
  { id: 'b', title: 'Plain chat', updatedAt: new Date().toISOString() },
  { id: 'c', title: 'Morning digest', origin: 'Schedule', updatedAt: new Date().toISOString() },
];

const ICONS: Record<string, { icon: string; label: string }> = {
  Email: { icon: 'lucide:mail', label: 'From an email rule' },
  Schedule: { icon: 'lucide:alarm-clock', label: 'From a scheduled task' },
  Card: { icon: 'lucide:sparkles', label: 'From a card' },
};
const threadIcon: ThreadIconResolver = (thread) => (thread.origin ? ICONS[thread.origin] ?? null : null);

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(props: Record<string, unknown>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(TChatApp, { autoApplyTheme: false, threads, threadIcon, ...props }) });
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

/** A button's accessible name as a screen reader computes it here: a named image by its label, text by its text. */
function accessibleName(button: Element): string {
  const parts: string[] = [];
  const walk = (node: Node): void => {
    if (node instanceof Element) {
      if (node.getAttribute('aria-hidden') === 'true') return;
      const label = node.getAttribute('aria-label');
      if (label && node !== button) {
        parts.push(label);
        return;
      }
      node.childNodes.forEach(walk);
    } else if (node.nodeType === Node.TEXT_NODE && node.textContent?.trim()) {
      parts.push(node.textContent.trim());
    }
  };
  walk(button);
  return parts.join(' ');
}

/**
 * For each row button: [tooltip of the origin icon at the row's end or null,
 * accessible name of the button]. `row` finds the element holding the button
 * and its end (the button itself when the icon is inside it).
 */
function rowsOf(buttons: Element[], row: (button: Element) => Element): Array<[string | null, string]> {
  return buttons.map((button) => {
    const icon = row(button).querySelector('.t-thread-icon');
    return [icon ? icon.getAttribute('title') : null, accessibleName(button)];
  });
}

const sidebarRow = (button: Element): Element => button.closest('.t-thread-list__row')!;

/** The rules of an SFC's stylesheet whose selector list contains `part`, as "selector { body }". */
function rulesWith(file: string, part: string): string[] {
  const sfc = readFileSync(resolve(__dirname, '../../src/components/chat', file), 'utf8');
  const style = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
  return Array.from(style.matchAll(/([^{}]+)\{([^{}]*)\}/g))
    .filter((m) => m[1].includes(part))
    .map((m) => `${m[1].trim().replace(/\s+/g, ' ')} { ${m[2].trim().replace(/\s+/g, ' ')} }`);
}

function fakeController(initial: ThreadItem[]): RecentThreadsController {
  const pageThreads = shallowRef<ThreadItem[]>(initial);
  const loaded = ref(false);
  return {
    preferences: ref(DEFAULT_RECENTS_PREFERENCES),
    setPreferences: vi.fn(async () => {}),
    rename: vi.fn(async () => true),
    archive: vi.fn(async () => true),
    unarchive: vi.fn(async () => true),
    page: {
      threads: pageThreads,
      status: ref<'active' | 'archived'>('active'),
      keyword: ref(''),
      hasMore: ref(true),
      loading: ref(false),
      loadingMore: ref(false),
      loaded,
      load: vi.fn(async () => {
        loaded.value = true;
      }),
      loadMore: vi.fn(async () => {
        pageThreads.value = [...pageThreads.value, { id: 'd', title: 'From the dashboard', origin: 'Card' }];
      }),
      setStatus: vi.fn(async () => {}),
      search: vi.fn(async () => {}),
    },
  };
}

beforeEach(() => {
  localStorage.clear();
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 });
});

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
});

describe('thread icons', () => {
  it("the sidebar list draws the mapped icon at the row's end, in the menu's cell, with the label as tooltip and name", async () => {
    const el = mount({ sidebarStorageKey: null });
    await settle();

    const buttons = Array.from(el.querySelectorAll('.t-thread-list__select'));
    expect(rowsOf(buttons, sidebarRow)).toEqual([
      ['From an email rule', 'From an email rule Invoice from Acme'],
      [null, 'Plain chat'],
      ['From a scheduled task', 'From a scheduled task Morning digest'],
    ]);

    // Nothing before the title inside the button: every title starts at one x.
    for (const button of buttons) {
      const visible = Array.from(button.children).filter((c) => !c.classList.contains('sr-only'));
      expect(visible.map((c) => c.className)).toEqual(['t-thread-list__title']);
      expect(button.querySelector('.t-thread-icon')).toBeNull();
    }
    expect(buttons[0].getAttribute('title')).toBe('From an email rule');
    expect(buttons[1].hasAttribute('title')).toBe(false);

    // The icon and the "..." share the row's end cell; the icon is not announced a second time.
    const end = sidebarRow(buttons[0]).querySelector('.t-thread-list__end')!;
    expect(end.previousElementSibling).toBe(buttons[0]);
    expect(end.classList.contains('has-menu')).toBe(true);
    expect(end.querySelector('.t-thread-icon')).not.toBeNull();
    expect(end.querySelector('.t-thread-list__row-act')).not.toBeNull();
    const icon = end.querySelector('.t-thread-icon')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.hasAttribute('role')).toBe(false);
  });

  it('a thread mapped to null has nothing at its end but the "..." menu', async () => {
    const el = mount({ sidebarStorageKey: null });
    await settle();

    const plain = sidebarRow(Array.from(el.querySelectorAll('.t-thread-list__select'))[1]);
    const end = plain.querySelector('.t-thread-list__end')!;
    expect(end.querySelector('.t-thread-icon')).toBeNull();
    expect(end.querySelector('.t-thread-list__row-act')).not.toBeNull();
  });

  it('opening the menu marks the row, which is what puts the "..." in the icon\'s place', async () => {
    const el = mount({ sidebarStorageKey: null });
    await settle();

    const row = sidebarRow(el.querySelector('.t-thread-list__select')!);
    expect(row.classList.contains('is-menu-open')).toBe(false);
    (row.querySelector('.t-thread-list__row-act') as HTMLButtonElement).click();
    await settle();
    expect(row.classList.contains('is-menu-open')).toBe(true);
    expect(row.querySelector('.t-thread-icon')).not.toBeNull();
  });

  it('the stylesheet hides the icon and shows the "..." on hover, keyboard focus, an open menu and a touch screen', () => {
    const iconHidden = rulesWith('TThreadList.vue', '.has-menu .t-thread-list__icon').join('\n');
    for (const state of [':hover', ':focus-within', '.is-menu-open']) {
      expect(iconHidden).toContain(`.t-thread-list__row${state} .has-menu .t-thread-list__icon`);
    }
    expect(iconHidden).toMatch(/opacity: 0/);
    const actShown = rulesWith('TThreadList.vue', '.t-thread-list__row-act').join('\n');
    for (const state of [':hover', ':focus-within', '.is-menu-open']) {
      expect(actShown).toContain(`.t-thread-list__row${state} .t-thread-list__row-act`);
    }
    // A touch screen has no hover: the "..." always shows there, so the icon never does on a row with a menu.
    const sfc = readFileSync(resolve(__dirname, '../../src/components/chat/TThreadList.vue'), 'utf8');
    expect(sfc).toMatch(/@media \(hover: none\) \{\s*\.has-menu \.t-thread-list__icon \{\s*opacity: 0;/);

    const recentsHidden = rulesWith('TRecentsPage.vue', '.t-recents-page__icon').join('\n');
    for (const state of [':hover', ':focus-within', '.is-menu-open']) {
      expect(recentsHidden).toContain(`.t-recents-page__row${state} .t-recents-page__icon`);
    }
  });

  it('a row with no menu keeps its icon on hover; a row being opened shows the spinner instead', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({
      render: () =>
        h(TThreadList, {
          threads,
          threadIcon,
          enableDelete: false,
          enableRename: false,
          enableArchive: false,
          openingThreadId: 'c',
        }),
    });
    app.mount(host);
    await settle();

    const ends = Array.from(host.querySelectorAll('.t-thread-list__row')).map((row) => row.querySelector('.t-thread-list__end'));
    expect(ends[0]?.classList.contains('has-menu')).toBe(false);
    expect(ends[0]?.querySelector('.t-thread-icon')).not.toBeNull();
    // No origin and no menu: no end cell at all, as before.
    expect(ends[1]).toBeNull();
    expect(ends[2]?.querySelector('.t-thread-list__spinner')).not.toBeNull();
    expect(ends[2]?.querySelector('.t-thread-icon')).toBeNull();
  });

  it('without a resolver no row has an icon, and each row ends in the "..." menu as before', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({ render: () => h(TThreadList, { threads }) });
    app.mount(host);
    await settle();

    expect(host.querySelectorAll('.t-thread-icon')).toHaveLength(0);
    expect(host.querySelectorAll('.t-thread-list__row .t-thread-list__end .t-thread-list__row-act')).toHaveLength(threads.length);
    expect(host.querySelectorAll('.t-thread-list__select[title]')).toHaveLength(0);
  });

  it('the collapsed rail flyout draws the same icons', async () => {
    vi.useFakeTimers();
    localStorage.setItem('test-rail-icons', 'icon');
    const el = mount({ sidebarStorageKey: 'test-rail-icons', activeThreadId: 'b' });
    await settle();

    const root = el.querySelector('.t-chat-app__rail-threads')!;
    const enter = new Event('pointerenter') as PointerEvent;
    Object.defineProperty(enter, 'pointerType', { value: 'mouse' });
    root.dispatchEvent(enter);
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    await settle();

    const buttons = Array.from(el.querySelectorAll('.t-rail-flyout__panel .t-rail-thread-list__rows .t-rail-thread-list__row'));
    // No "..." in the flyout: the icon is the button's last child, after the title.
    expect(buttons[0].lastElementChild?.classList.contains('t-thread-icon')).toBe(true);
    expect(buttons[0].querySelector('.t-thread-icon')?.previousElementSibling?.classList.contains('t-rail-thread-list__title')).toBe(true);
    expect(rowsOf(buttons, (button) => button)).toEqual([
      ['From an email rule', 'From an email rule Invoice from Acme'],
      [null, 'Plain chat'],
      ['From a scheduled task', 'From a scheduled task Morning digest'],
    ]);
  });

  it('the Recents page draws the same icons, on pages loaded later too', async () => {
    const controller = fakeController(threads);
    const el = mount({ sidebarStorageKey: null, recents: controller, view: 'recents' });
    await settle();

    const page = () => Array.from(el.querySelectorAll('.t-recents-page__open'));
    const recentsRow = (button: Element): Element => button.closest('.t-recents-page__row')!;
    expect(rowsOf(page(), recentsRow).map(([label]) => label)).toEqual(['From an email rule', null, 'From a scheduled task']);
    expect(accessibleName(page()[0])).toContain('From an email rule Invoice from Acme');
    expect(page()[0].querySelector('.t-thread-icon')).toBeNull();
    const end = recentsRow(page()[0]).querySelector('.t-recents-page__end')!;
    expect(end.querySelector('.t-thread-icon')).not.toBeNull();
    expect(end.querySelector('.t-recents-page__row-act')).not.toBeNull();

    (el.querySelector('.t-recents-page__more') as HTMLButtonElement).click();
    await settle();

    expect(rowsOf(page(), recentsRow).map(([label]) => label)).toEqual([
      'From an email rule',
      null,
      'From a scheduled task',
      'From a card',
    ]);
  });
});

describe('resolveThreadIcon', () => {
  it('treats a missing resolver, null, an empty icon name and a throwing resolver as no icon', () => {
    const thread: ThreadItem = { id: 'x', title: 'x', origin: 'Email' };
    expect(resolveThreadIcon(undefined, thread)).toBeNull();
    expect(resolveThreadIcon(() => null, thread)).toBeNull();
    expect(resolveThreadIcon(() => undefined, thread)).toBeNull();
    expect(resolveThreadIcon(() => ({ icon: '', label: 'x' }), thread)).toBeNull();
    expect(
      resolveThreadIcon(() => {
        throw new Error('boom');
      }, thread),
    ).toBeNull();
    expect(resolveThreadIcon(threadIcon, thread)).toEqual({ icon: 'lucide:mail', label: 'From an email rule' });
  });

  it('an icon without a label is decoration: hidden from assistive tech, no tooltip', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({
      render: () => h(TThreadList, { threads: [threads[0]], threadIcon: () => ({ icon: 'lucide:mail', label: '' }) }),
    });
    app.mount(host);
    await settle();

    const icon = host.querySelector('.t-thread-icon')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.hasAttribute('role')).toBe(false);
    expect(icon.hasAttribute('title')).toBe(false);
    const button = host.querySelector('.t-thread-list__select')!;
    expect(accessibleName(button)).toBe('Invoice from Acme');
    expect(button.hasAttribute('title')).toBe(false);
  });
});
