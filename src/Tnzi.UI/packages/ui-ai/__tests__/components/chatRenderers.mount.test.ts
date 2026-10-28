/**
 * The message renderers and their parts, mounted for real.
 *
 * Each case is something only a rendered component shows: a list that drew
 * tool cards twice, a running tool drawn as queued, a stored rating nobody
 * displayed, three icon mappings for one file, and a refused microphone that
 * said nothing.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

// Icons render as an element carrying their name, so a test can read which
// glyph a component chose.
vi.mock('@iconify/vue', async (importOriginal) => {
  const original = await importOriginal<typeof import('@iconify/vue')>();
  const { defineComponent, h: render } = await import('vue');
  return {
    ...original,
    Icon: defineComponent({
      props: { icon: { type: String, default: '' } },
      setup: (props) => () => render('i', { 'data-icon': props.icon }),
    }),
  };
});

const { default: TMessageList } = await import('../../src/components/chat/TMessageList.vue');
const { default: TChatMessage } = await import('../../src/components/chat/TChatMessage.vue');
const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');
const { default: TToolCallDisplay } = await import('../../src/components/reasoning/TToolCallDisplay.vue');
const { default: TMessageAttachments } = await import('../../src/components/chat/TMessageAttachments.vue');
const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TPromptInput } = await import('../../src/components/chat/TPromptInput.vue');
const { en } = await import('../../src/locales/en');

import type { ChatMessage } from '../../src/headless/useChat';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
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

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

const assistant = (patch: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 'a1',
  role: 'assistant',
  content: 'answer',
  createdAt: '2026-01-01T00:00:00Z',
  ...patch,
});

describe('TMessageList', () => {
  it('draws each tool call once, even when one tool is called twice', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const el = mount(() =>
      h(TMessageList, {
        messages: [assistant({ toolCalls: [{ name: 'web_search' }, { name: 'web_search' }] })],
      }),
    );
    await settle();

    // The classic list draws the same cards as the thread layout (`TToolCall`).
    expect(el.querySelectorAll('.t-tool-card')).toHaveLength(2);
    const duplicateKeys = warn.mock.calls.filter((call) => String(call[0]).includes('Duplicate keys'));
    expect(duplicateKeys).toEqual([]);
    warn.mockRestore();
  });
});

describe('TToolCallDisplay', () => {
  it('shows a call with no result yet as running while its message streams', () => {
    const el = mount(() => h(TToolCallDisplay, { toolCall: { name: 'fetch_page' }, streaming: true }));
    expect(el.querySelector('[role="status"]')).not.toBeNull();
  });

  it('shows the same call as waiting once the stream has ended without a result', () => {
    const el = mount(() => h(TToolCallDisplay, { toolCall: { name: 'fetch_page' } }));
    expect(el.querySelector('[role="status"]')).toBeNull();
    expect(el.querySelector('[data-icon="lucide:clock"]')).not.toBeNull();
  });

  it('does not spin for a call that reports how long it took', () => {
    const el = mount(() => h(TToolCallDisplay, { toolCall: { name: 'fetch_page', durationMs: 900 } }));
    expect(el.querySelector('[role="status"]')).toBeNull();
    expect(el.querySelector('[data-icon="lucide:check-circle-2"]')).not.toBeNull();
  });

  it('trusts a reported running status over the default success flag', () => {
    const running = { name: 'take_screenshot', status: 'running', isSuccess: true, durationMs: 0 };
    const live = mount(() => h(TToolCallDisplay, { toolCall: running, streaming: true }));
    expect(live.querySelector('[role="status"]')).not.toBeNull();
    app?.unmount();
    host?.remove();

    const ended = mount(() => h(TToolCallDisplay, { toolCall: running }));
    expect(ended.querySelector('[data-icon="lucide:check-circle-2"]')).toBeNull();
    expect(ended.querySelector('[data-icon="lucide:clock"]')).not.toBeNull();
  });

  it('shows a reported failure as failed', () => {
    const el = mount(() =>
      h(TToolCallDisplay, { toolCall: { name: 'relay_command', status: 'failed', isSuccess: true } }),
    );
    expect(el.querySelector('[data-icon="lucide:x-circle"]')).not.toBeNull();
  });

  it('is told by the thread renderer that its message is streaming', () => {
    const el = mount(() =>
      h(TThreadMessage, {
        message: assistant({ isStreaming: true, content: '', toolCalls: [{ name: 'run_command' }] }),
      }),
    );
    expect(el.querySelector('.t-tool-card [role="status"]')).not.toBeNull();
  });
});

describe('stored feedback', () => {
  const pressed = (el: HTMLElement, label: string) =>
    el.querySelector(`button[aria-label="${label}"]`)?.getAttribute('aria-pressed');

  it('TChatMessage shows the rating the message carries and a second click revokes it', async () => {
    const feedback = vi.fn();
    const el = mount(() =>
      h(TChatMessage, { message: assistant({ feedbackRating: true }), showFeedback: true, onFeedback: feedback }),
    );
    await settle();

    expect(pressed(el, en.feedback.helpful)).toBe('true');
    expect(pressed(el, en.feedback.notHelpful)).toBe('false');

    (el.querySelector(`button[aria-label="${en.feedback.helpful}"]`) as HTMLButtonElement).click();
    expect(feedback).toHaveBeenCalledWith('a1', null, undefined);
  });

  it('TThreadMessage marks the thumb the message was rated with', () => {
    const el = mount(() => h(TThreadMessage, { message: assistant({ feedbackRating: false }) }));
    const dislike = el.querySelector(`button[aria-label="${en.chat.dislike}"]`)!;
    const like = el.querySelector(`button[aria-label="${en.chat.like}"]`)!;
    expect(dislike.getAttribute('aria-pressed')).toBe('true');
    expect(dislike.classList.contains('is-active')).toBe(true);
    expect(like.getAttribute('aria-pressed')).toBe('false');
  });

  it('TThreadMessage leaves both thumbs unpressed for an unrated message', () => {
    const el = mount(() => h(TThreadMessage, { message: assistant() }));
    expect(pressed(el, en.chat.like)).toBe('false');
    expect(pressed(el, en.chat.dislike)).toBe('false');
  });
});

describe('TMessageAttachments', () => {
  it('uses the shared extension mapping, so a sent file keeps its composer icon', () => {
    const el = mount(() =>
      h(TMessageAttachments, {
        attachments: [
          { type: 'file', fileName: 'notes.docx' },
          { type: 'file', fileName: 'rows.csv' },
        ],
      }),
    );
    const icons = [...el.querySelectorAll('[data-icon]')].map((n) => n.getAttribute('data-icon'));
    expect(icons).toEqual(['lucide:file-type', 'lucide:table-2']);
  });
});

describe('TPromptInput', () => {
  it('gives a composer chip the same icon the sent message will show', async () => {
    const el = mount(() => h(TPromptInput, { modelValue: '', enableAttachments: true }));
    const input = el.querySelector('input[type="file"]') as HTMLInputElement;
    const docx = new File([new Uint8Array([1])], 'notes.docx');
    Object.defineProperty(input, 'files', { value: [docx], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
    expect(el.querySelector('[data-icon="lucide:file-type"]')).not.toBeNull();
  });
});

describe('TThreadComposer voice input', () => {
  it('says so when the browser refuses the microphone', async () => {
    let recogniser: { onerror: ((e: { error: string }) => void) | null; onend: (() => void) | null } | null = null;
    class FakeRecognition {
      continuous = false;
      interimResults = false;
      lang = '';
      onstart: (() => void) | null = null;
      onresult: (() => void) | null = null;
      onerror: ((e: { error: string }) => void) | null = null;
      onend: (() => void) | null = null;
      constructor() {
        recogniser = this;
      }
      start(): void {}
      stop(): void {}
    }
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    vi.stubGlobal('navigator', { ...globalThis.navigator, permissions: { query: async () => ({ state: 'prompt' }) } });

    const el = mount(() => h(TThreadComposer, { modelValue: '' }));
    (el.querySelector(`button[aria-label="${en.composer.voiceInput}"]`) as HTMLButtonElement).click();
    recogniser!.onerror?.({ error: 'not-allowed' });
    // The permission lookup that tells a dismissed prompt from a remembered block.
    await new Promise((r) => setTimeout(r, 0));
    await nextTick();

    const notice = el.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain(en.composer.voiceMicNotGranted);

    (notice!.querySelector('button') as HTMLButtonElement).click();
    await nextTick();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });
});
