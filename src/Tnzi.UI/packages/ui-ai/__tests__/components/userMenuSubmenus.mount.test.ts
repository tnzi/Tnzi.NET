/**
 * Submenus in the account menu (a language or theme picker): an item with
 * `children` opens them in place under a back row instead of being selected,
 * `checked` marks the current choice, and a child's id is what `select` emits.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import { opensSubmenu, resolveMenuLevel } from '../../src/components/overlay/user-menu';

const { default: TUserMenu } = await import('../../src/components/overlay/TUserMenu.vue');

interface Item {
  id: string;
  label: string;
  children?: Item[];
  checked?: boolean;
}

const tree: Item[] = [
  { id: 'billing', label: 'Billing' },
  {
    id: 'language',
    label: 'Language',
    children: [
      { id: 'lang:en', label: 'English', checked: true },
      { id: 'lang:fr', label: 'Français', checked: false },
    ],
  },
  {
    id: 'appearance',
    label: 'Appearance',
    children: [{ id: 'theme', label: 'Theme', children: [{ id: 'theme:dark', label: 'Dark' }] }],
  },
];

describe('resolveMenuLevel', () => {
  it('is the top level for an empty path', () => {
    expect(resolveMenuLevel(tree, [])).toEqual({ items: tree, trail: [] });
  });

  it('walks down the opened items, recording the trail', () => {
    const level = resolveMenuLevel(tree, ['appearance', 'theme']);
    expect(level.items.map((i) => i.id)).toEqual(['theme:dark']);
    expect(level.trail.map((i) => i.id)).toEqual(['appearance', 'theme']);
  });

  it('stops at the deepest level that still exists when the items changed under it', () => {
    const level = resolveMenuLevel(tree, ['language', 'gone']);
    expect(level.items.map((i) => i.id)).toEqual(['lang:en', 'lang:fr']);
    expect(level.trail.map((i) => i.id)).toEqual(['language']);
    expect(resolveMenuLevel(tree, ['nope']).items).toBe(tree);
  });

  it('an item opens a submenu only when it has children', () => {
    expect(opensSubmenu(tree[1]!)).toBe(true);
    expect(opensSubmenu(tree[0]!)).toBe(false);
    expect(opensSubmenu({ id: 'x', children: [] })).toBe(false);
  });
});

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function mountMenu(onSelect: (id: string) => void): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup: () => () => h(TUserMenu, { name: 'Ada', extraItems: tree, onSelect }),
  }));
  app.mount(host);
  return host;
}

const labels = (el: HTMLElement) =>
  Array.from(el.querySelectorAll('.t-popover-menu .t-user-menu__item-label')).map((n) => n.textContent?.trim());
const itemNamed = (el: HTMLElement, label: string) =>
  Array.from(el.querySelectorAll<HTMLButtonElement>('.t-popover-menu__item')).find(
    (b) => b.querySelector('.t-user-menu__item-label')?.textContent?.trim() === label,
  )!;

describe('TUserMenu submenus', () => {
  it('opens a submenu in place, with a back row, without selecting the parent', async () => {
    const onSelect = vi.fn();
    const el = mountMenu(onSelect);
    el.querySelector<HTMLButtonElement>('.t-user-menu__trigger')!.click();
    await settle();

    expect(itemNamed(el, 'Language').getAttribute('aria-haspopup')).toBe('menu');
    itemNamed(el, 'Language').click();
    await settle();

    expect(onSelect).not.toHaveBeenCalled();
    expect(el.querySelector('.t-user-menu__head')).toBeNull();
    expect(labels(el)).toEqual(['Language', 'English', 'Français']);
    expect(el.querySelector('.t-user-menu__back')).not.toBeNull();
  });

  it('marks the current choice and emits the child id, closing the menu', async () => {
    const onSelect = vi.fn();
    const el = mountMenu(onSelect);
    el.querySelector<HTMLButtonElement>('.t-user-menu__trigger')!.click();
    await settle();
    itemNamed(el, 'Language').click();
    await settle();

    const english = itemNamed(el, 'English');
    expect(english.getAttribute('role')).toBe('menuitemradio');
    expect(english.getAttribute('aria-checked')).toBe('true');
    expect(english.querySelector('.t-popover-menu__item-check')).not.toBeNull();
    expect(itemNamed(el, 'Français').getAttribute('aria-checked')).toBe('false');

    itemNamed(el, 'Français').click();
    await settle();
    expect(onSelect).toHaveBeenCalledWith('lang:fr');
    expect(el.querySelector('.t-popover-menu')).toBeNull();
  });

  it('the back row returns one level, and reopening starts at the top', async () => {
    const el = mountMenu(vi.fn());
    const trigger = el.querySelector<HTMLButtonElement>('.t-user-menu__trigger')!;
    trigger.click();
    await settle();
    itemNamed(el, 'Appearance').click();
    await settle();
    itemNamed(el, 'Theme').click();
    await settle();
    expect(labels(el)).toEqual(['Theme', 'Dark']);

    el.querySelector<HTMLButtonElement>('.t-user-menu__back')!.click();
    await settle();
    expect(labels(el)).toEqual(['Appearance', 'Theme']);

    trigger.click();
    await settle();
    trigger.click();
    await settle();
    expect(el.querySelector('.t-user-menu__head')).not.toBeNull();
    expect(el.querySelector('.t-user-menu__back')).toBeNull();
  });
});
