/**
 * The open conversation's title in the top bar (`TThreadTitle`), and when
 * `TChatApp` offers it:
 *
 * - clicking the title edits it in place, whole title selected; Enter or blur
 *   saves, Escape cancels, an empty or unchanged title is not sent;
 * - the field is as wide as its text and follows it while typing, composition
 *   included; its minimum and maximum are in its styles (no fixed long box);
 * - the chevron menu has Rename / Archive or Unarchive / Delete, Delete asking
 *   in the bar first;
 * - `TChatApp` shows it only for a saved, open conversation with a `recents`
 *   controller, renames through `recents.rename`, and shows the new title at once.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, ref, shallowRef, type App } from 'vue';
import type { RecentThreadsController } from '../../src/headless/recents';
import { DEFAULT_RECENTS_PREFERENCES } from '../../src/headless/recents';

const { default: TThreadTitle } = await import('../../src/components/chat/TThreadTitle.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => ReturnType<typeof h>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const menu = () => document.body.querySelector('.t-action-menu') as HTMLElement | null;
const key = (el: Element, name: string) => el.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  localStorage.clear();
});

function mountTitle(props: Record<string, unknown> = {}) {
  const handlers = { onRename: vi.fn(), onArchive: vi.fn(), onUnarchive: vi.fn(), onDelete: vi.fn() };
  const el = mount(() => h(TThreadTitle, { title: 'Trip plan', ...handlers, ...props }));
  return { el, ...handlers };
}

describe('TThreadTitle', () => {
  it('clicking the title edits it in place with the whole title selected; Enter saves trimmed', async () => {
    const { el, onRename } = mountTitle();
    (el.querySelector('.t-thread-title__text') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-title__input') as HTMLInputElement;
    expect(document.activeElement).toBe(input);
    expect(input.value).toBe('Trip plan');
    expect([input.selectionStart, input.selectionEnd]).toEqual([0, 'Trip plan'.length]);

    input.value = '  Trip to Kyoto  ';
    input.dispatchEvent(new Event('input'));
    key(input, 'Enter');
    await settle();
    expect(onRename).toHaveBeenCalledWith('Trip to Kyoto');
    expect(el.querySelector('.t-thread-title__input')).toBeNull();
    expect(document.activeElement).toBe(el.querySelector('.t-thread-title__text'));
  });

  it('Escape cancels; an empty or unchanged title is not sent; leaving the field saves', async () => {
    const { el, onRename } = mountTitle();
    const edit = async (value: string, end: 'Escape' | 'Enter' | 'blur') => {
      (el.querySelector('.t-thread-title__text') as HTMLButtonElement).click();
      await settle();
      const input = el.querySelector('.t-thread-title__input') as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      if (end === 'blur') input.dispatchEvent(new Event('blur'));
      else key(input, end);
      await settle();
    };
    await edit('Changed', 'Escape');
    await edit('   ', 'Enter');
    await edit('Trip plan', 'Enter');
    expect(onRename).not.toHaveBeenCalled();
    await edit('Blurred', 'blur');
    expect(onRename).toHaveBeenCalledWith('Blurred');
  });

  it('the chevron menu renames, archives, unarchives, and deletes after confirming in the bar', async () => {
    const { el, onArchive, onDelete } = mountTitle();
    const openMenu = async () => {
      (el.querySelector('.t-thread-title__chevron') as HTMLButtonElement).click();
      await settle();
    };

    await openMenu();
    expect(Array.from(menu()!.querySelectorAll('[data-menu-item]')).map((i) => i.textContent?.trim())).toEqual([
      'Rename',
      'Archive',
      'Delete',
    ]);
    (menu()!.querySelector('[data-menu-item="archive"]') as HTMLButtonElement).click();
    await settle();
    expect(onArchive).toHaveBeenCalledTimes(1);

    await openMenu();
    (menu()!.querySelector('[data-menu-item="rename"]') as HTMLButtonElement).click();
    await settle();
    expect(document.activeElement).toBe(el.querySelector('.t-thread-title__input'));
    key(el.querySelector('.t-thread-title__input')!, 'Escape');
    await settle();

    await openMenu();
    (menu()!.querySelector('[data-menu-item="delete"]') as HTMLButtonElement).click();
    await settle();
    expect(onDelete).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Delete this conversation?');
    /* Focus lands on Cancel, the safe answer; Escape backs out too. */
    const cancel = el.querySelectorAll('.t-thread-title__confirm-btn')[1] as HTMLButtonElement;
    expect(document.activeElement).toBe(cancel);
    key(cancel, 'Escape');
    await settle();
    expect(el.querySelector('.t-thread-title__confirm')).toBeNull();

    await openMenu();
    (menu()!.querySelector('[data-menu-item="delete"]') as HTMLButtonElement).click();
    await settle();
    (el.querySelector('.t-thread-title__confirm-btn--danger') as HTMLButtonElement).click();
    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it('an archived conversation offers Unarchive', async () => {
    const { el, onUnarchive } = mountTitle({ archived: true });
    (el.querySelector('.t-thread-title__chevron') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="unarchive"]') as HTMLButtonElement).click();
    expect(onUnarchive).toHaveBeenCalledTimes(1);
  });
});

function controller(): RecentThreadsController {
  return {
    preferences: ref(DEFAULT_RECENTS_PREFERENCES),
    setPreferences: vi.fn(async () => {}),
    rename: vi.fn(async () => true),
    archive: vi.fn(async () => true),
    unarchive: vi.fn(async () => true),
    page: {
      threads: shallowRef([]),
      status: ref('active' as const),
      keyword: ref(''),
      hasMore: ref(false),
      loading: ref(false),
      loadingMore: ref(false),
      loaded: ref(false),
      load: vi.fn(async () => {}),
      loadMore: vi.fn(async () => {}),
      setStatus: vi.fn(async () => {}),
      search: vi.fn(async () => {}),
    },
  };
}

