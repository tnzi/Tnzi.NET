/**
 * `TChatApp` and a turn being sent, mounted for real. happy-dom has no layout,
 * so the scroller and the messages are given one: each message is 100px tall,
 * stacked from the top, then 200px of composer; the view is 500px.
 *
 * - `scrollMode: 'chat'` (default): a sent message comes into view with what
 *   came before it above, and the answer is followed no further than that
 *   message reaching the top;
 * - `scrollMode: 'follow'`: a sent message goes to the bottom and the answer is
 *   followed to its end;
 * - opening another conversation lands at its bottom in both; a transcript
 *   arriving at once is not a send;
 * - each message is wrapped with its id, which is how the sent one is found;
 * - the edit button's row keeps its place while a reply streams (it used to be
 *   removed and put back, moving the whole transcript twice per turn).
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';
import type { ChatScrollMode } from '../../src/headless/useAutoScroll';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');

let app: App | null = null;
let host: HTMLElement | null = null;
const originalRect = Element.prototype.getBoundingClientRect;
const restores: Array<() => void> = [];

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  Element.prototype.getBoundingClientRect = originalRect;
  restores.splice(0).reverse().forEach((restore) => restore());
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

const VIEW = 500;
const COMPOSER = 200;

/** Message heights by id (default 100). */
let heights: Record<string, number> = {};
let scroller: HTMLElement | null = null;
let top = 0;

function messageTops(el: HTMLElement): Map<string, number> {
  const tops = new Map<string, number>();
  let y = 0;
  for (const m of el.querySelectorAll('[data-message-id]')) {
    const id = m.getAttribute('data-message-id')!;
    tops.set(id, y);
    y += heights[id] ?? 100;
  }
  tops.set('__end', y);
  return tops;
}

function contentHeight(el: HTMLElement): number {
  return messageTops(el).get('__end')! + COMPOSER;
}

/** Give the thread scroller (once it exists) and its messages a layout. */
function giveLayout(root: HTMLElement): void {
  Element.prototype.getBoundingClientRect = function (this: Element) {
    if (scroller && this === scroller) return { top: 0, bottom: VIEW } as DOMRect;
    const id = this.getAttribute?.('data-message-id');
    if (scroller && id) {
      const y = messageTops(scroller).get(id) ?? 0;
      return { top: y - top, bottom: y - top + (heights[id] ?? 100) } as DOMRect;
    }
    return originalRect.call(this);
  };
  /* On the prototype rather than on the element once it appears: the shell
     pins its scroller in a post-flush watch during the mount itself, before a
     MutationObserver here would have run. */
  const isThread = (el: Element) => root.contains(el) && el.classList.contains('t-chat-app__thread');
  const own = (el: Element) => {
    if (el !== scroller) scroller = el as HTMLElement;
    return el as HTMLElement;
  };
  patch('scrollHeight', function (this: Element) { return contentHeight(own(this)); });
  patch('clientHeight', function (this: Element) { return (own(this), VIEW); });
  patch('scrollTop', function (this: Element) { return (own(this), top); }, function (this: Element, v: number) {
    top = Math.max(0, Math.min(v, contentHeight(own(this)) - VIEW));
  });
  patchValue('scrollTo', function (this: Element, o: ScrollToOptions) { (this as HTMLElement).scrollTop = o.top ?? top; });

  function descriptorOf(name: string): { owner: object; descriptor: PropertyDescriptor } {
    let owner: object | null = HTMLElement.prototype;
    while (owner) {
      const descriptor = Object.getOwnPropertyDescriptor(owner, name);
      if (descriptor) return { owner, descriptor };
      owner = Object.getPrototypeOf(owner);
    }
    throw new Error(`no ${name} on HTMLElement`);
  }
  function patch(name: string, get: (this: Element) => number, set?: (this: Element, v: number) => void): void {
    const { owner, descriptor } = descriptorOf(name);
    restores.push(() => Object.defineProperty(owner, name, descriptor));
    Object.defineProperty(owner, name, {
      configurable: true,
      get(this: Element) { return isThread(this) ? get.call(this) : descriptor.get!.call(this); },
      set(this: Element, v: number) {
        if (isThread(this) && set) set.call(this, v);
        else descriptor.set?.call(this, v);
      },
    });
  }
  function patchValue(name: string, value: (this: Element, o: ScrollToOptions) => void): void {
    const { owner, descriptor } = descriptorOf(name);
    restores.push(() => Object.defineProperty(owner, name, descriptor));
    Object.defineProperty(owner, name, {
      configurable: true,
      writable: true,
      value(this: Element, ...args: unknown[]) {
        if (isThread(this)) return value.call(this, args[0] as ScrollToOptions);
        return (descriptor.value as (...a: unknown[]) => unknown).apply(this, args);
      },
    });
  }
}

function shell(initial: State, scrollMode?: ChatScrollMode) {
  const state = ref<State>(initial);
  heights = {};
  scroller = null;
  top = 0;
  host = document.createElement('div');
  document.body.appendChild(host);
  giveLayout(host);
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
            scrollMode,
          });
      },
    }),
  );
  app.mount(host);
  return { el: host, state, scrollTop: () => top };
}

/** The answer `id` grows to `height` px (markdown renders asynchronously, so
 *  the streamed DOM is stood in for by a node added to the message). */
