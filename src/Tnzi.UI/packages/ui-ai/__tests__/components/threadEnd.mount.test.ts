/**
 * `TChatApp` `#thread-end`: the app's content after the last message, mounted
 * for real:
 *
 * - it renders in the scrolling thread column, after the last message and
 *   before the composer, with `narrow`;
 * - not while a conversation is being opened, and not on the landing page;
 * - without the slot the thread has no wrapper for it;
 * - for the "scroll to the latest" button it is the conversation's end: hidden
 *   below the composer, the button is up even with the last message in view;
 *   an empty one does not count.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;
const originalRect = Element.prototype.getBoundingClientRect;

const messages: ChatMessage[] = [
  { id: 'u1', role: 'user', content: 'Merge it', createdAt: '' },
  { id: 'a1', role: 'assistant', content: 'Merged.', createdAt: '' },
];

function chat(slots: Record<string, (props?: any) => unknown>, extra: Record<string, unknown> = {}): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const props = { sidebarStorageKey: null, autoApplyTheme: false, activeThreadId: 'A', messages, inputText: '', ...extra };
  app = createApp(defineComponent({ setup: () => () => h(TChatApp, props, slots) }));
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

const ended = () => h('div', { class: 't-wave-divider ended' }, [h('span', 'Merged, the session has ended')]);

describe('TChatApp #thread-end', () => {
  it('renders after the last message and before the composer, in the thread column, with narrow', async () => {
    let narrow: unknown;
    const el = chat({
      'thread-end': (props: { narrow: boolean }) => {
        narrow = props.narrow;
        return ended();
      },
    });
    await settle();
    const end = el.querySelector('.t-chat-app__thread > .t-chat-app__thread-col > .t-chat-app__thread-end');
    expect(end?.querySelector('.ended')?.textContent).toBe('Merged, the session has ended');
    expect(end?.previousElementSibling?.getAttribute('data-message-id')).toBe('a1');
    expect(end?.nextElementSibling?.classList.contains('t-thread-composer-wrap')).toBe(true);
    expect(narrow).toBe(false);
  });

  it('is not there without the slot', async () => {
    const el = chat({});
    await settle();
    expect(el.querySelector('.t-chat-app__thread-end')).toBeNull();
    const last = el.querySelector('[data-message-id="a1"]');
    expect(last?.nextElementSibling?.classList.contains('t-thread-composer-wrap')).toBe(true);
  });

  it('is not rendered while the conversation is being opened', async () => {
    const el = chat({ 'thread-end': ended }, { openingThreadId: 'B' });
    await settle();
    expect(el.querySelector('.t-thread-loading')).not.toBeNull();
    expect(el.querySelector('.ended')).toBeNull();
  });

  it('is not on the landing page', async () => {
    const el = chat({ 'thread-end': ended }, { activeThreadId: undefined, messages: [] });
    await settle();
    expect(el.querySelector('.t-landing')).not.toBeNull();
    expect(el.querySelector('.ended')).toBeNull();
  });

  it('is the end of the conversation for the scroll-to-latest button, and not when empty', async () => {
    // The last message ends at 350, above the box at 400; the end mark ends at 420, under it.
    Element.prototype.getBoundingClientRect = function (this: Element) {
      const at = (top: number, bottom: number) => ({ top, bottom, left: 0, right: 0, width: 0, height: bottom - top, x: 0, y: top }) as DOMRect;
      if (this.getAttribute('data-message-id') === 'a1') return at(250, 350);
      if (this.classList.contains('t-chat-app__thread-end')) return at(350, 420);
      if (this.classList.contains('t-thread-composer')) return at(400, 480);
      return at(0, 500);
    };
    const show = ref(true);
    const el = chat({ 'thread-end': () => (show.value ? ended() : null) });
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