describe('TChatApp top-bar title', () => {
  const conversation = {
    threads: [{ id: 't1', title: 'Trip plan' }],
    activeThreadId: 't1',
    threadTitle: 'Trip plan',
    messages: [{ id: 'm', role: 'user', content: 'hi' }],
  };

  function mountShell(props: Record<string, unknown>) {
    const onDelete = vi.fn();
    const el = mount(() =>
      h(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, 'onDelete-thread': onDelete, ...props }),
    );
    return { el, onDelete };
  }

  it('is editable for a saved conversation with a recents controller, and renames through it', async () => {
    const recents = controller();
    const { el } = mountShell({ ...conversation, recents });
    await settle();
    (el.querySelector('.t-chat-app__topbar .t-thread-title__text') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-title__input') as HTMLInputElement;
    input.value = 'Kyoto';
    input.dispatchEvent(new Event('input'));
    key(input, 'Enter');
    await settle();
    expect(recents.rename).toHaveBeenCalledWith('t1', 'Kyoto');
    /* Shown at once, before the consumer's own title catches up. */
    expect(el.querySelector('.t-thread-title__text')?.textContent).toBe('Kyoto');
  });

  it('deleting from the bar goes out as delete-thread for the open conversation', async () => {
    const { el, onDelete } = mountShell({ ...conversation, recents: controller() });
    await settle();
    (el.querySelector('.t-thread-title__chevron') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="delete"]') as HTMLButtonElement).click();
    await settle();
    (el.querySelector('.t-thread-title__confirm-btn--danger') as HTMLButtonElement).click();
    expect(onDelete).toHaveBeenCalledWith('t1');
  });

  it('archiving from the bar shows Unarchive next time, even once the conversation left the list', async () => {
    const recents = controller();
    const { el } = mountShell({ ...conversation, recents });
    await settle();
    (el.querySelector('.t-thread-title__chevron') as HTMLButtonElement).click();
    await settle();
    (menu()!.querySelector('[data-menu-item="archive"]') as HTMLButtonElement).click();
    await settle();
    expect(recents.archive).toHaveBeenCalledWith('t1');
    (el.querySelector('.t-thread-title__chevron') as HTMLButtonElement).click();
    await settle();
    expect(menu()!.querySelector('[data-menu-item="unarchive"]')).not.toBeNull();
  });

  it.each([
    ['without a recents controller', { ...conversation }],
    ['on the new-chat landing page', { recents: 'controller', threads: [] }],
    ['while another conversation opens', { ...conversation, recents: 'controller', openingThreadId: 't2' }],
  ])('is plain text %s', async (_name, props) => {
    const resolved = props.recents === 'controller' ? { ...props, recents: controller() } : props;
    const { el } = mountShell(resolved as Record<string, unknown>);
    await settle();
    expect(el.querySelector('.t-thread-title')).toBeNull();
  });
});

describe('TThreadTitle field width', () => {
  /* happy-dom has no canvas text metrics: each character measures 8px here. */
  function stubMeasure() {
    const context = { font: '', measureText: (text: string) => ({ width: text.length * 8 }) };
    return vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never);
  }
  afterEach(() => vi.restoreAllMocks());

  // Text + 16px padding and border + 2px for the caret.
  const widthFor = (chars: number) => `${chars * 8 + 18}px`;

  it('opens at the width of the title', async () => {
    stubMeasure();
    const { el } = mountTitle();
    (el.querySelector('.t-thread-title__text') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-title__input') as HTMLInputElement;
    expect(input.style.width).toBe(widthFor('Trip plan'.length));
  });

  it('grows and shrinks with the text, while an IME is composing too', async () => {
    stubMeasure();
    const { el } = mountTitle();
    (el.querySelector('.t-thread-title__text') as HTMLButtonElement).click();
    await settle();
    const input = el.querySelector('.t-thread-title__input') as HTMLInputElement;

    input.value = 'Trip plan for the spring';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(input.style.width).toBe(widthFor('Trip plan for the spring'.length));

    input.value = 'Tr';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(input.style.width).toBe(widthFor(2));

    // v-model waits for compositionend; the width does not.
    input.dispatchEvent(new CompositionEvent('compositionstart'));
    input.value = 'Tr京都旅行';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(input.style.width).toBe(widthFor('Tr京都旅行'.length));
  });

  it('has a minimum and never overflows the top bar (styles)', () => {
    const sfc = readFileSync(resolve(import.meta.dirname, '../../src/components/chat/TThreadTitle.vue'), 'utf8');
    const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
    const field = /\.t-thread-title__input\s*\{([^}]*)\}(?![\s\S]*\.t-thread-title__input\s*\{)/.exec(css)?.[1] ?? '';
    expect(field).toMatch(/min-width:\s*64px/);
    const appSfc = readFileSync(resolve(import.meta.dirname, '../../src/components/chat/TChatApp.vue'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
    // The title takes the bar's free room only (basis 0), never room from the controls beside it.
    expect(/\.t-chat-app__thread-title\s*\{([^}]*)\}/.exec(appSfc)?.[1]).toMatch(/flex:\s*1000 1 0%/);
    expect(field).toMatch(/max-width:\s*100%/);
    expect(field).toMatch(/flex:\s*0 1 auto/);
    expect(css).not.toMatch(/480px/);
  });
});