async function stream(id: string, height: number): Promise<void> {
  heights[id] = height;
  scroller!.querySelector(`[data-message-id="${id}"]`)!.appendChild(document.createElement('p'));
  await settle();
}

/** The view's bottom for the current layout. */
const bottom = () => (scroller ? contentHeight(scroller) - VIEW : 0);

describe('TChatApp scrolling a sent turn', () => {
  const history = [user('u1'), reply('a1'), user('u2'), reply('a2'), user('u3'), reply('a3')];

  it('wraps every message with its id', async () => {
    const { el } = shell({ active: 'A', messages: history.slice(0, 4) });
    await settle();
    expect([...el.querySelectorAll('[data-message-id]')].map((m) => m.getAttribute('data-message-id'))).toEqual(['u1', 'a1', 'u2', 'a2']);
  });

  it('a conversation opens at its bottom', async () => {
    const { scrollTop } = shell({ active: 'A', messages: history });
    await settle();
    expect(scrollTop()).toBe(bottom());
  });

  it('chat: the sent message comes into view with the previous answer above it', async () => {
    const { state, scrollTop } = shell({ active: 'A', messages: history });
    await settle();
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4', { isStreaming: true })], streaming: true };
    await settle();
    // u4 at 600..700, a4 at 700..800, composer to 1000: the bottom (500)
    // comes before u4 reaching the top (600 - 24), so a3 is still in view.
    expect(scrollTop()).toBe(500);
  });

  it('chat: a long answer is followed until the sent message reaches the top, then stays', async () => {
    const { state, scrollTop } = shell({ active: 'A', messages: history });
    await settle();
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4', { isStreaming: true })], streaming: true };
    await settle();
    await stream('a4', 150);
    expect(scrollTop()).toBe(550);
    await stream('a4', 2000);
    expect(scrollTop()).toBe(600 - 24);
  });

  it('follow: the answer is followed to its end', async () => {
    const { state, scrollTop } = shell({ active: 'A', messages: history }, 'follow');
    await settle();
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4', { isStreaming: true })], streaming: true };
    await settle();
    expect(scrollTop()).toBe(bottom());
    await stream('a4', 2000);
    expect(scrollTop()).toBe(bottom());
    expect(scrollTop()).toBe(600 + 100 + 2000 + COMPOSER - VIEW);
  });

  it('the first message of a new conversation stays at the top, and its id arriving keeps it so', async () => {
    const { state, scrollTop } = shell({ messages: [] });
    await settle();
    state.value = { messages: [user('u1'), reply('a1', { isStreaming: true })], streaming: true };
    await settle();
    state.value = { active: 'NEW', messages: [user('u1'), reply('a1', { isStreaming: true })], streaming: true };
    await settle();
    await stream('a1', 2000);
    expect(scrollTop()).toBe(0);
  });

  it('opening another conversation lands at its bottom, whichever mode', async () => {
    for (const mode of ['chat', 'follow'] as const) {
      const { state, scrollTop } = shell({ active: 'A', messages: history }, mode);
      await settle();
      const other = [user('b1'), reply('b2'), user('b3'), reply('b4'), user('b5'), reply('b6'), user('b7'), reply('b8')];
      state.value = { active: 'B', messages: other };
      await settle();
      expect(scrollTop()).toBe(bottom());
      app?.unmount();
      host?.remove();
    }
  });

  it('a transcript arriving at once (more than one turn) is not a send', async () => {
    const { state, scrollTop } = shell({ active: 'A', messages: history });
    await settle();
    scroller!.dispatchEvent(new WheelEvent('wheel', { deltaY: -100 }));
    scroller!.scrollTop = 100;
    scroller!.dispatchEvent(new Event('scroll'));
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4'), user('u5'), reply('a5')] };
    await settle();
    expect(scrollTop()).toBe(100);
  });

  it('scrolling up while an answer streams stops following, in follow mode too', async () => {
    const { state, scrollTop } = shell({ active: 'A', messages: history }, 'follow');
    await settle();
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4', { isStreaming: true })], streaming: true };
    await settle();
    await stream('a4', 1000);
    expect(scrollTop()).toBe(bottom());
    scroller!.dispatchEvent(new WheelEvent('wheel', { deltaY: -100 }));
    scroller!.scrollTop = 900;
    scroller!.dispatchEvent(new Event('scroll'));
    await stream('a4', 2000);
    expect(scrollTop()).toBe(900);
  });

  it('the jump button goes to the bottom and follows from there', async () => {
    const { el, state, scrollTop } = shell({ active: 'A', messages: history }, 'chat');
    await settle();
    state.value = { active: 'A', messages: [...history, user('u4'), reply('a4', { isStreaming: true })], streaming: true };
    await settle();
    await stream('a4', 2000);
    expect(scrollTop()).toBe(576);
    const button = el.querySelector('.t-chat-app__to-latest') as HTMLButtonElement | null;
    expect(button).not.toBeNull();
    button!.click();
    await settle();
    expect(scrollTop()).toBe(bottom());
    // The smooth scroll lands (instantly here); following goes on from there.
    scroller!.dispatchEvent(new Event('scrollend'));
    await stream('a4', 2400);
    expect(scrollTop()).toBe(bottom());
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
