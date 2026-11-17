/**
 * Submenus in the account menu (a language or theme picker): an item with
 * `children` opens a submenu instead of being selected, `checked` marks the
 * current choice, and a child's id is what `select` emits. With a mouse the
 * submenu flies out beside the menu on hover; on a touch screen or a narrow
 * one it opens in place under a back row.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import { opensSubmenu, resolveMenuLevel } from '../../src/components/overlay/user-menu';
import { SUBMENU_CLOSE_DELAY_MS, SUBMENU_OPEN_DELAY_MS } from '../../src/headless/submenu-intent';

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

/* What the device says about its pointer: `fine` is a mouse that hovers. */
function stubPointer(kind: 'fine' | 'touch'): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: kind === 'fine' && query.includes('hover: hover'),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
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

describe('TUserMenu submenus on a touch screen', () => {
  beforeEach(() => stubPointer('touch'));

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

/* ---- With a mouse: submenus fly out beside the menu ---- */

const pointer = (type: string, el: Element, extra: PointerEventInit = {}) =>
  el.dispatchEvent(new PointerEvent(type, { bubbles: type !== 'pointerenter' && type !== 'pointerleave', pointerType: 'mouse', ...extra }));
const flyout = (el: HTMLElement) => el.querySelector<HTMLElement>('.t-user-menu__flyout');
const flyoutLabels = (el: HTMLElement) =>
  Array.from(flyout(el)?.querySelectorAll('.t-user-menu__item-label') ?? []).map((n) => n.textContent?.trim());
const key = (target: Element, name: string) =>
  target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));

async function openWithMouse(onSelect = vi.fn()): Promise<HTMLElement> {
  const el = mountMenu(onSelect);
  el.querySelector<HTMLButtonElement>('.t-user-menu__trigger')!.click();
  await settle();
  return el;
}

