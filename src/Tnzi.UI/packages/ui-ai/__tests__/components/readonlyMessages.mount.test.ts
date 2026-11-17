/**
 * `readonly` on the message renderers: a record of a conversation shows a failed answer's
 * failure line with no Retry and no other action, and `TMessageList` keeps no room for a
 * composer. Without `readonly` the renderers are as before (Retry, actions, edit, spacer).
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');
const { default: TChatMessage } = await import('../../src/components/chat/TChatMessage.vue');
const { default: TMessageList } = await import('../../src/components/chat/TMessageList.vue');
const { en } = await import('../../src/locales/en');

import type { ChatMessage } from '../../src/headless/useChat';

let app: App | null = null;
let host: HTMLElement | null = null;

async function mount(render: () => unknown): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  for (let i = 0; i < 3; i += 1) await nextTick();
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

const failed: ChatMessage = {
  id: 'a1',
  role: 'assistant',
  content: 'Half an answer',
  createdAt: '2026-11-13T10:00:00Z',
  status: 'error',
  error: 'The agent ran out of time',
  errorCode: 'AI_TIMEOUT',
};
const answered: ChatMessage = { id: 'a2', role: 'assistant', content: 'Done', createdAt: '2026-11-13T10:01:00Z', status: 'done' };
const question: ChatMessage = { id: 'u1', role: 'user', content: 'Tidy the memory', createdAt: '2026-11-13T09:59:00Z' };

const buttonLabels = (el: HTMLElement) =>
  [...el.querySelectorAll('button')].map((b) => b.getAttribute('aria-label') || b.textContent?.trim() || '');

describe('TThreadMessage readonly', () => {
  it('shows the failure and why, with no Retry and no actions', async () => {
    const el = await mount(() => h(TThreadMessage, { message: failed, readonly: true }));
    const alert = el.querySelector('.t-thread-message__error');
    expect(alert?.textContent).toContain('The agent ran out of time');
    expect(el.textContent).toContain('Half an answer');
    expect(el.querySelector('.t-thread-message__error-retry')).toBeNull();
    expect(el.querySelector('.t-thread-message__actions')).toBeNull();
    expect(el.querySelectorAll('button')).toHaveLength(0);
  });

  it('draws no action row under an answer and no edit on a question', async () => {
    const el = await mount(() => [
      h(TThreadMessage, { message: question, readonly: true, enableEdit: true }),
      h(TThreadMessage, { message: answered, readonly: true }, { actions: () => h('button', 'custom') }),
    ]);
    expect(el.querySelector('.t-thread-message__user-actions')).toBeNull();
    expect(el.querySelector('.t-thread-message__actions')).toBeNull();
    expect(el.querySelectorAll('button')).toHaveLength(0);
  });

  it('keeps Retry and the actions by default', async () => {
    const el = await mount(() => [
      h(TThreadMessage, { message: failed }),
      h(TThreadMessage, { message: question, enableEdit: true }),
    ]);
    expect(el.querySelector('.t-thread-message__error-retry')?.textContent).toContain(en.common.retry);
    expect(el.querySelector('.t-thread-message__actions')).not.toBeNull();
    expect(buttonLabels(el)).toContain(en.chat.regenerate);
    expect(buttonLabels(el)).toContain(en.chat.editMessage);
  });

  it('keeps Retry when only showActions is off, as before', async () => {
    const el = await mount(() => h(TThreadMessage, { message: failed, showActions: false }));
    expect(el.querySelector('.t-thread-message__error-retry')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__actions')).toBeNull();
  });
});

describe('TChatMessage readonly', () => {
  it('shows the failure with no Retry and no actions', async () => {
    const el = await mount(() => h(TChatMessage, { message: failed, readonly: true, showFeedback: true }));
    expect(el.querySelector('.t-chat-message__error')?.textContent).toContain('The agent ran out of time');
    expect(el.querySelector('.t-chat-message__error-retry')).toBeNull();
    expect(el.querySelectorAll('button')).toHaveLength(0);
  });

  it('keeps Retry by default', async () => {
    const el = await mount(() => h(TChatMessage, { message: failed }));
    expect(el.querySelector('.t-chat-message__error-retry')).not.toBeNull();
  });
});

describe('TMessageList readonly', () => {
  it('renders every message without actions or Retry, and no composer spacer', async () => {
    const el = await mount(() => h(TMessageList, { messages: [question, failed, answered], readonly: true }));
    expect(el.textContent).toContain('Tidy the memory');
    expect(el.textContent).toContain('The agent ran out of time');
    expect(el.querySelectorAll('button')).toHaveLength(0);
    expect(el.querySelector('.h-40')).toBeNull();
  });

  it('keeps the actions and the spacer by default', async () => {
    const el = await mount(() => h(TMessageList, { messages: [question, failed, answered] }));
    expect(el.querySelector('.t-chat-message__error-retry')).not.toBeNull();
    expect(el.querySelector('.h-40')).not.toBeNull();
  });
});
