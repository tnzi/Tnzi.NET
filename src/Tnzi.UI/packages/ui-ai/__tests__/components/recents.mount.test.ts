/**
 * The sidebar's recent conversations and the full Recents page, mounted:
 *
 * - `TActionMenu` (the filter menu and each row's "..." menu) is reachable and
 *   operable by keyboard, opens submenus in place, and returns focus;
 * - `TThreadList` folds under its heading, filters / groups / sorts through
 *   `preferences`, and offers rename, archive and delete per row (delete still
 *   confirmed in place);
 * - `TRecentsPage` loads, switches Active / Archived, opens and acts on rows;
 * - `TChatApp` routes View all to the `recents` view and renders the page.
 *
 * Layout and positioning are not visible to happy-dom; those were checked in a
 * browser. This pins behaviour.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, shallowRef, type App, type Component } from 'vue';
import type { RecentThreadsController, RecentsPreferences } from '../../src/headless/recents';
import { DEFAULT_RECENTS_PREFERENCES } from '../../src/headless/recents';
import type { ThreadItem } from '../../src/components/chat/TThreadList.vue';

const { default: TActionMenu } = await import('../../src/components/overlay/TActionMenu.vue');
const { default: TThreadList } = await import('../../src/components/chat/TThreadList.vue');
const { default: TRecentsPage } = await import('../../src/components/chat/TRecentsPage.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

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

function key(name: string, target: Element | null = document.activeElement): void {
  (target ?? document.body).dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
}

const menu = () => document.body.querySelector('.t-action-menu') as HTMLElement | null;
const menuLabels = () => Array.from(menu()?.querySelectorAll('[data-menu-item]') ?? []).map((el) => el.textContent?.trim());
const focusedLabel = () => (document.activeElement as HTMLElement | null)?.textContent?.trim();

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  localStorage.clear();
});

describe('TActionMenu', () => {
  const items = [
    {
      id: 'status',
      label: 'Status',
      value: 'Active',
      children: [
        { id: 'status:active', label: 'Active', checked: true },
        { id: 'status:archived', label: 'Archived', checked: false },
      ],
    },
    { id: 'rename', label: 'Rename' },
    { id: 'delete', label: 'Delete', danger: true, dividerBefore: true },
  ];

  function mountMenu(onSelect = vi.fn()) {
    const el = mount(() =>
      h(TActionMenu as Component, { items, label: 'Menu', onSelect }, {
        trigger: ({ toggle, attrs }: { toggle: () => void; attrs: Record<string, unknown> }) =>
          h('button', { ...attrs, class: 'trigger', onClick: toggle }, 'open'),
      }),
    );
    return { el, trigger: el.querySelector('.trigger') as HTMLButtonElement, onSelect };
  }

  it('opens on body next to its trigger and moves focus into it', async () => {
    const { trigger } = mountMenu();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    trigger.click();
    await settle();

    expect(menu()).not.toBeNull();
    expect(menu()!.getAttribute('role')).toBe('menu');
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(menuLabels()).toEqual(['StatusActive', 'Rename', 'Delete']);
    expect(focusedLabel()).toBe('StatusActive');
  });

  it('is driven by the keyboard: arrows move, Right opens a submenu on its current choice, Escape goes back then closes', async () => {
    const { trigger, onSelect } = mountMenu();
    trigger.click();
    await settle();

    key('ArrowDown');
    expect(focusedLabel()).toBe('Rename');
    key('End');
    expect(focusedLabel()).toBe('Delete');
    key('ArrowDown');
    expect(focusedLabel()).toBe('StatusActive');

    key('ArrowRight');
    await settle();
    expect(menuLabels()).toEqual(['Status', 'Active', 'Archived']);
    expect(focusedLabel()).toBe('Active');
    expect(menu()!.querySelector('[aria-checked="true"]')?.textContent?.trim()).toBe('Active');

    key('Escape');
    await settle();
    expect(menuLabels()).toEqual(['StatusActive', 'Rename', 'Delete']);
    expect(focusedLabel()).toBe('StatusActive');

    key('Escape');
    await settle();
    expect(menu()).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('choosing a leaf emits its id, closes and gives focus back', async () => {
    const { trigger, onSelect } = mountMenu();
    trigger.click();
    await settle();
    (menu()!.querySelector('[data-menu-item="status"]') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="status:archived"]') as HTMLButtonElement).click();
    await settle();

    expect(onSelect).toHaveBeenCalledWith('status:archived');
    expect(menu()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('a press outside closes it', async () => {
    const { trigger } = mountMenu();
    trigger.click();
    await settle();
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await settle();
    expect(menu()).toBeNull();
  });
});

describe('TThreadList', () => {
  const threads: ThreadItem[] = [
    { id: 'a', title: 'Alpha', updatedAt: new Date().toISOString() },
    { id: 'b', title: 'Beta', updatedAt: new Date(Date.now() - 86_400_000).toISOString(), archived: true },
  ];

  function mountList(props: Record<string, unknown> = {}) {
    const handlers = {
      onRename: vi.fn(),
      onArchive: vi.fn(),
      onUnarchive: vi.fn(),
      onDelete: vi.fn(),
      'onView-all': vi.fn(),
      'onUpdate:preferences': vi.fn(),
      'onUpdate:collapsed': vi.fn(),
    };
    const el = mount(() =>
      h(TThreadList, {
        threads,
        enableRename: true,
        enableArchive: true,
        showViewAll: true,
        searchable: true,
        preferences: DEFAULT_RECENTS_PREFERENCES,
        ...handlers,
        ...props,
      }),
    );
    return { el, ...handlers };
  }

  it('rows are titles only, without icons', () => {
    const { el } = mountList();
    const rows = el.querySelectorAll('.t-thread-list__row');
    expect(rows).toHaveLength(2);
    expect(rows[0]!.querySelector('.t-thread-list__select svg, .t-thread-list__select .iconify')).toBeNull();
    expect(rows[0]!.querySelector('.t-thread-list__title')?.textContent).toBe('Alpha');
  });

  it('the label folds the list away and says so', async () => {
    const { el, 'onUpdate:collapsed': onCollapsed } = mountList();
    const label = el.querySelector('.t-thread-list__head-label') as HTMLButtonElement;
    expect(label.getAttribute('aria-expanded')).toBe('true');
    label.click();
    await settle();
    expect(onCollapsed).toHaveBeenCalledWith(true);
    expect(label.getAttribute('aria-expanded')).toBe('false');
    expect(el.querySelectorAll('.t-thread-list__row')).toHaveLength(0);
  });

  it('View all is a header action', () => {
    const { el, 'onView-all': onViewAll } = mountList();
    (el.querySelector('[aria-label="View all"]') as HTMLButtonElement).click();
    expect(onViewAll).toHaveBeenCalledTimes(1);
  });

  it('the filter menu shows the current values and changes one field at a time', async () => {
    const { el, 'onUpdate:preferences': onPreferences } = mountList();
    (el.querySelector('[aria-label="Filter and group"]') as HTMLButtonElement).click();
    await settle();
    expect(menuLabels()).toEqual(['StatusActive', 'Last activityAll', 'Group byNone', 'Sort byLast activity']);

    (menu()!.querySelector('[data-menu-item="sort"]') as HTMLButtonElement).click();
    await settle();
    expect(menuLabels()).toEqual(['Sort by', 'Name', 'Date created', 'Last activity']);
    (menu()!.querySelector('[data-menu-item="sort:name"]') as HTMLButtonElement).click();
    await settle();
    expect(onPreferences).toHaveBeenCalledWith({ sort: 'name' });
  });

  it('groups by date under headings when asked', () => {
    const preferences: RecentsPreferences = { ...DEFAULT_RECENTS_PREFERENCES, status: 'all', groupBy: 'date' };
    const { el } = mountList({ preferences });
    expect(Array.from(el.querySelectorAll('.t-thread-list__group')).map((g) => g.textContent)).toEqual(['Today', 'Yesterday']);
  });

  it('a row menu renames in place, archives, unarchives, and deletes after an inline confirmation', async () => {
    const { el, onRename, onArchive, onUnarchive, onDelete } = mountList();
    const openRowMenu = async (index: number) => {
      (el.querySelectorAll('.t-thread-list__row-act')[index] as HTMLButtonElement).click();
      await settle();
    };

    await openRowMenu(0);
    expect(menuLabels()).toEqual(['Rename', 'Archive', 'Delete']);
    (menu()!.querySelector('[data-menu-item="archive"]') as HTMLButtonElement).click();
    expect(onArchive).toHaveBeenCalledWith('a');

    await openRowMenu(1);
    expect(menuLabels()).toEqual(['Rename', 'Unarchive', 'Delete']);
    (menu()!.querySelector('[data-menu-item="unarchive"]') as HTMLButtonElement).click();
    expect(onUnarchive).toHaveBeenCalledWith('b');

    await openRowMenu(0);
    (menu()!.querySelector('[data-menu-item="rename"]') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-list__rename') as HTMLInputElement;
    expect(document.activeElement).toBe(input);
    input.value = 'Alpha 2';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();
    expect(onRename).toHaveBeenCalledWith('a', 'Alpha 2');
    expect(el.querySelector('.t-thread-list__rename')).toBeNull();

    await openRowMenu(1);
    (menu()!.querySelector('[data-menu-item="delete"]') as HTMLButtonElement).click();
    await settle();
    expect(onDelete).not.toHaveBeenCalled();
    (el.querySelector('.t-thread-list__confirm--yes') as HTMLButtonElement).click();
    expect(onDelete).toHaveBeenCalledWith('b');
  });

  it('Escape leaves a rename without sending it', async () => {
    const { el, onRename } = mountList();
    (el.querySelectorAll('.t-thread-list__row-act')[0] as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="rename"]') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-list__rename') as HTMLInputElement;
    input.value = 'Changed';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(onRename).not.toHaveBeenCalled();
  });

  it('without preferences or actions it is the plain list: no filter menu, delete only', async () => {
    const { el } = mountList({ preferences: undefined, enableRename: false, enableArchive: false, showViewAll: false });
    expect(el.querySelector('[aria-label="Filter and group"]')).toBeNull();
    expect(el.querySelector('[aria-label="View all"]')).toBeNull();
    (el.querySelectorAll('.t-thread-list__row-act')[0] as HTMLButtonElement).click();
    await settle();
    expect(menuLabels()).toEqual(['Delete']);
  });
});

function fakeController(initial: ThreadItem[] = []) {
  const threads = shallowRef<ThreadItem[]>(initial);
  const status = ref<'active' | 'archived'>('active');
  const loaded = ref(false);
  const controller: RecentThreadsController = {
    preferences: ref(DEFAULT_RECENTS_PREFERENCES),
    setPreferences: vi.fn(async () => {}),
    rename: vi.fn(async () => true),
    archive: vi.fn(async () => true),
    unarchive: vi.fn(async () => true),
    page: {
      threads,
      status,
      keyword: ref(''),
      hasMore: ref(false),
      loading: ref(false),
      loadingMore: ref(false),
      loaded,
      load: vi.fn(async () => {
        loaded.value = true;
      }),
      loadMore: vi.fn(async () => {}),
      setStatus: vi.fn(async (next: 'active' | 'archived') => {
        status.value = next;
      }),
      search: vi.fn(async () => {}),
    },
  };
  return controller;
}

describe('TRecentsPage', () => {
  const rows: ThreadItem[] = [
    { id: 'a', title: 'Alpha', updatedAt: new Date(Date.now() - 5 * 3_600_000).toISOString() },
    { id: 'b', title: 'Beta', updatedAt: new Date(Date.now() - 4 * 86_400_000).toISOString() },
  ];

  it('loads on open and lists titles with when they were last active', async () => {
    const controller = fakeController(rows);
    const el = mount(() => h(TRecentsPage, { controller }));
    await settle();

    expect(controller.page.load).toHaveBeenCalledTimes(1);
    expect(el.querySelector('.t-recents-page__heading')?.textContent).toBe('All tasks');
    expect(Array.from(el.querySelectorAll('.t-recents-page__time')).map((t) => t.textContent)).toEqual([
      '5 hours ago',
      '4 days ago',
    ]);
  });

  it('switches between Active and Archived, by click and by arrow key', async () => {
    const controller = fakeController(rows);
    const el = mount(() => h(TRecentsPage, { controller }));
    await settle();
    const tabs = el.querySelectorAll('[role="tab"]');
    expect(tabs[0]!.getAttribute('aria-selected')).toBe('true');

    (tabs[1] as HTMLButtonElement).click();
    expect(controller.page.setStatus).toHaveBeenCalledWith('archived');
    await settle();
    tabs[1]!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    expect(controller.page.setStatus).toHaveBeenLastCalledWith('active');
  });

  it('opens a row, and acts on it through the controller', async () => {
    const controller = fakeController(rows);
    const onSelect = vi.fn();
    const onDelete = vi.fn();
    const el = mount(() => h(TRecentsPage, { controller, include: (t: ThreadItem) => t.id !== 'b', onSelect, onDelete }));
    await settle();

    expect(el.querySelectorAll('.t-recents-page__row')).toHaveLength(1);
    (el.querySelector('.t-recents-page__open') as HTMLButtonElement).click();
    expect(onSelect).toHaveBeenCalledWith('a');

    (el.querySelector('.t-recents-page__row-act') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="archive"]') as HTMLButtonElement).click();
    await settle();
    expect(controller.archive).toHaveBeenCalledWith('a');

    (el.querySelector('.t-recents-page__row-act') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="delete"]') as HTMLButtonElement).click();
    await settle();
    (el.querySelector('.t-recents-page__confirm--yes') as HTMLButtonElement).click();
    expect(onDelete).toHaveBeenCalledWith('a');
  });

  it('says when a tab is empty', async () => {
    const controller = fakeController([]);
    const el = mount(() => h(TRecentsPage, { controller }));
    await settle();
    expect(el.querySelector('.t-recents-page__state')?.textContent?.trim()).toBe('No conversations yet.');
  });
});

describe('TRecentsPage search and rows', () => {
  const rows: ThreadItem[] = [{ id: 'a', title: 'Alpha', updatedAt: new Date(Date.now() - 5 * 3_600_000).toISOString() }];

  it('the search opens in the tabs row, searches as typed, and its x clears it back into the button', async () => {
    vi.useFakeTimers();
    try {
      const controller = fakeController(rows);
      const el = mount(() => h(TRecentsPage, { controller }));
      await settle();
      const bar = el.querySelector('.t-recents-page__bar') as HTMLElement;

      (bar.querySelector('[aria-label="Search"]') as HTMLButtonElement).click();
      await settle();
      /* In the same row as the tabs, in place of the button; no extra row. */
      const field = bar.querySelector('.t-recents-page__search') as HTMLElement;
      expect(field).not.toBeNull();
      expect(bar.querySelector('[aria-label="Search"]')).toBeNull();
      expect(el.querySelectorAll('.t-recents-page__search')).toHaveLength(1);
      const input = field.querySelector('input') as HTMLInputElement;
      expect(document.activeElement).toBe(input);
      expect(input.placeholder).toBe('Search recents...');

      input.value = 'alp';
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      expect(controller.page.search).toHaveBeenCalledWith('alp');

      (field.querySelector('[aria-label="Close search"]') as HTMLButtonElement).click();
      await settle();
      expect(bar.querySelector('.t-recents-page__search')).toBeNull();
      expect(controller.page.search).toHaveBeenLastCalledWith('');
      expect(document.activeElement).toBe(bar.querySelector('[aria-label="Search"]'));
    } finally {
      vi.useRealTimers();
    }
  });

  it('Escape in the field collapses it too', async () => {
    const el = mount(() => h(TRecentsPage, { controller: fakeController(rows) }));
    await settle();
    (el.querySelector('[aria-label="Search"]') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-recents-page__search input') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await settle();
    expect(el.querySelector('.t-recents-page__search')).toBeNull();
  });

  it('a row is one line: the title and its time inside the same button, time after the title', async () => {
    const el = mount(() => h(TRecentsPage, { controller: fakeController(rows) }));
    await settle();
    const open = el.querySelector('.t-recents-page__open') as HTMLElement;
    expect(Array.from(open.children).map((c) => c.className)).toEqual(['t-recents-page__title', 't-recents-page__time']);
    expect(open.textContent).toContain('5 hours ago');
  });
});

