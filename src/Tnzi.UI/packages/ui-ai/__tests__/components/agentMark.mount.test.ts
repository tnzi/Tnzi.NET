/**
 * The agent's mark in the byline above each answer, mounted for real.
 *
 * The byline is where a product's identity shows in every conversation, so the
 * mark is settable from `TChatApp` without replacing the whole message: an
 * icon (`agentIcon`), a different one while the answer is still being written
 * (`agentStreamingIcon`), or anything at all through the `agent-mark` slot.
 * Without any of them the byline is what it always was.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App, type Component } from 'vue';

// Icons render as an element carrying their name, so a test can read which
// glyph a component chose.
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
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { en } = await import('../../src/locales/en');

import type { ChatMessage } from '../../src/headless/useChat';

interface MarkScope {
  message: ChatMessage;
  streaming: boolean;
  placement: 'byline' | 'pending';
}

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(component: Component, props: () => Record<string, unknown>, slots?: Record<string, (scope: never) => unknown>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({ setup: () => () => h(component, props(), slots) });
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
});

const answer = (patch: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 'a1',
  role: 'assistant',
  content: 'The answer.',
  createdAt: '',
  ...patch,
});

const markIcon = (el: HTMLElement) =>
  el.querySelector('.t-thread-message__brand [data-icon]')?.getAttribute('data-icon');

describe('TThreadMessage agent mark', () => {
  it('draws the sparkles mark in the tile by default, streaming or not', async () => {
    const message = ref(answer({ isStreaming: true }));
    const el = mount(TThreadMessage, () => ({ message: message.value }));
    await settle();
    expect(markIcon(el)).toBe('lucide:sparkles');

    message.value = answer({ isStreaming: false });
    await settle();
    expect(markIcon(el)).toBe('lucide:sparkles');
  });

  it('draws the product icon when one is given', async () => {
    const el = mount(TThreadMessage, () => ({ message: answer(), agentIcon: 'brand:face' }));
    await settle();
    expect(markIcon(el)).toBe('brand:face');
  });

  it('switches to the streaming icon while the answer is written, and back when it is done', async () => {
    const message = ref(answer({ content: '', isStreaming: true }));
    const el = mount(TThreadMessage, () => ({
      message: message.value,
      agentIcon: 'brand:face',
      agentStreamingIcon: 'brand:face-talking',
    }));
    await settle();
    expect(markIcon(el)).toBe('brand:face-talking');

    message.value = answer({ isStreaming: false });
    await settle();
    expect(markIcon(el)).toBe('brand:face');
  });

  it('replaces the tile with the agent-mark slot, which is told whether the answer is streaming', async () => {
    const seen: MarkScope[] = [];
    const message = ref(answer({ isStreaming: true }));
    const el = mount(
      TThreadMessage,
      () => ({ message: message.value, agentIcon: 'brand:face' }),
      {
        'agent-mark': (scope: MarkScope) => {
          seen.push(scope);
          return h('b', { class: 'custom-mark' }, scope.streaming ? 'busy' : 'idle');
        },
      } as Record<string, (scope: never) => unknown>,
    );
    await settle();
    expect(el.querySelector('.t-thread-message__brand')).toBeNull();
    expect(el.querySelector('.custom-mark')?.textContent).toBe('busy');
    // The name and the rest of the byline stay.
    expect(el.querySelector('.t-thread-message__role strong')).not.toBeNull();

    message.value = answer({ isStreaming: false });
    await settle();
    expect(el.querySelector('.custom-mark')?.textContent).toBe('idle');
    const last = seen.at(-1);
    expect([last?.message.id, last?.streaming, last?.placement]).toEqual(['a1', false, 'byline']);
  });

  it('draws no mark on a finished answer when the byline is off', async () => {
    const el = mount(TThreadMessage, () => ({
      message: answer(),
      agentIcon: 'brand:face',
      agentStreamingIcon: 'brand:face-talking',
      showAgentByline: false,
    }));
    await settle();
    expect(el.querySelector('[data-icon^="brand:"]')).toBeNull();
    expect(el.querySelector('.t-thread-message__pending, .t-thread-message__pending-mark')).toBeNull();
  });
});

/**
 * With the byline off, the "still writing" mark (before the first words, and
 * while the agent works between two pieces of text) is where a product shows
 * its own working mark. Without one it stays the pulsing dot.
 */
