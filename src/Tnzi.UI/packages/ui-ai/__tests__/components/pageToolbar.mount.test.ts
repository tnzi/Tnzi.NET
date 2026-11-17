/**
 * The page toolbar, mounted for real: the Scheduled page's bar (tabs; search,
 * filter, sort, a primary button that opens a menu) on a wide screen and on a
 * phone, the collapsible search on its own, the tabs, the fold of the icon
 * actions into one "more" menu, and `TResourcePage`'s shared column.
 *
 * happy-dom does no layout: what is checked is structure, names, events and
 * focus, not geometry.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { default: TPageToolbar } = await import('../../src/components/layout/TPageToolbar.vue');
const { default: TToolbarSearch } = await import('../../src/components/layout/TToolbarSearch.vue');
const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue');
const { default: TResourcePage } = await import('../../src/components/layout/TResourcePage.vue');
const { foldActions, foldedItemId, parseFoldedId } = await import('../../src/components/layout/page-toolbar');
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
const press = (el: Element, key: string) => el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

const FILTER = {
  id: 'filter',
  label: 'Filter',
  icon: 'lucide:list-filter',
  items: [
    { id: 'all', label: 'All', checked: true },
    { id: 'paused', label: 'Paused', checked: false },
  ],
};
const SORT = { id: 'sort', label: 'Sort', icon: 'lucide:arrow-down-up', items: [{ id: 'next', label: 'Next run', checked: true }] };
const NEW = { label: 'New task', items: [{ id: 'compose', label: 'With the assistant' }, { id: 'manual', label: 'Manually' }] };

interface Events {
  actions: string[];
  selects: [string, string][];
  primary: number;
  primarySelects: string[];
}

function mountBar(props: Record<string, unknown> = {}) {
  const query = ref('');
  const tab = ref<'yours' | 'runs'>('yours');
  const events: Events = { actions: [], selects: [], primary: 0, primarySelects: [] };
  mount(() =>
    h(
      TPageToolbar,
      {
        search: true,
        query: query.value,
        'onUpdate:query': (v: string) => (query.value = v),
        actions: [FILTER, SORT],
        primary: NEW,
        onAction: (id: string) => events.actions.push(id),
        onSelect: (a: string, i: string) => events.selects.push([a, i]),
        onPrimary: () => (events.primary += 1),
        'onPrimary-select': (id: string) => events.primarySelects.push(id),
        ...props,
      },
      {
        start: () =>
          h(TSegmented, {
            modelValue: tab.value,
            'onUpdate:modelValue': (v: 'yours' | 'runs') => (tab.value = v),
            label: 'Views',
            options: [
              { value: 'yours', label: 'Yours' },
              { value: 'runs', label: 'Runs' },
            ],
          }),
      },
    ),
  );
  return { query, tab, events };
}

describe('TPageToolbar on a wide screen', () => {
  it('lays out tabs on the left and search, filter, sort, primary on the right, in that order', async () => {
    mountBar({ narrow: false });
    await settle();
    expect(q('.t-page-toolbar__start [role="radiogroup"]')?.getAttribute('aria-label')).toBe('Views');
    const tools = q('.t-page-toolbar__tools')!;
    const names = [...tools.querySelectorAll<HTMLButtonElement>('button')].map((b) => b.getAttribute('aria-label') ?? b.textContent?.trim());
    expect(names).toEqual([en.toolbar.search, 'Filter', 'Sort', 'New task']);
    expect(q('[data-toolbar-primary]')!.classList.contains('t-page-toolbar-button--primary')).toBe(true);
    expect(q('[data-toolbar-primary] .t-page-toolbar-chevron')).not.toBeNull();
  });

  it('opens an action menu and reports the item chosen', async () => {
    const { events } = mountBar({ narrow: false });
    await settle();
    const filter = q<HTMLButtonElement>('[data-toolbar-action="filter"]')!;
    expect(filter.getAttribute('aria-haspopup')).toBe('menu');
    filter.click();
    await settle();
    const menu = q('.t-action-menu')!;
    expect(menu.getAttribute('aria-label')).toBe('Filter');
    const paused = [...menu.querySelectorAll<HTMLElement>('[data-menu-item]')].find((el) => el.textContent?.includes('Paused'))!;
    paused.click();
    await settle();
    expect(events.selects).toEqual([['filter', 'paused']]);
  });

  it('shows an applied filter on its button', async () => {
    mountBar({ narrow: false, actions: [{ ...FILTER, active: true }, SORT] });
    await settle();
    expect(q('[data-toolbar-action="filter"]')!.classList.contains('is-active')).toBe(true);
    expect(q('[data-toolbar-action="sort"]')!.classList.contains('is-active')).toBe(false);
  });

  it('emits action for an icon action without a menu', async () => {
    const { events } = mountBar({ narrow: false, actions: [{ id: 'refresh', label: 'Refresh', icon: 'lucide:refresh-cw' }] });
    await settle();
    q<HTMLButtonElement>('[data-toolbar-action="refresh"]')!.click();
    expect(events.actions).toEqual(['refresh']);
  });

  it('a primary with a menu opens it; split, the button acts and the chevron opens it', async () => {
    const { events } = mountBar({ narrow: false });
    await settle();
    q<HTMLButtonElement>('[data-toolbar-primary]')!.click();
    await settle();
    [...document.querySelectorAll<HTMLElement>('.t-action-menu [data-menu-item]')].find((el) => el.textContent?.includes('Manually'))!.click();
    await settle();
    expect(events.primarySelects).toEqual(['manual']);
    expect(events.primary).toBe(0);
    app?.unmount();
    document.body.innerHTML = '';

    const split = mountBar({ narrow: false, primary: { ...NEW, split: true } });
    await settle();
    q<HTMLButtonElement>('[data-toolbar-primary]')!.click();
    expect(split.events.primary).toBe(1);
    const chevron = q<HTMLButtonElement>('[data-toolbar-primary-menu]')!;
    expect(chevron.getAttribute('aria-label')).toBe('New task: more options');
    chevron.click();
    await settle();
    expect(q('.t-action-menu')).not.toBeNull();
  });

  it('a plain primary is one button with a plus icon', async () => {
    const { events } = mountBar({ narrow: false, primary: { label: 'New note' } });
    await settle();
    const button = q<HTMLButtonElement>('[data-toolbar-primary]')!;
    expect(button.textContent?.trim()).toBe('New note');
    expect(button.getAttribute('aria-haspopup')).toBeNull();
    button.click();
    expect(events.primary).toBe(1);
  });
});

describe('TPageToolbar on a phone', () => {
  it('folds the icon actions into one more menu, with submenus that keep their choice', async () => {
    const { events } = mountBar({ narrow: true, actions: [{ ...FILTER, active: true }, SORT] });
    await settle();
    expect(q('[data-toolbar-action]')).toBeNull();
    const more = q<HTMLButtonElement>('[data-toolbar-more]')!;
    expect(more.getAttribute('aria-label')).toBe(en.toolbar.more);
    expect(more.classList.contains('is-active')).toBe(true);
    more.click();
    await settle();
    const rows = () => [...document.querySelectorAll<HTMLElement>('.t-action-menu [data-menu-item]')];
    expect(rows().map((el) => el.textContent?.trim())).toEqual([`Filter${en.toolbar.active}`, 'Sort']);
    rows()[0]!.click();
    await settle();
    rows().find((el) => el.textContent?.includes('Paused'))!.click();
    await settle();
    expect(events.selects).toEqual([['filter', 'paused']]);
  });

  it('keeps the primary icon and gives its label to screen readers', async () => {
    mountBar({ narrow: true });
    await settle();
    const button = q<HTMLButtonElement>('[data-toolbar-primary]')!;
    expect(button.querySelector('span')).toBeNull();
    expect(button.getAttribute('aria-label')).toBe('New task');
  });

  it('gives an open search the row', async () => {
    mountBar({ narrow: true });
    await settle();
    q<HTMLButtonElement>(`.t-page-toolbar__tools button[aria-label="${en.toolbar.search}"]`)!.click();
    await settle();
    expect(q('.t-page-toolbar')!.classList.contains('is-searching')).toBe(true);
    expect(q('.t-page-toolbar__start')).toBeNull();
    expect(q('[data-toolbar-more]')).toBeNull();
    expect(q('[data-toolbar-primary]')).toBeNull();
    expect(document.activeElement).toBe(q('input[type="search"]'));
  });
});

describe('TToolbarSearch', () => {
  function mountSearch(initial = '') {
    const query = ref(initial);
    mount(() =>
      h('div', [
        h(TToolbarSearch, { modelValue: query.value, 'onUpdate:modelValue': (v: string) => (query.value = v), label: 'Search tasks', placeholder: 'Search by name' }),
        h('button', { class: 'elsewhere' }, 'x'),
      ]),
    );
    return query;
  }
  const field = () => q<HTMLInputElement>('input[type="search"]');
  const icon = () => q<HTMLButtonElement>('.t-toolbar-search button');

  it('waits as an icon, opens into a labelled field and focuses it', async () => {
    mountSearch();
    await settle();
    expect(field()).toBeNull();
    expect(icon()!.getAttribute('aria-label')).toBe('Search tasks');
    icon()!.click();
    await settle();
    expect(field()!.getAttribute('aria-label')).toBe('Search tasks');
    expect(field()!.placeholder).toBe('Search by name');
    expect(q('[role="search"]')).not.toBeNull();
    expect(document.activeElement).toBe(field());
  });

  it('closes on leaving it empty, and stays open with a query', async () => {
    const query = mountSearch();
    await settle();
    icon()!.click();
    await settle();
    field()!.dispatchEvent(new Event('blur'));
    await settle();
    expect(field()).toBeNull();

    icon()!.click();
    await settle();
    field()!.value = 'backup';
    field()!.dispatchEvent(new Event('input'));
    await settle();
    expect(query.value).toBe('backup');
    field()!.dispatchEvent(new Event('blur'));
    await settle();
    expect(field()).not.toBeNull();
  });

  it('Escape clears the query, closes it and returns focus to the icon, and is not passed on', async () => {
    const query = mountSearch('backup');
    await settle();
    expect(field()).not.toBeNull();
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    field()!.dispatchEvent(event);
    await settle();
    expect(event.defaultPrevented).toBe(true);
    expect(query.value).toBe('');
    expect(field()).toBeNull();
    expect(document.activeElement).toBe(icon());
  });
});

describe('TSegmented', () => {
  it('is a radio group whose arrows move the choice', async () => {
    const value = ref('a');
    mount(() =>
      h(TSegmented, {
        modelValue: value.value,
        'onUpdate:modelValue': (v: string) => (value.value = v),
        label: 'Views',
        options: [
          { value: 'a', label: 'A' },
          { value: 'b', label: 'B', disabled: true },
          { value: 'c', label: 'C' },
        ],
      }),
    );
    await settle();
    const radios = [...document.querySelectorAll<HTMLButtonElement>('[role="radio"]')];
    expect(radios.map((r) => r.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false']);
    expect(radios.map((r) => r.tabIndex)).toEqual([0, -1, -1]);
    press(radios[0]!, 'ArrowRight');
    await settle();
    expect(value.value).toBe('c');
    press(radios[2]!, 'ArrowRight');
    await settle();
    expect(value.value).toBe('a');
  });

  it('icon-only: the label names each option, and an option without an icon keeps its text', async () => {
    mount(() =>
      h(TSegmented, {
        modelValue: 'list',
        label: 'Layout',
        iconOnly: true,
        options: [
          { value: 'list', label: 'List', icon: 'lucide:list' },
          { value: 'grid', label: 'Grid', icon: 'lucide:layout-grid' },
          { value: 'plain', label: 'Plain' },
        ],
      }),
    );
    await settle();
    expect(q('.t-segmented')!.classList.contains('t-segmented--icon-only')).toBe(true);
    const radios = [...document.querySelectorAll<HTMLButtonElement>('[role="radio"]')];
    expect(radios.map((r) => r.getAttribute('aria-label'))).toEqual(['List', 'Grid', null]);
    expect(radios.map((r) => r.classList.contains('is-icon'))).toEqual([true, true, false]);
    expect(radios.map((r) => r.querySelector('.t-segmented__label')?.textContent ?? null)).toEqual([null, null, 'Plain']);
    expect(radios[0]!.getAttribute('aria-checked')).toBe('true');
    expect(radios[0]!.tabIndex).toBe(0);
  });

  it('block: marks the group to fill its container', async () => {
    mount(() => h(TSegmented, { modelValue: 'a', label: 'Views', block: true, options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }] }));
    await settle();
    expect(q('.t-segmented')!.classList.contains('t-segmented--block')).toBe(true);
    expect(q('.t-segmented')!.classList.contains('t-segmented--icon-only')).toBe(false);
  });

  describe('declared size (happy-dom does no layout)', () => {
    const sfc = readFileSync(resolve(__dirname, '../../src/components/layout/TSegmented.vue'), 'utf8');
    const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
    const rule = (selector: string) => {
      const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const body = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css)?.[1];
      if (body === undefined) throw new Error(`no rule "${selector}"`);
      return body;
    };
    const decl = (body: string, prop: string) => new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`).exec(body)?.[1]?.trim();

    it('is as tall as the toolbar controls: 2px around options 4px shorter', () => {
      expect(decl(rule('.t-segmented'), '--t-segmented-pad')).toBe('2px');
      expect(decl(rule('.t-segmented'), 'padding')).toBe('var(--t-segmented-pad)');
      expect(decl(rule('.t-segmented'), 'border-radius')).toBe('9px');
      // Or 4px above and below a larger icon: segmentedIconSize.mount.test.ts works the sizes out.
      expect(decl(rule('.t-segmented__option'), 'height')?.replace(/\s+/g, ' ')).toBe(
        'max( calc(var(--tnzi-ai-toolbar-control-size, 30px) - 2 * var(--t-segmented-pad)), calc(var(--t-segmented-icon) + 8px) )',
      );
      expect(decl(rule('.t-segmented__option'), 'border-radius')).toBe('7px');
      expect(decl(rule('.t-segmented__option'), 'font-size')).toBe('13px');
      expect(decl(rule('.t-segmented'), '--t-segmented-icon')).toBe('var(--tnzi-ai-segmented-icon-size, 16px)');
      expect(decl(rule('.t-segmented__option :deep(svg)'), 'width')).toBe('var(--t-segmented-icon)');
    });

    it('icon-only options are 32px wide (or 7px either side of a larger icon), block options share the width', () => {
      expect(decl(rule('.t-segmented__option.is-icon'), 'width')).toBe('max(32px, calc(var(--t-segmented-icon) + 14px))');
      expect(decl(rule('.t-segmented--block'), 'width')).toBe('100%');
      expect(decl(rule('.t-segmented--block .t-segmented__option'), 'flex')).toBe('1 1 0');
    });
  });
});

describe('page toolbar folding', () => {
  it('prefixes item ids with their action so two menus may share one', () => {
    const folded = foldActions([FILTER, { ...SORT, items: [{ id: 'all', label: 'All' }] }], 'On');
    expect(folded.map((f) => f.children?.map((c) => c.id))).toEqual([
      [foldedItemId('filter', 'all'), foldedItemId('filter', 'paused')],
      [foldedItemId('sort', 'all')],
    ]);
    expect(folded[0]!.children![0]!.checked).toBe(true);
    expect(parseFoldedId(foldedItemId('sort', 'all'))).toEqual({ actionId: 'sort', itemId: 'all' });
    expect(parseFoldedId('refresh')).toEqual({ actionId: 'refresh', itemId: null });
    expect(foldActions([{ ...FILTER, active: true }], 'On')[0]!.value).toBe('On');
  });
});

describe('TResourcePage column', () => {
  it('puts toolbar and body in one column of the page width', async () => {
    mount(() => h(TResourcePage, { maxWidth: 'page', narrow: false }, { toolbar: () => h('span', 'bar'), default: () => h('p', 'body') }));
    await settle();
    const page = q('.t-resource-page')!;
    expect(page.style.getPropertyValue('--t-resource-page-max-width')).toBe('var(--tnzi-ai-page-max-width, 1040px)');
    expect(page.classList.contains('t-resource-page--narrow')).toBe(false);
  });

  it('takes a width of its own, and uses the phone gutters when narrow', async () => {
    mount(() => h(TResourcePage, { maxWidth: 720, narrow: true }, { default: () => h('p', 'body') }));
    await settle();
    const page = q('.t-resource-page')!;
    expect(page.style.getPropertyValue('--t-resource-page-max-width')).toBe('720px');
    expect(page.classList.contains('t-resource-page--narrow')).toBe(true);
  });
});