describe('TChatApp recents', () => {
  it('View all routes to the recents view, and that view is the Recents page', async () => {
    const controller = fakeController([{ id: 'a', title: 'Alpha', updatedAt: new Date().toISOString() }]);
    const onView = vi.fn();
    const view = ref('chat');
    const el = mount(() =>
      h(TChatApp, {
        sidebarStorageKey: null,
        autoApplyTheme: false,
        threads: [{ id: 'a', title: 'Alpha' }],
        recents: controller,
        view: view.value,
        'onUpdate:view': (next: string) => {
          onView(next);
          view.value = next;
        },
      }),
    );
    await settle();

    (el.querySelector('[aria-label="View all"]') as HTMLButtonElement).click();
    await settle();
    expect(onView).toHaveBeenCalledWith('recents');
    expect(el.querySelector('.t-recents-page')).not.toBeNull();
    expect(controller.page.load).toHaveBeenCalled();
  });

  it('without a controller the recents view is the product\'s own `#view`', async () => {
    const el = mount(() =>
      h(
        TChatApp,
        { sidebarStorageKey: null, autoApplyTheme: false, view: 'recents' },
        { view: () => h('p', { class: 'own-page' }, 'mine') },
      ),
    );
    await settle();
    expect(el.querySelector('.t-recents-page')).toBeNull();
    expect(el.querySelector('.own-page')).not.toBeNull();
  });

  it('remembers the folded list under the sidebar key', async () => {
    const el = mount(() =>
      h(TChatApp, { sidebarStorageKey: 'test-sidebar', autoApplyTheme: false, threads: [{ id: 'a', title: 'Alpha' }] }),
    );
    await settle();
    (el.querySelector('.t-thread-list__head-label') as HTMLButtonElement).click();
    await settle();
    expect(localStorage.getItem('test-sidebar:recents-collapsed')).toBe('1');
  });
});
