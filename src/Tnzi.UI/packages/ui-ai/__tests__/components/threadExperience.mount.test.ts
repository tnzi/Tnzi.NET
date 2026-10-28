/**
 * What a user of a chat product sees on every turn, mounted for real:
 *
 * - the byline above an answer can be turned off, and a reply still being
 *   written then says so with a mark of its own;
 * - the action row under an answer is hidden until the turn is hovered or holds
 *   keyboard focus, and always shown where there is no hover (pinned through
 *   the stylesheet: happy-dom has no hover or layout to observe);
 * - every way voice input stops has its own sentence, in the current language;
 * - a conversation picked in the sidebar is highlighted and shown as loading at
 *   once, before its transcript arrives;
 * - a message that is not streaming renders its markdown during mount.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App, type Component } from 'vue';

const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');
const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TStreamMarkdown } = await import('../../src/components/streaming/TStreamMarkdown.vue');
const { createAiI18n } = await import('../../src/i18n/index');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');

import type { ChatMessage } from '../../src/headless/useChat';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(component: Component, props: () => Record<string, unknown>, messages?: typeof en, slots?: Record<string, () => unknown>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      if (messages) createAiI18n(messages);
      return () => h(component, props(), slots);
    },
  });
  app = createApp(Root);
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

const answer = (patch: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 'a1',
  role: 'assistant',
  content: 'The answer.',
  createdAt: '',
  ...patch,
});

describe('TThreadMessage byline', () => {
  it('shows the agent byline by default', async () => {
    const el = mount(TThreadMessage, () => ({ message: answer(), agentName: 'TuanZi' }));
    await settle();
    expect(el.querySelector('.t-thread-message__role')?.textContent).toContain('TuanZi');
  });

  it('starts the answer with its text when the byline is off', async () => {
    const el = mount(TThreadMessage, () => ({ message: answer(), agentName: 'TuanZi', showAgentByline: false }));
    await settle();
    expect(el.querySelector('.t-thread-message__role')).toBeNull();
    expect(el.textContent).not.toContain('TuanZi');
    expect(el.querySelector('.t-thread-message')?.firstElementChild?.classList.contains('t-thread-message__body')).toBe(true);
  });

  it('without a byline, a reply with no words yet shows the pending mark, and the caret takes over once text arrives', async () => {
    const message = ref(answer({ content: '', isStreaming: true, status: 'streaming' }));
    const el = mount(TThreadMessage, () => ({ message: message.value, showAgentByline: false }));
    await settle();

    const pending = el.querySelector('.t-thread-message__pending');
    expect(pending?.getAttribute('role')).toBe('status');
    expect(pending?.getAttribute('aria-label')).toBe(en.chat.generating);
    expect(el.querySelector('.t-thread-message')?.getAttribute('aria-busy')).toBe('true');

    message.value = answer({ content: 'First words', isStreaming: true, status: 'streaming' });
    await settle();
    expect(el.querySelector('.t-thread-message__pending')).toBeNull();
    expect(el.querySelector('.t-thread-message__body')?.classList.contains('ai-streaming')).toBe(true);

    message.value = answer({ content: 'First words.', isStreaming: false, status: 'done' });
    await settle();
    expect(el.querySelector('.t-thread-message')?.hasAttribute('aria-busy')).toBe(false);
  });

  it('shows the pending mark again while the agent works after a tool call', async () => {
    const el = mount(TThreadMessage, () => ({
      message: answer({
        content: 'Let me look.',
        isStreaming: true,
        toolCalls: [{ id: 'c1', name: 'read_file', textOffset: 12, status: 'running' }],
      }),
      showAgentByline: false,
    }));
    await settle();
    expect(el.querySelector('.t-thread-message__pending')).not.toBeNull();
  });

  it('keeps the byline pulse as the only streaming mark when the byline is on', async () => {
    const el = mount(TThreadMessage, () => ({ message: answer({ content: '', isStreaming: true }) }));
    await settle();
    expect(el.querySelector('.t-thread-message__streaming')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__pending')).toBeNull();
  });
});

describe('TThreadMessage action row', () => {
  const source = readFileSync(resolve(import.meta.dirname, '../../src/components/chat/TThreadMessage.vue'), 'utf8');
  const style = source.slice(source.indexOf('<style')).replace(/\/\*[\s\S]*?\*\//g, '');
  const ruleBody = (selector: string) => {
    const at = style.indexOf(`${selector} {`);
    expect(at, `rule for ${selector}`).toBeGreaterThan(-1);
    return style.slice(at, style.indexOf('}', at));
  };

  it('is hidden by opacity (it keeps its space, so revealing it moves nothing)', () => {
    const body = ruleBody('.t-thread-message__actions');
    expect(body).toMatch(/opacity:\s*0/);
    expect(body).not.toMatch(/display:\s*none/);
  });

  it('is revealed by hovering the turn and by keyboard focus inside it', () => {
    expect(ruleBody('.t-thread-message:hover .t-thread-message__actions')).toMatch(/opacity:\s*1/);
    expect(ruleBody('.t-thread-message:has(:focus-visible) .t-thread-message__actions')).toMatch(/opacity:\s*1/);
  });

  it('is always shown on a device without hover', () => {
    const media = style.slice(style.indexOf('@media (hover: none)'));
    expect(media).toMatch(/^@media \(hover: none\) \{\s*\.t-thread-message__actions \{ opacity: 1; \}/);
  });

  it('renders the actions as buttons a keyboard can reach', async () => {
    const el = mount(TThreadMessage, () => ({ message: answer() }));
    await settle();
    const buttons = [...el.querySelectorAll('.t-thread-message__actions button')] as HTMLButtonElement[];
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual([en.chat.copy, en.chat.regenerate, en.chat.like, en.chat.dislike]);
    expect(buttons.every((b) => b.tabIndex >= 0)).toBe(true);
  });
});

describe('TThreadComposer voice notices', () => {
  let recogniser: { onerror: ((e: { error: string }) => void) | null } | null = null;
  class FakeRecognition {
    continuous = false;
    interimResults = false;
    lang = '';
    onstart = null;
    onresult = null;
    onerror: ((e: { error: string }) => void) | null = null;
    onend = null;
    constructor() {
      recogniser = this;
    }
    start(): void {}
    stop(): void {}
  }

  function failVoice(el: HTMLElement, messages: typeof en, error: string): void {
    (el.querySelector(`button[aria-label="${messages.composer.voiceInput}"]`) as HTMLButtonElement).click();
    recogniser!.onerror?.({ error });
  }

  it('tells a blocked microphone how to allow it, in the current language', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    vi.stubGlobal('navigator', { ...globalThis.navigator, permissions: { query: async () => ({ state: 'denied' }) } });
    const el = mount(TThreadComposer, () => ({ modelValue: '', voiceLang: 'zh-CN' }), zhCn);
    await settle();

    failVoice(el, zhCn, 'not-allowed');
    await new Promise((r) => setTimeout(r, 0));
    await settle();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain(zhCn.composer.voiceMicBlocked);
  });

  it.each([
    ['network', 'voiceNetwork'],
    ['audio-capture', 'voiceNoMicrophone'],
    ['no-speech', 'voiceNoSpeech'],
    ['service-not-allowed', 'voiceServiceNotAllowed'],
  ] as const)('says what %s means and what to do', async (error, key) => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const el = mount(TThreadComposer, () => ({ modelValue: '' }), zhCn);
    await settle();

    failVoice(el, zhCn, error);
    await settle();

    const notice = el.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain(zhCn.composer[key]);
    (notice!.querySelector('button') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('does not offer the mic where the page may not use the microphone', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    vi.stubGlobal('isSecureContext', false);
    const el = mount(TThreadComposer, () => ({ modelValue: '' }));
    await settle();
    expect(el.querySelector(`button[aria-label="${en.composer.voiceInput}"]`)).toBeNull();
  });

  it('starts recognition in the language the prop has at the time of the click', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const lang = ref('zh-CN');
    const el = mount(TThreadComposer, () => ({ modelValue: '', voiceLang: lang.value }));
    await settle();
    lang.value = 'en-US';
    await settle();
    (el.querySelector(`button[aria-label="${en.composer.voiceInput}"]`) as HTMLButtonElement).click();
    expect((recogniser as unknown as { lang: string }).lang).toBe('en-US');
  });
});

describe('TThreadComposer sticky spacing', () => {
  it('marks a composer with fine print, which carries the spacing to the edge', async () => {
    const el = mount(TThreadComposer, () => ({ modelValue: '' }), undefined, { footer: () => 'AI can make mistakes.' });
    await settle();
    expect(el.querySelector('.t-thread-composer-wrap')?.classList.contains('has-footer')).toBe(true);
  });
});

describe('TChatApp opening a thread', () => {
  const threads = [
    { id: 'A', title: 'Trip to Kyoto' },
    { id: 'B', title: 'Quarterly numbers' },
  ];
  const transcriptOfA: ChatMessage[] = [
    { id: 'u1', role: 'user', content: 'Plan my trip', createdAt: '' },
    answer({ id: 'a1', content: 'Day one: temples.' }),
  ];

  function shell(
    state: { active?: string; opening?: string; messages: ChatMessage[] },
    extra: Record<string, unknown> = {},
  ) {
    const current = ref(state);
    const el = mount(TChatApp, () => ({
      ...extra,
      sidebarStorageKey: null,
      autoApplyTheme: false,
      threads,
      activeThreadId: current.value.active,
      openingThreadId: current.value.opening,
      messages: current.value.messages,
      inputText: 'draft',
      threadTitle: current.value.active === 'A' ? 'Trip to Kyoto' : undefined,
    }));
    return { el, current };
  }

  const rowTitle = (el: HTMLElement) => el.querySelector('.t-thread-list__row.is-active .t-thread-list__title')?.textContent;

  it('highlights the clicked row and shows it loading in place of the previous transcript', async () => {
    const { el, current } = shell({ active: 'A', messages: transcriptOfA });
    await settle();
    expect(rowTitle(el)).toBe('Trip to Kyoto');

    current.value = { active: 'A', opening: 'B', messages: transcriptOfA };
    await settle();

    expect(rowTitle(el)).toBe('Quarterly numbers');
    expect(el.querySelectorAll('.t-thread-list__row.is-active')).toHaveLength(1);
    expect(el.querySelector('.t-thread-loading')?.getAttribute('role')).toBe('status');
    expect(el.querySelector('.t-thread-message')).toBeNull();
    expect(el.querySelector('.t-chat-app__workspace-switcher')?.textContent?.trim()).toBe('Quarterly numbers');
    // The composer stays, but what is typed waits for the conversation it would go to.
    expect((el.querySelector('.t-thread-composer__send') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the transcript once it is the active thread', async () => {
    const { el, current } = shell({ active: 'A', opening: 'B', messages: transcriptOfA });
    await settle();
    current.value = { active: 'B', messages: [answer({ id: 'b1', content: 'Revenue grew.' })] };
    await settle();

    expect(el.querySelector('.t-thread-loading')).toBeNull();
    expect(el.textContent).toContain('Revenue grew.');
    expect((el.querySelector('.t-thread-composer__send') as HTMLButtonElement).disabled).toBe(false);
  });

  it('shows the loading state instead of the landing page when opened from a fresh start', async () => {
    const { el, current } = shell({ messages: [] });
    await settle();
    expect(el.querySelector('.t-landing')).not.toBeNull();

    current.value = { opening: 'B', messages: [] };
    await settle();
    expect(el.querySelector('.t-landing')).toBeNull();
    expect(el.querySelector('.t-thread-loading')).not.toBeNull();
  });

  // A conversation the product lists in its own nav groups (not in `threads`):
  // the top bar asks the product for its title instead of showing the brand.
  it('names a conversation listed outside `threads` through `threadTitleOf`', async () => {
    const titles: Record<string, string> = { 'pass-1': 'Cleaner pass chat' };
    const { el, current } = shell(
      { active: 'A', messages: transcriptOfA },
      { brandName: 'TuanZi', threadTitleOf: (id: string) => titles[id] },
    );
    await settle();

    current.value = { active: 'A', opening: 'pass-1', messages: transcriptOfA };
    await settle();
    expect(el.querySelector('.t-chat-app__workspace-switcher')?.textContent?.trim()).toBe('Cleaner pass chat');

    // A conversation the lookup does not know is still found in `threads`.
    current.value = { active: 'A', opening: 'B', messages: transcriptOfA };
    await settle();
    expect(el.querySelector('.t-chat-app__workspace-switcher')?.textContent?.trim()).toBe('Quarterly numbers');
  });

  it('falls back to the brand name for a conversation nobody can name', async () => {
    const { el, current } = shell({ active: 'A', messages: transcriptOfA }, { brandName: 'TuanZi' });
    await settle();
    current.value = { active: 'A', opening: 'pass-1', messages: transcriptOfA };
    await settle();
    expect(el.querySelector('.t-chat-app__workspace-switcher')?.textContent?.trim()).toBe('TuanZi');
  });
});

describe('TStreamMarkdown', () => {
  it('renders a message that is not streaming during mount, not a frame later', () => {
    const el = mount(TStreamMarkdown, () => ({ content: 'Hello **world**' }));
    expect(el.querySelector('.t-stream-markdown')?.innerHTML).toContain('<strong>world</strong>');
  });
});