describe('TUserMenu submenus with a mouse', () => {
  beforeEach(() => {
    stubPointer('fine');
    vi.useFakeTimers();
  });

  it('resting on an item opens its submenu beside the list, which stays', async () => {
    const onSelect = vi.fn();
    const el = await openWithMouse(onSelect);
    const language = itemNamed(el, 'Language');
    pointer('pointerenter', language);
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    await settle();

    expect(flyoutLabels(el)).toEqual(['English', 'Français']);
    expect(flyout(el)!.getAttribute('role')).toBe('menu');
    expect(language.getAttribute('aria-expanded')).toBe('true');
    expect(language.getAttribute('aria-controls')).toBe(flyout(el)!.id);
    // The parent list is still there, header included, and nothing was selected.
    expect(el.querySelector('.t-user-menu__head')).not.toBeNull();
    expect(el.querySelector('.t-user-menu__back')).toBeNull();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('a click opens it too, and choosing a child emits its id and closes everything', async () => {
    const onSelect = vi.fn();
    const el = await openWithMouse(onSelect);
    itemNamed(el, 'Language').dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    expect(flyoutLabels(el)).toEqual(['English', 'Français']);

    const english = Array.from(flyout(el)!.querySelectorAll<HTMLButtonElement>('.t-popover-menu__item'))[0]!;
    expect(english.getAttribute('aria-checked')).toBe('true');
    expect(english.querySelector('.t-popover-menu__item-check')).not.toBeNull();

    Array.from(flyout(el)!.querySelectorAll<HTMLButtonElement>('.t-popover-menu__item'))[1]!.click();
    await settle();
    expect(onSelect).toHaveBeenCalledWith('lang:fr');
    expect(el.querySelector('.t-popover-menu')).toBeNull();
  });

  it('only one is open; a diagonal across other rows keeps it, leaving closes it after a delay', async () => {
    const el = await openWithMouse();
    const menu = el.querySelector('.t-popover-menu')!;
    const language = itemNamed(el, 'Language');
    pointer('pointerenter', language);
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    await settle();
    const panel = flyout(el)!;
    // happy-dom has no layout: give the fly-out a box to the right of the list.
    panel.getBoundingClientRect = () => new DOMRect(304, 93, 220, 120);

    // Out of Language at its lower right, across Appearance, towards the fly-out.
    pointer('pointerleave', language, { clientX: 200, clientY: 134 });
    pointer('pointerenter', itemNamed(el, 'Appearance'));
    pointer('pointermove', menu, { clientX: 260, clientY: 150 });
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS + 50);
    pointer('pointerenter', panel);
    vi.advanceTimersByTime(1000);
    await settle();
    expect(flyoutLabels(el)).toEqual(['English', 'Français']);

    // Resting on another item with a submenu switches to it: still one open.
    pointer('pointerenter', itemNamed(el, 'Appearance'));
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    await settle();
    expect(el.querySelectorAll('.t-user-menu__flyout')).toHaveLength(1);
    expect(flyoutLabels(el)).toEqual(['Theme']);

    // Leaving the menu and the fly-out closes it, after a grace period.
    pointer('pointerleave', menu);
    vi.advanceTimersByTime(SUBMENU_CLOSE_DELAY_MS - 50);
    await settle();
    expect(flyout(el)).not.toBeNull();
    vi.advanceTimersByTime(50);
    await settle();
    expect(flyout(el)).toBeNull();
    expect(el.querySelector('.t-popover-menu')).not.toBeNull();
  });

  it('a level below the fly-out drills in place inside it', async () => {
    const el = await openWithMouse();
    itemNamed(el, 'Appearance').dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    flyout(el)!.querySelector<HTMLButtonElement>('.t-popover-menu__item')!.click();
    await settle();
    expect(flyoutLabels(el)).toEqual(['Theme', 'Dark']);
    expect(flyout(el)!.querySelector('.t-user-menu__back')).not.toBeNull();
    expect(el.querySelector('.t-user-menu__head')).not.toBeNull();
  });

  it('keyboard: Enter or ArrowRight open it with focus inside, ArrowLeft and Escape come back', async () => {
    const el = await openWithMouse();
    const language = itemNamed(el, 'Language');
    expect(language.getAttribute('aria-haspopup')).toBe('menu');
    expect(language.getAttribute('aria-expanded')).toBe('false');

    // Enter on a button is a click without a pointer position (detail 0).
    language.focus();
    language.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 0 }));
    await settle();
    // Focus lands on the current choice.
    expect(document.activeElement?.textContent?.trim()).toBe('English');

    key(document.activeElement!, 'ArrowDown');
    expect(document.activeElement?.textContent?.trim()).toBe('Français');
    key(document.activeElement!, 'ArrowDown');
    expect(document.activeElement?.textContent?.trim()).toBe('English');

    key(document.activeElement!, 'ArrowLeft');
    await settle();
    expect(flyout(el)).toBeNull();
    expect(document.activeElement).toBe(itemNamed(el, 'Language'));
    expect(el.querySelector('.t-popover-menu')).not.toBeNull();

    key(itemNamed(el, 'Language'), 'ArrowRight');
    await settle();
    expect(flyoutLabels(el)).toEqual(['English', 'Français']);
    expect(flyout(el)!.contains(document.activeElement)).toBe(true);

    key(document.activeElement!, 'Escape');
    await settle();
    expect(flyout(el)).toBeNull();
    expect(document.activeElement).toBe(itemNamed(el, 'Language'));
    // Escape went back one level; the menu itself is still open.
    expect(el.querySelector('.t-popover-menu')).not.toBeNull();
  });

  it('a tap on a touch screen still drills in place, and hover from a finger does nothing', async () => {
    const el = await openWithMouse();
    const menu = el.querySelector('.t-popover-menu')!;
    pointer('pointerenter', itemNamed(el, 'Language'), { pointerType: 'touch' });
    vi.advanceTimersByTime(1000);
    await settle();
    expect(flyout(el)).toBeNull();

    pointer('pointerdown', menu, { pointerType: 'touch' });
    itemNamed(el, 'Language').dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    expect(flyout(el)).toBeNull();
    expect(labels(el)).toEqual(['Language', 'English', 'Français']);
    expect(el.querySelector('.t-user-menu__back')).not.toBeNull();
  });

  it('a narrow window drills in place even with a mouse', async () => {
    vi.stubGlobal('innerWidth', 600);
    const el = await openWithMouse();
    pointer('pointerenter', itemNamed(el, 'Language'));
    vi.advanceTimersByTime(1000);
    await settle();
    expect(flyout(el)).toBeNull();

    itemNamed(el, 'Language').dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
    await settle();
    expect(labels(el)).toEqual(['Language', 'English', 'Français']);
  });
});
