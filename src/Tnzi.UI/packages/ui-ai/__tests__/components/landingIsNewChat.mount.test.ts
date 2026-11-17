/**
 * `landingIsNewChat`: the new-chat entry is current on the landing state
 * unless the product says that landing state is something else. The expanded
 * sidebar and the collapsed rail agree, and the entry still starts a new chat.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

const KEY = 'test-landing-is-new-chat';
let app: App | null = null;
let host: HTMLElement | null = null;

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function mount(mode: 'expanded' | 'icon', props: Record<string, unknown> = {}): HTMLElement {
  localStorage.setItem(KEY, mode);
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () => h(TChatApp, { sidebarStorageKey: KEY, autoApplyTheme: false, ...props }),
  });
  app.mount(host);
  return host;
}

function newChatEntry(el: HTMLElement, mode: 'expanded' | 'icon'): HTMLElement {
  const selector =
    mode === 'expanded'
      ? '.t-collapsible-sidebar .t-sidebar-nav__item'
      : '.t-chat-rail__btn[aria-label="New chat"]';
  const entry = Array.from(el.querySelectorAll<HTMLElement>(selector)).find((node) =>
    mode === 'icon' ? true : node.textContent?.includes('New chat'),
  );
  expect(entry, `${mode} new-chat entry`).toBeTruthy();
  return entry!;
}

const isCurrent = (entry: HTMLElement) => entry.getAttribute('aria-current') === 'page';

beforeEach(() => {
  localStorage.clear();
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 });
});
afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe.each(['expanded', 'icon'] as const)('TChatApp landingIsNewChat (%s sidebar)', (mode) => {
  it('by default the new-chat entry is current on the landing state', async () => {
    const el = mount(mode);
    await settle();
    expect(isCurrent(newChatEntry(el, mode))).toBe(true);
  });

  it('is not current there when the product says the landing state is something else', async () => {
    const el = mount(mode, { landingIsNewChat: false });
    await settle();
    expect(isCurrent(newChatEntry(el, mode))).toBe(false);
  });

  it('is not current in a conversation, whatever the prop says', async () => {
    const el = mount(mode, {
      landingIsNewChat: true,
      threads: [{ id: 'a', title: 'Alpha' }],
      activeThreadId: 'a',
      messages: [{ id: 'm1', role: 'user', content: 'hi' }],
    });
    await settle();
    expect(isCurrent(newChatEntry(el, mode))).toBe(false);
  });

  it('still starts a new chat when clicked', async () => {
    let newChats = 0;
    const el = mount(mode, { landingIsNewChat: false, onNewChat: () => (newChats += 1) });
    await settle();
    newChatEntry(el, mode).click();
    await settle();
    expect(newChats).toBe(1);
  });
});
