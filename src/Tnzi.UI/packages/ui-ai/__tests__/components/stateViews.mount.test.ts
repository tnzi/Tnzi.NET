/**
 * The states of a list (`TStateView`), an empty page (`TEmptyState` with
 * `TEmptyArt`) and its templates (`TTemplateRow`): what each renders, which
 * state wins, the retry button, the page's own drawing, and the template row's
 * keyboard reach.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { TStateView, TEmptyState, TEmptyArt, TTemplateRow, EMPTY_ART_NAMES } = await import('../../src/components/controls/index');
const { createAiI18n } = await import('../../src/i18n/index');
const { zhCn } = await import('../../src/locales/zh-cn');

let app: App | null = null;

function mount(render: () => unknown, setup?: () => void): HTMLElement {
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    setup() {
      setup?.();
      return render;
    },
  });
  app.mount(host);
  return host;
}

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

describe('TStateView', () => {
  it('shows placeholder rows while loading, busy and named for a screen reader', () => {
    const root = mount(() => h(TStateView, { loading: true, rows: 4, error: 'x', empty: true }, () => 'content'));
    const state = root.querySelector('[data-state]')!;
    expect(state.getAttribute('data-state')).toBe('loading');
    expect(state.getAttribute('aria-busy')).toBe('true');
    expect(state.querySelectorAll('.t-state__skeleton')).toHaveLength(4);
    expect(state.querySelector('.sr-only')?.textContent).toBe('Loading...');
    expect(root.textContent).not.toContain('content');
  });

  it('says what went wrong as an alert, with a retry button when retry is listened to', async () => {
    const onRetry = vi.fn();
    const root = mount(() => h(TStateView, { error: 'Network down', empty: true, onRetry }));
    const state = root.querySelector('[data-state="error"]')!;
    expect(state.getAttribute('role')).toBe('alert');
    expect(state.querySelector('.t-state__text')?.textContent).toBe('Network down');
    const button = state.querySelector('button')!;
    expect(button.textContent?.trim()).toBe('Retry');
    button.click();
    await nextTick();
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('has no retry button when nobody listens', () => {
    const root = mount(() => h(TStateView, { error: 'Network down' }));
    expect(root.querySelector('button')).toBeNull();
  });

  it('is an icon, a title, a line and an action when empty without a drawing', () => {
    const root = mount(() =>
      h(TStateView, { empty: true, emptyTitle: 'No results', emptyText: 'Try another word', compact: true }, { action: () => h('button', 'Clear') }),
    );
    const state = root.querySelector('[data-state="empty"]')!;
    expect(state.classList.contains('is-compact')).toBe(true);
    expect(state.querySelector('.t-state__title')?.textContent).toBe('No results');
    expect(state.querySelector('.t-state__text')?.textContent).toBe('Try another word');
    expect(state.querySelector('.t-state__action button')?.textContent).toBe('Clear');
    expect(root.querySelector('[data-empty]')).toBeNull();
  });

  it('is TEmptyState with the drawing and what the page offers under it when given emptyArt', () => {
    const root = mount(() =>
      h(TStateView, { empty: true, emptyArt: 'stopwatch', emptyTitle: 'No tasks yet' }, {
        action: () => h('button', { class: 'create' }, 'New task'),
        'empty-more': () => h('ul', { class: 't-templates' }),
      }),
    );
    const empty = root.querySelector('[data-empty]')!;
    expect(empty.querySelector('[data-empty-art="stopwatch"]')).not.toBeNull();
    expect(empty.querySelector('.t-empty-state__title')?.textContent).toBe('No tasks yet');
    expect(empty.querySelector('.t-empty-state__action .create')).not.toBeNull();
    expect(empty.querySelector('.t-templates')).not.toBeNull();
  });

  it('takes the page own drawing through #empty-art', () => {
    const root = mount(() =>
      h(TStateView, { empty: true, emptyTitle: 'Nothing' }, { 'empty-art': () => h('svg', { class: 't-empty-art own' }) }),
    );
    expect(root.querySelector('[data-empty] svg.own')).not.toBeNull();
  });

  it('is the content when no state is set', () => {
    const root = mount(() => h(TStateView, null, () => h('p', { class: 'content' }, 'rows')));
    expect(root.querySelector('.content')).not.toBeNull();
    expect(root.querySelector('[data-state]')).toBeNull();
  });

  it('speaks the provided catalogue', () => {
    const root = mount(() => h(TStateView, { error: 'x', onRetry: () => {} }), () => createAiI18n(zhCn));
    expect(root.querySelector('button')?.textContent?.trim()).toBe(zhCn.common.retry);
  });
});

describe('TEmptyArt', () => {
  it('has thirteen drawings, each drawn', () => {
    expect(EMPTY_ART_NAMES).toHaveLength(13);
    for (const name of EMPTY_ART_NAMES) {
      const root = mount(() => h(TEmptyArt, { name }));
      const svg = root.querySelector('svg')!;
      expect(svg.getAttribute('data-empty-art')).toBe(name);
      expect(svg.getAttribute('aria-hidden')).toBe('true');
      expect(svg.classList.contains('t-empty-art')).toBe(true);
      expect(svg.querySelectorAll('path, circle').length).toBeGreaterThan(1);
      expect(svg.querySelector('.t-empty-art__fill')).not.toBeNull();
      app!.unmount();
      app = null;
      document.body.innerHTML = '';
    }
  });
});

describe('TEmptyState', () => {
  it('leaves the text line out when there is none', () => {
    const root = mount(() => h(TEmptyState, { art: 'mail', title: 'No mail' }));
    expect(root.querySelector('.t-empty-state__text')).toBeNull();
    expect(root.querySelector('.t-empty-state__action')).toBeNull();
  });
});

describe('TTemplateRow', () => {
  it('is a button with its icon, name, description and fact, emitting pick', async () => {
    const onPick = vi.fn();
    const root = mount(() =>
      h(TTemplateRow, { icon: 'lucide:sun', name: 'Morning brief', description: 'News at 8', when: 'Every day', onPick }),
    );
    const row = root.querySelector('button')!;
    expect(row.getAttribute('type')).toBe('button');
    expect(row.querySelector('.t-template-row__tile')?.getAttribute('aria-hidden')).toBe('true');
    expect(row.querySelector('.t-template-row__name')?.textContent).toBe('Morning brief');
    expect(row.querySelector('.t-template-row__description')?.textContent).toBe('News at 8');
    expect(row.querySelector('.t-template-row__when')?.textContent).toBe('Every day');
    row.click();
    await nextTick();
    expect(onPick).toHaveBeenCalledTimes(1);
  });

  it('keeps clear of a peek only when there is one', () => {
    const root = mount(() => [
      h(TTemplateRow, { icon: 'a', name: 'A', description: 'a' }),
      h(TTemplateRow, { icon: 'b', name: 'B', description: 'b' }, { peek: () => h('span', { class: 'card' }) }),
    ]);
    const [plain, peeking] = root.querySelectorAll('button');
    expect(plain!.classList.contains('has-peek')).toBe(false);
    expect(plain!.querySelector('.t-template-row__when')).toBeNull();
    expect(peeking!.classList.contains('has-peek')).toBe(true);
    expect(peeking!.querySelector('.card')).not.toBeNull();
  });
});