describe('TThreadMessage still-writing mark without a byline', () => {
  const working = (patch: Partial<ChatMessage> = {}) => answer({ content: '', isStreaming: true, ...patch });

  it('stays the pulsing dot when no streaming mark is given, the idle icon included', async () => {
    const el = mount(TThreadMessage, () => ({ message: working(), agentIcon: 'brand:face', showAgentByline: false }));
    await settle();
    expect(el.querySelector('.t-thread-message__pending')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__pending-mark')).toBeNull();
    expect(el.querySelector('[data-icon^="brand:"]')).toBeNull();
  });

  it('shows the streaming icon in its place, announced the same way, until the first words arrive', async () => {
    const message = ref(working());
    const el = mount(TThreadMessage, () => ({
      message: message.value,
      agentStreamingIcon: 'brand:face-glancing',
      showAgentByline: false,
    }));
    await settle();
    const mark = el.querySelector('.t-thread-message__pending-mark');
    expect(mark?.querySelector('[data-icon^="brand:"]')?.getAttribute('data-icon')).toBe('brand:face-glancing');
    expect(mark?.getAttribute('role')).toBe('status');
    expect(mark?.getAttribute('aria-label')).toBe(en.chat.generating);
    expect(el.querySelector('.t-thread-message__pending')).toBeNull();

    message.value = working({ content: 'First words' });
    await settle();
    expect(el.querySelector('.t-thread-message__pending-mark')).toBeNull();
    expect(el.querySelector('.t-thread-message__body')).not.toBeNull();
  });

  it('comes back while the agent works after a tool call, at the start of the run\'s line', async () => {
    const el = mount(TThreadMessage, () => ({
      message: working({
        content: 'Let me look.',
        toolCalls: [{ id: 'c1', name: 'read_file', textOffset: 12, status: 'running' }],
      }),
      agentStreamingIcon: 'brand:face-glancing',
      showAgentByline: false,
    }));
    await settle();
    // One sign of work: on the run's line, not under it as well.
    expect(el.querySelector('.t-tool-run__line .t-thread-message__run-mark [data-icon="brand:face-glancing"]')).not.toBeNull();
    expect(el.querySelectorAll('[data-icon="brand:face-glancing"]')).toHaveLength(1);
    expect(el.querySelector('.t-thread-message__pending-mark')).toBeNull();
  });

  it('renders the agent-mark slot there, told it is the pending placement', async () => {
    const seen: MarkScope[] = [];
    const el = mount(
      TThreadMessage,
      () => ({ message: working(), showAgentByline: false }),
      {
        'agent-mark': (scope: MarkScope) => {
          seen.push(scope);
          return h('b', { class: 'custom-mark' }, scope.placement);
        },
      } as Record<string, (scope: never) => unknown>,
    );
    await settle();
    expect(el.querySelector('.t-thread-message__pending-mark .custom-mark')?.textContent).toBe('pending');
    expect(seen.at(-1)?.streaming).toBe(true);
    expect(el.querySelector('.t-thread-message__pending')).toBeNull();
  });

  it('leaves the byline-on turn as it was: the mark in the byline, no pending mark', async () => {
    const el = mount(TThreadMessage, () => ({ message: working(), agentStreamingIcon: 'brand:face-glancing' }));
    await settle();
    expect(markIcon(el)).toBe('brand:face-glancing');
    expect(el.querySelector('.t-thread-message__pending, .t-thread-message__pending-mark')).toBeNull();
  });

  // happy-dom has no layout, so the size is pinned through the stylesheet:
  // one line of the answer tall, so the first words replace it in place.
  it('is one line of the answer tall', () => {
    const source = readFileSync(resolve(import.meta.dirname, '../../src/components/chat/TThreadMessage.vue'), 'utf8');
    const style = source.slice(source.indexOf('<style')).replace(/\/\*[\s\S]*?\*\//g, '');
    const rule = (selector: string) => style.slice(style.indexOf(`${selector} {`), style.indexOf('}', style.indexOf(`${selector} {`)));
    const body = rule('.t-thread-message__body');
    const fontSize = Number(/font-size:\s*([\d.]+)px/.exec(body)?.[1]);
    const lineHeight = Number(/line-height:\s*([\d.]+)\s*;/.exec(body)?.[1]);
    const height = Number(/height:\s*([\d.]+)px/.exec(rule('.t-thread-message__pending-mark'))?.[1]);
    expect(fontSize * lineHeight).toBeGreaterThan(0);
    expect(height).toBe(fontSize * lineHeight);
  });
});

describe('TChatApp agent mark', () => {
  const shell = (props: Record<string, unknown>) => () => ({
    sidebarStorageKey: null,
    autoApplyTheme: false,
    messages: [answer({ isStreaming: true })],
    isStreaming: true,
    ...props,
  });

  it('keeps the default mark when nothing is set', async () => {
    const el = mount(TChatApp, shell({}));
    await settle();
    expect(markIcon(el)).toBe('lucide:sparkles');
  });

  it('hands both icons to every answer', async () => {
    const el = mount(TChatApp, shell({ agentIcon: 'brand:face', agentStreamingIcon: 'brand:face-talking' }));
    await settle();
    expect(markIcon(el)).toBe('brand:face-talking');
  });

  it('forwards the agent-mark slot with the message, its streaming state and the placement', async () => {
    const el = mount(TChatApp, shell({}), {
      'agent-mark': (scope: MarkScope) =>
        h('b', { class: 'custom-mark' }, `${scope.message.id}:${scope.streaming}:${scope.placement}`),
    } as Record<string, (scope: never) => unknown>);
    await settle();
    expect(el.querySelector('.t-thread-message__brand')).toBeNull();
    expect(el.querySelector('.custom-mark')?.textContent).toBe('a1:true:byline');
  });

  it('with the byline off, shows the streaming icon as the still-writing mark', async () => {
    const el = mount(
      TChatApp,
      shell({
        messages: [answer({ content: '', isStreaming: true })],
        showAgentByline: false,
        agentStreamingIcon: 'brand:face-glancing',
      }),
    );
    await settle();
    expect(el.querySelector('.t-thread-message__pending-mark [data-icon="brand:face-glancing"]')).not.toBeNull();
    expect(el.querySelector('.t-thread-message__pending')).toBeNull();
  });
});
