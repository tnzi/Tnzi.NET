/**
 * `NavItem.action`: a quick action at the end of a nav row.
 *
 * - A row without one renders exactly the plain row button, with nothing added.
 * - A row with one is a box holding two sibling buttons (never a button in a
 *   button); the box is the hover host of the icon motion and title reveal.
 * - Clicking the action emits `item-action` (TSidebarNav) / `nav-item-action`
 *   (TChatApp, from the pinned nav and from the groups alike) and never selects
 *   the row; clicking the row still selects it.
 * - The collapsed rail does not show it.
 *
 * Visibility on hover / focus / touch is CSS and not visible to happy-dom.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import type { NavGroup } from '../../src/components/layout/TSidebarNav.vue';

const { default: TSidebarNav } = await import('../../src/components/layout/TSidebarNav.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

const KEY = 'test-nav-item-action';
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

const GROUPS: NavGroup[] = [
  {
    id: 'main',
    items: [
      { id: 'chat', label: 'Chat', icon: 'lucide:message-square', badge: 3 },
      {
        id: 'projects',
        label: 'Projects',
        icon: 'lucide:folder',
        iconMotion: 'pop',
        badge: 7,
        active: true,
        action: { id: 'create', icon: 'lucide:plus', label: 'Create project' },
      },
    ],
  },
];

function rowOf(el: HTMLElement, label: string): HTMLElement {
  const label_ = Array.from(el.querySelectorAll<HTMLElement>('.t-sidebar-nav__item-label'))
    .find((node) => node.textContent === label);
  expect(label_, `row "${label}"`).toBeTruthy();
  return label_!.closest<HTMLElement>('.t-sidebar-nav__item')!;
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
});

describe('TSidebarNav item action', () => {
  it('adds nothing to a row that declares no action', async () => {
    const el = mount(() => h(TSidebarNav, { groups: GROUPS }));
    await settle();
    const chat = rowOf(el, 'Chat');
    expect(chat.tagName).toBe('BUTTON');
    expect(chat.parentElement!.classList.contains('t-sidebar-nav__items')).toBe(true);
    expect(chat.hasAttribute('data-icon-motion-host')).toBe(true);
    expect(chat.hasAttribute('data-title-reveal-host')).toBe(true);
    expect(chat.querySelector('.t-sidebar-nav__item-end')).toBeNull();
    expect(chat.querySelector('.t-sidebar-nav__item-badge')!.textContent!.trim()).toBe('3');
    expect(el.querySelectorAll('.t-sidebar-nav__item-action')).toHaveLength(1);
    expect(el.querySelectorAll('.t-sidebar-nav__row')).toHaveLength(1);
  });

  it('renders the action beside the row button, never inside it', async () => {
    const el = mount(() => h(TSidebarNav, { groups: GROUPS }));
    await settle();
    const projects = rowOf(el, 'Projects');
    const row = projects.parentElement!;
    expect(row.classList.contains('t-sidebar-nav__row')).toBe(true);
    expect(row.hasAttribute('data-icon-motion-host')).toBe(true);
    expect(row.hasAttribute('data-title-reveal-host')).toBe(true);
    expect(projects.hasAttribute('data-icon-motion-focus')).toBe(true);
    expect(projects.getAttribute('aria-current')).toBe('page');
    expect(projects.classList.contains('is-active')).toBe(true);
    expect(projects.querySelector('button')).toBeNull();
    expect(projects.querySelector('.t-sidebar-nav__item-badge')!.textContent!.trim()).toBe('7');

    const action = row.querySelector<HTMLButtonElement>(':scope > .t-sidebar-nav__item-action')!;
    expect(action.getAttribute('type')).toBe('button');
    expect(action.getAttribute('aria-label')).toBe('Create project');
    expect(el.querySelector('button button')).toBeNull();
  });

  it('emits item-action and not select when the action is clicked; the row still selects', async () => {
    const selected: string[] = [];
    const actions: Array<[string, string]> = [];
    const el = mount(() =>
      h(TSidebarNav, {
        groups: GROUPS,
        onSelect: (item: { id: string }) => selected.push(item.id),
        onItemAction: (itemId: string, actionId: string) => actions.push([itemId, actionId]),
      }),
    );
    await settle();
    el.querySelector<HTMLButtonElement>('.t-sidebar-nav__item-action')!.click();
    await settle();
    expect(actions).toEqual([['projects', 'create']]);
    expect(selected).toEqual([]);

    rowOf(el, 'Projects').click();
    await settle();
    expect(selected).toEqual(['projects']);
    expect(actions).toHaveLength(1);
  });
});

describe('TChatApp nav-item-action', () => {
  const action = { id: 'create', icon: 'lucide:plus', label: 'Create project' };

  function mountApp(mode: 'expanded' | 'icon', listeners: Record<string, unknown>): HTMLElement {
    localStorage.setItem(KEY, mode);
    return mount(() =>
      h(TChatApp, {
        sidebarStorageKey: KEY,
        autoApplyTheme: false,
        mainNav: [{ id: 'projects', label: 'Projects', icon: 'lucide:folder', action }],
        navGroups: [{ id: 'spaces', label: 'Spaces', items: [{ id: 'space-1', label: 'Space one', action }] }],
        ...listeners,
      }),
    );
  }

  it('is emitted from the pinned nav and from the groups, without nav or a view change', async () => {
    const actions: Array<[string, string]> = [];
    const navs: string[] = [];
    const views: string[] = [];
    const el = mountApp('expanded', {
      onNavItemAction: (itemId: string, actionId: string) => actions.push([itemId, actionId]),
      onNav: (item: { id: string }) => navs.push(item.id),
      'onUpdate:view': (view: string) => views.push(view),
    });
    await settle();
    const buttons = el.querySelectorAll<HTMLButtonElement>('.t-collapsible-sidebar .t-sidebar-nav__item-action');
    expect(buttons).toHaveLength(2);
    buttons.forEach((button) => button.click());
    await settle();
    expect(actions).toEqual([['projects', 'create'], ['space-1', 'create']]);
    expect(navs).toEqual([]);
    expect(views).toEqual([]);
  });

  it('is absent from the collapsed rail', async () => {
    const el = mountApp('icon', {});
    await settle();
    expect(el.querySelector('.t-chat-rail')).toBeTruthy();
    expect(el.querySelector('.t-sidebar-nav__item-action')).toBeNull();
  });
});
