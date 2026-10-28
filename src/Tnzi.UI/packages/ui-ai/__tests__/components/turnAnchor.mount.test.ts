/**
 * `TChatApp` and a turn being sent, mounted for real (happy-dom has no layout,
 * so what is observed is which turns get anchored, not pixels):
 *
 * - a new user message at the end of the conversation on screen is anchored:
 *   the reserve under it appears, so it can be brought to the top while its
 *   reply is short;
 * - opening another conversation, or a transcript arriving at once, is not a
 *   turn being sent: no reserve, and an anchor in place is released;
 * - each message is wrapped with its id, which is how the anchor finds it;
 * - the edit button's row keeps its place while a reply streams (it used to be
 *   removed and put back, moving the whole transcript twice per turn).
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');

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
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const user = (id: string, content = 'A question'): ChatMessage => ({ id, role: 'user', content, createdAt: '' });
const reply = (id: string, patch: Partial<ChatMessage> = {}): ChatMessage => ({ id, role: 'assistant', content: 'An answer.', createdAt: '', ...patch });

interface State {
  active?: string;
  opening?: string;
  messages: ChatMessage[];
  streaming?: boolean;
}

function shell(initial: State) {
  const state = ref<State>(initial);
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(
    defineComponent({
      setup() {
        return () =>
          h(TChatApp, {
            sidebarStorageKey: null,
            autoApplyTheme: false,
            activeThreadId: state.value.active,
            openingThreadId: state.value.opening,
            messages: state.value.messages,
            isStreaming: state.value.streaming ?? false,
            enableEdit: true,
          });
      },
    }),
  );
  app.mount(host);
  return { el: host, state };
}

const reserve = (el: HTMLElement) => el.querySelector('.t-chat-app__turn-reserve');

describe('TChatApp anchoring a sent turn', () => {
  const history = [user('u1'), reply('a1'), user('u2'), reply('a2')];

  it('wraps every message with its id', async () => {
    const { el } = shell({ active: 'A', messages: history });
    await settle();
    expect([...el.querySelectorAll('[data-message-id]')].map((m) => m.getAttribute('data-message-id'))).toEqual(['u1', 'a1', 'u2', 'a2']);
  });

  it('a conversation as it opens is not anchored', async () => {
    const { el } = shell({ active: 'A', messages: history });
    await settle();
    expect(reserve(el)).toBeNull();
  });

  it('a message sent in the open conversation is anchored', async () => {
    const { el, state } = shell({ active: 'A', messages: history });
    await settle();
    state.value = { active: 'A', messages: [...history, user('u3'), reply('a3', { isStreaming: true })], streaming: true };
    await settle();
    expect(reserve(el)).not.toBeNull();
  });

  it('the first message of a new conversation is anchored, and its id arriving keeps it', async () => {
    const { el, state } = shell({ messages: [] });
    await settle();
    state.value = { messages: [user('u1'), reply('a1', { isStreaming: true })], streaming: true };
    await settle();
    expect(reserve(el)).not.toBeNull();
    state.value = { active: 'NEW', messages: [user('u1'), reply('a1')] };
    await settle();
    expect(reserve(el)).not.toBeNull();
  });

  it('opening another conversation releases the anchor', async () => {
    const { el, state } = shell({ active: 'A', messages: history });
    await settle();
    state.value = { active: 'A', messages: [...history, user('u3'), reply('a3')] };
    await settle();
    expect(reserve(el)).not.toBeNull();
    state.value = { active: 'B', messages: [user('b1'), reply('b2')] };
    await settle();
    expect(reserve(el)).toBeNull();
  });

  it('a transcript arriving at once (more than one turn) is not anchored', async () => {
    const { el, state } = shell({ active: 'A', messages: [user('u1'), reply('a1')] });
    await settle();
    state.value = { active: 'A', messages: [...history, user('u3'), reply('a3')] };
    await settle();
    expect(reserve(el)).toBeNull();
  });
});

describe('TChatApp top fade', () => {
  it('shows once the thread is scrolled, and not at the top', async () => {
    const { el } = shell({ active: 'A', messages: [user('u1'), reply('a1')] });
    await settle();
    const fade = el.querySelector('.t-chat-app__top-fade')!;
    const scroller = el.querySelector('.t-chat-app__thread') as HTMLElement;
    expect(fade.getAttribute('aria-hidden')).toBe('true');
    expect(fade.classList.contains('is-visible')).toBe(false);

    Object.defineProperty(scroller, 'scrollTop', { value: 120, configurable: true, writable: true });
    scroller.dispatchEvent(new Event('scroll'));
    await settle();
    expect(fade.classList.contains('is-visible')).toBe(true);

    Object.defineProperty(scroller, 'scrollTop', { value: 0, configurable: true, writable: true });
    scroller.dispatchEvent(new Event('scroll'));
    await settle();
    expect(fade.classList.contains('is-visible')).toBe(false);
  });
});

describe('TThreadMessage edit row while a reply streams', () => {
  it('keeps the row and disables the button', async () => {
    const disabled = ref(false);
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp(
      defineComponent({
        setup: () => () => h(TThreadMessage, { message: user('u1'), enableEdit: true, editDisabled: disabled.value }),
      }),
    );
    app.mount(host);
    await settle();
    const row = () => host!.querySelector('.t-thread-message__user-actions');
    expect(row()).not.toBeNull();
    expect((row()!.querySelector('button') as HTMLButtonElement).disabled).toBe(false);

    disabled.value = true;
    await settle();
    expect(row()).not.toBeNull();
    expect(row()!.classList.contains('is-disabled')).toBe(true);
    expect((row()!.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
  });
});
