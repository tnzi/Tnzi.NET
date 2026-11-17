/**
 * The thinking block says "running" while thinking is what arrives, not for the
 * whole streaming turn: in a long agent turn the block sat at the top spinning
 * under later paragraphs and tool calls, which read as stuck.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';

vi.mock('@iconify/vue', async (importOriginal) => {
  const original = await importOriginal<typeof import('@iconify/vue')>();
  const { defineComponent: define, h: render } = await import('vue');
  return {
    ...original,
    Icon: define({
      props: { icon: { type: String, default: '' } },
      setup: (props) => () => render('i', { 'data-icon': props.icon }),
    }),
  };
});

const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

async function mountThread(initial: ChatMessage) {
  const message = ref<ChatMessage>(initial);
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => () => h(TThreadMessage, { message: message.value }) }));
  app.mount(host);
  await settle();
  const el = host;
  return {
    running: () => el.querySelector('.t-reasoning-stage__status--running') !== null,
    title: () => el.querySelector('.t-reasoning-stage__title')?.textContent?.trim(),
    update: async (patch: Partial<ChatMessage>) => {
      message.value = { ...message.value, ...patch };
      await settle();
    },
  };
}

const base: ChatMessage = {
  id: 'a1', role: 'assistant', content: '', reasoning: '', createdAt: '2026-11-04T00:00:00Z',
  isStreaming: true, status: 'streaming',
};

describe('TThreadMessage thinking block', () => {
  it('runs while thinking arrives and is done once text follows, mid-stream', async () => {
    const view = await mountThread(base);
    await view.update({ reasoning: 'Let me look at the logs' });
    expect(view.running()).toBe(true);
    expect(view.title()).toBe('Thinking');
    await view.update({ content: 'The logs show a timeout.' });
    expect(view.running()).toBe(false);
    expect(view.title()).toBe('Thought process');
  });

  it('is done once a tool call follows, and runs again if the turn goes back to thinking', async () => {
    const view = await mountThread(base);
    await view.update({ reasoning: 'First' });
    await view.update({ toolCalls: [{ id: 'c1', name: 'grep', status: 'running' }] });
    expect(view.running()).toBe(false);
    await view.update({ reasoning: 'First\n\nNow the fix' });
    expect(view.running()).toBe(true);
  });

  it('leaves no spinner when the turn is stopped or fails', async () => {
    const stopped = await mountThread(base);
    await stopped.update({ reasoning: 'Thinking' });
    await stopped.update({ isStreaming: false, status: 'stopped' });
    expect(stopped.running()).toBe(false);
    app?.unmount();
    host?.remove();
    const failed = await mountThread(base);
    await failed.update({ reasoning: 'Thinking' });
    await failed.update({ isStreaming: false, status: 'error' });
    expect(failed.running()).toBe(false);
  });

  it('a reopened conversation shows done', async () => {
    const view = await mountThread({ ...base, content: 'Answer', reasoning: 'Thought', isStreaming: false, status: 'done' });
    expect(view.running()).toBe(false);
    expect(view.title()).toBe('Thought process');
  });
});
