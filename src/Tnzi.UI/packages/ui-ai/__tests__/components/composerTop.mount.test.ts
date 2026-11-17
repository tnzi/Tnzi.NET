/**
 * The place just above the composer that takes room (`TThreadComposer` `#top`,
 * `TChatApp` `#thread-composer-top`), mounted for real:
 *
 * - it renders inside the composer's (sticky) wrapper, before the box, as wide
 *   as the box, and is not there without the slot;
 * - `TChatApp` passes it through to the conversation's composer only, with
 *   `narrow`;
 * - what it shows counts as covering the conversation for the "scroll to the
 *   latest" button, and an empty one does not;
 * - its declared styles keep it in the flow, take no room when empty and
 *   scroll on their own past a height (happy-dom does no layout).
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App, type Component } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;
const originalRect = Element.prototype.getBoundingClientRect;

function mount(component: Component, props: () => Record<string, unknown>, slots?: Record<string, (props?: any) => unknown>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({ setup: () => () => h(component, props(), slots) });
  app = createApp(Root);
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const frame = () => new Promise<void>((done) => requestAnimationFrame(() => done()));

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  Element.prototype.getBoundingClientRect = originalRect;
});

const messages: ChatMessage[] = [
  { id: 'u1', role: 'user', content: 'Delete the old branch', createdAt: '' },
  { id: 'a1', role: 'assistant', content: 'This needs your approval.', createdAt: '' },
];

function chat(slots: Record<string, (props?: any) => unknown>, extra: Record<string, unknown> = {}): HTMLElement {
  return mount(
    TChatApp,
    () => ({ sidebarStorageKey: null, autoApplyTheme: false, activeThreadId: 'A', messages, inputText: '', ...extra }),
    slots,
  );
}

describe('TThreadComposer #top', () => {
  it('renders in the wrapper, before the box, as wide as the box', async () => {
    const el = mount(TThreadComposer, () => ({ modelValue: '', maxWidth: 640 }), {
      top: () => h('div', { class: 'card' }, 'Approve?'),
    });
    await settle();
    const wrap = el.querySelector('.t-thread-composer-wrap')!;
    const top = wrap.querySelector(':scope > .t-thread-composer__top') as HTMLElement;
    expect(top.querySelector('.card')?.textContent).toBe('Approve?');
    expect(top.nextElementSibling?.classList.contains('t-thread-composer')).toBe(true);
    expect(top.style.maxWidth).toBe('640px');
  });

  it('is not rendered without the slot', async () => {
    const el = mount(TThreadComposer, () => ({ modelValue: '' }));
    await settle();
    expect(el.querySelector('.t-thread-composer__top')).toBeNull();
  });
});

describe('TChatApp #thread-composer-top', () => {
  it('reaches the conversation composer with narrow', async () => {
    let narrow: unknown;
    const el = chat({
      'thread-composer-top': (props: { narrow: boolean }) => {
        narrow = props.narrow;
        return h('div', { class: 'card' }, 'Approve?');
      },
    });
    await settle();
    const top = el.querySelector('.t-chat-app__thread .t-thread-composer-wrap.is-sticky > .t-thread-composer__top');
    expect(top?.querySelector('.card')).not.toBeNull();
    expect(narrow).toBe(false);
  });

  it('is not on the landing page', async () => {
    const el = chat({ 'thread-composer-top': () => h('div', { class: 'card' }) }, { activeThreadId: undefined, messages: [] });
    await settle();
    expect(el.querySelector('.t-landing')).not.toBeNull();
    expect(el.querySelector('.card')).toBeNull();
  });

  it('counts as covering the conversation for the scroll-to-latest button, and not when empty', async () => {
    // The last message ends at 350; the cards start at 300, the box at 400.
    Element.prototype.getBoundingClientRect = function (this: Element) {
      const at = (top: number, bottom: number) => ({ top, bottom, left: 0, right: 0, width: 0, height: bottom - top, x: 0, y: top }) as DOMRect;
      if (this.getAttribute('data-message-id') === 'a1') return at(250, 350);
      if (this.classList.contains('t-thread-composer__top')) return at(300, 392);
      if (this.classList.contains('t-thread-composer')) return at(400, 480);
      return at(0, 500);
    };
    const show = ref(true);
    const el = chat({ 'thread-composer-top': () => (show.value ? h('div', { class: 'card' }) : null) });
    await settle();
    const scroller = el.querySelector('.t-chat-app__thread')!;
    scroller.dispatchEvent(new Event('scroll'));
    await frame();
    await settle();
    expect(el.querySelector('.t-chat-app__to-latest')).not.toBeNull();

    show.value = false;
    await settle();
    scroller.dispatchEvent(new Event('scroll'));
    await frame();
    await settle();
    expect(el.querySelector('.t-chat-app__to-latest')).toBeNull();
  });
});

describe('#top declared styles', () => {
  const sfc = readFileSync(resolve(__dirname, '../../src/components/chat/TThreadComposer.vue'), 'utf8');
  const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
  const rule = (selector: string) => {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css)?.[1] ?? '';
  };

  it('stays in the flow, 8px above the box, and scrolls past a height', () => {
    const top = rule('.t-thread-composer__top');
    expect(top).not.toMatch(/position\s*:\s*(?:absolute|fixed)/);
    expect(top).toMatch(/margin-bottom\s*:\s*8px/);
    expect(top).toMatch(/max-height\s*:\s*40dvh/);
    expect(top).toMatch(/overflow-y\s*:\s*auto/);
  });

  it('takes no room when nothing in it rendered', () => {
    expect(rule('.t-thread-composer__top:not(:has(> *))')).toMatch(/display\s*:\s*none/);
  });
});
