/**
 * What migrating real content pages onto the page toolbar asked for:
 *
 * - a page's own attributes (`data-*` test hooks) on every element the toolbar
 *   renders, without being able to override the toolbar's own;
 * - folded ids a CSS selector can address (`<actionId>::<itemId>`);
 * - `maxlength` on the search, and the package's clear button in place of the
 *   browser's native one;
 * - one icon action on a phone keeps its button;
 * - the chosen tab scrolled into view inside its scroller;
 * - no keyboard ring on a menu opened with the pointer;
 * - room under a page's body (`bodyPaddingBottom`).
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const { default: TPageToolbar } = await import('../../src/components/layout/TPageToolbar.vue');
const { default: TToolbarSearch } = await import('../../src/components/layout/TToolbarSearch.vue');
const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue');
const { default: TResourcePage } = await import('../../src/components/layout/TResourcePage.vue');
const { FOLDED_ID_SEPARATOR, foldActions, foldedItemId, parseFoldedId, scrollLeftToReveal, shouldFoldActions } = await import(
  '../../src/components/layout/page-toolbar'
);
const { en } = await import('../../src/locales/en');

let app: App | null = null;

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div');
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

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

const FILTER = {
  id: 'filter',
  label: 'Filter',
  icon: 'lucide:list-filter',
  attrs: { 'data-filter': '', 'aria-haspopup': 'false' },
  items: [
    { id: 'all', label: 'All', checked: true, attrs: { 'data-filter-state': 'all' } },
    { id: 'paused', label: 'Paused', checked: false, attrs: { 'data-filter-state': 'paused', 'data-menu-item': 'hijack' } },
  ],
};
const SORT = { id: 'sort', label: 'Sort', icon: 'lucide:arrow-down-up', attrs: { 'data-sort': '' }, items: [{ id: 'next', label: 'Next run' }] };
const PRIMARY = { label: 'New task', attrs: { 'data-new-task': '' }, menuAttrs: { 'data-new-menu': '' }, items: [{ id: 'manual', label: 'Manually', attrs: { 'data-new-manual': '' } }] };

function mountBar(props: Record<string, unknown> = {}) {
  const query = ref('');
  const selects: [string, string][] = [];
  mount(() =>
    h(TPageToolbar, {
      search: true,
      query: query.value,
      'onUpdate:query': (v: string) => (query.value = v),
      actions: [FILTER, SORT],
      primary: PRIMARY,
      searchButtonAttrs: { 'data-search-open': '' },
      searchInputAttrs: { 'data-search': '' },
      moreAttrs: { 'data-more': '' },
      onSelect: (a: string, i: string) => selects.push([a, i]),
      ...props,
    }),
  );
  return { query, selects };
}

describe('a page’s own attributes', () => {
  it('reach the action buttons, the primary, its chevron and the search icon, and cannot override the toolbar’s', async () => {
    mountBar({ narrow: false, primary: { ...PRIMARY, split: true } });
    await settle();
    const filter = q('[data-filter]')!;
    expect(filter.getAttribute('data-toolbar-action')).toBe('filter');
    expect(filter.getAttribute('aria-haspopup')).toBe('menu');
    expect(q('[data-sort]')).not.toBeNull();
    expect(q('[data-new-task]')?.hasAttribute('data-toolbar-primary')).toBe(true);
    expect(q('[data-new-menu]')?.hasAttribute('data-toolbar-primary-menu')).toBe(true);
    expect(q('[data-search-open]')?.getAttribute('aria-label')).toBe(en.toolbar.search);
  });

  it('reach the menu items, keeping the menu’s own id', async () => {
    mountBar({ narrow: false });
    await settle();
    q<HTMLButtonElement>('[data-filter]')!.click();
    await settle();
    const paused = q('[data-filter-state="paused"]')!;
    expect(paused.getAttribute('data-menu-item')).toBe('paused');
    expect(paused.getAttribute('role')).toBe('menuitemradio');
  });

  it('reach the search field once it is open', async () => {
    mountBar({ narrow: false });
    await settle();
    q<HTMLButtonElement>('[data-search-open]')!.click();
    await settle();
    expect(q('input[data-search]')).not.toBeNull();
  });

  it('reach the phone’s more button and the folded rows, at every level', async () => {
    const { selects } = mountBar({ narrow: true });
    await settle();
    const more = q<HTMLButtonElement>('[data-more]')!;
    expect(more.hasAttribute('data-toolbar-more')).toBe(true);
    more.click();
    await settle();
    const filterRow = q<HTMLElement>('.t-action-menu [data-filter]')!;
    expect(filterRow.getAttribute('data-menu-item')).toBe('filter');
    filterRow.click();
    await settle();
    // The folded id is printable, so a page's own selector reaches it.
    const paused = q<HTMLElement>(`[data-menu-item="filter${FOLDED_ID_SEPARATOR}paused"]`)!;
    expect(paused.getAttribute('data-filter-state')).toBe('paused');
    paused.click();
    await settle();
    expect(selects).toEqual([['filter', 'paused']]);
  });
});

describe('folded ids', () => {
  it('are <actionId>::<itemId>, printable and reversible, an item id may contain the separator', () => {
    expect(foldedItemId('filter', 'paused')).toBe('filter::paused');
    expect(parseFoldedId('filter::paused')).toEqual({ actionId: 'filter', itemId: 'paused' });
    expect(parseFoldedId('sort::a::b')).toEqual({ actionId: 'sort', itemId: 'a::b' });
    expect(parseFoldedId('refresh')).toEqual({ actionId: 'refresh', itemId: null });
    // eslint-disable-next-line no-control-regex
    expect(JSON.stringify(foldActions([FILTER, SORT], 'On'))).not.toMatch(/[\u0000-\u001f]/);
  });

  it('refuse an action id that contains the separator', () => {
    expect(() => foldActions([{ ...SORT, id: 'a::b' }], 'On')).toThrow(/a::b/);
  });

  it('fold only two or more actions, only on a phone', () => {
    expect(shouldFoldActions(true, 1)).toBe(false);
    expect(shouldFoldActions(true, 2)).toBe(true);
    expect(shouldFoldActions(false, 5)).toBe(false);
  });
});

describe('one action on a phone', () => {
  it('keeps its own button instead of a more menu', async () => {
    mountBar({ narrow: true, actions: [FILTER] });
    await settle();
    expect(q('[data-toolbar-more]')).toBeNull();
    expect(q('[data-toolbar-action="filter"]')).not.toBeNull();
  });
});

describe('TToolbarSearch', () => {
  function mountSearch(initial = '', props: Record<string, unknown> = {}) {
    const query = ref(initial);
    mount(() => h(TToolbarSearch, { modelValue: query.value, 'onUpdate:modelValue': (v: string) => (query.value = v), ...props }));
    return query;
  }

  it('takes a maxlength', async () => {
    mountSearch('x', { maxlength: 80 });
    await settle();
    expect(q<HTMLInputElement>('input[type="search"]')!.maxLength).toBe(80);
  });

  it('clears with its own button, staying open with focus in the field', async () => {
    const query = mountSearch('backup');
    await settle();
    const clear = q<HTMLButtonElement>('.t-toolbar-search__clear')!;
    expect(clear.getAttribute('aria-label')).toBe(en.toolbar.clearSearch);
    clear.click();
    await settle();
    expect(query.value).toBe('');
    expect(q('input[type="search"]')).not.toBeNull();
    expect(document.activeElement).toBe(q('input[type="search"]'));
    expect(q('.t-toolbar-search__clear')).toBeNull();
  });

  it('hides the browser’s own search decorations', () => {
    const sfc = readFileSync(resolve(process.cwd(), 'src/components/layout/TToolbarSearch.vue'), 'utf8') // happy-dom: import.meta.url is not a file URL;
    const rule = /\.t-toolbar-search__input::-webkit-search-cancel-button[^{]*\{([^}]*)\}/.exec(sfc)?.[1] ?? '';
    expect(rule).toMatch(/display:\s*none/);
  });
});

describe('TSegmented in a scroller', () => {
  it('works out the scroll that reveals a box', () => {
    const container = { left: 0, right: 200 };
    expect(scrollLeftToReveal(container, { left: 50, right: 120 }, 0)).toBeNull();
    expect(scrollLeftToReveal(container, { left: 180, right: 260 }, 0)).toBe(68);
    expect(scrollLeftToReveal(container, { left: -40, right: 30 }, 100)).toBe(52);
    expect(scrollLeftToReveal(container, { left: 150, right: 450 }, 0)).toBe(142);
  });

  it('scrolls the chosen option into view on mount and when the choice changes', async () => {
    const value = ref('a');
    const host = mount(() =>
      h('div', { class: 'scroller', style: 'overflow-x: auto' }, [
        h(TSegmented, {
          modelValue: value.value,
          'onUpdate:modelValue': (v: string) => (value.value = v),
          label: 'Views',
          options: ['a', 'b', 'c'].map((v) => ({ value: v, label: v.toUpperCase() })),
        }),
      ]),
    );
    const scroller = host.querySelector<HTMLElement>('.scroller')!;
    Object.defineProperty(scroller, 'scrollWidth', { value: 600, configurable: true });
    Object.defineProperty(scroller, 'clientWidth', { value: 200, configurable: true });
    scroller.getBoundingClientRect = () => ({ left: 0, right: 200 }) as DOMRect;
    const scrollTo = vi.fn();
    scroller.scrollTo = scrollTo as never;
    const boxes: Record<string, [number, number]> = { a: [10, 60], b: [70, 120], c: [400, 460] };
    for (const option of host.querySelectorAll<HTMLElement>('[role="radio"]')) {
      const [left, right] = boxes[option.dataset.value!]!;
      option.getBoundingClientRect = () => ({ left, right }) as DOMRect;
    }
    await settle();
    expect(scrollTo).not.toHaveBeenCalled(); // `a` is in view
    value.value = 'c';
    await settle();
    expect(scrollTo).toHaveBeenCalledWith(expect.objectContaining({ left: 268 }));
  });
});

describe('a menu opened with the pointer', () => {
  it('holds the keyboard ring back until a key is pressed; opened by keyboard it does not', async () => {
    mountBar({ narrow: false });
    await settle();
    const filter = q<HTMLButtonElement>('[data-filter]')!;
    filter.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    filter.click();
    await settle();
    expect(q('.t-action-menu')!.classList.contains('t-action-menu--pointer')).toBe(true);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await settle();
    expect(q('.t-action-menu')!.classList.contains('t-action-menu--pointer')).toBe(false);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(q('.t-action-menu')).toBeNull();
    filter.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    filter.click();
    await settle();
    expect(q('.t-action-menu')!.classList.contains('t-action-menu--pointer')).toBe(false);
  });
});

describe('a menu opened with a finger', () => {
  it('focuses the menu itself, so no item shows a ring; the first arrow key moves onto the items', async () => {
    mountBar({ narrow: false });
    await settle();
    const filter = q<HTMLButtonElement>('[data-filter]')!;
    const touch = new Event('pointerdown', { bubbles: true });
    Object.defineProperty(touch, 'pointerType', { value: 'touch' });
    filter.dispatchEvent(touch);
    filter.click();
    await settle();
    const menu = q<HTMLElement>('.t-action-menu')!;
    expect(document.activeElement).toBe(menu);
    expect(menu.classList.contains('t-action-menu--pointer')).toBe(false);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await settle();
    expect((document.activeElement as HTMLElement | null)?.dataset.menuItem).toBeTruthy();
  });
});

describe('TResourcePage', () => {
  it('takes the space under its body from bodyPaddingBottom', async () => {
    mount(() => h(TResourcePage, { bodyPaddingBottom: 96 }, { default: () => h('p', 'x') }));
    await settle();
    expect(q('.t-resource-page')!.style.getPropertyValue('--t-resource-page-bottom')).toBe('96px');
  });
});
