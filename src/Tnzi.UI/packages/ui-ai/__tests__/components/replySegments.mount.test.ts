/**
 * Tool cards inside the answer, where the agent used them.
 *
 * External CLI agents report for each tool call how long the reply text was when
 * the call started (`textOffset`). The thread layout used to stack every card
 * above the whole answer, so "I'll check the logs [card] The logs show [card]
 * so I fixed it" read as two cards and then a paragraph that referred back to
 * them. The classic transcript layout used the old one-line tool view; it now
 * shows the same cards (and per-tool renderers).
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import { segmentReply, hasPlacedToolCalls } from '../../src/components/chat/reply-segments';

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
const { default: TChatMessage } = await import('../../src/components/chat/TChatMessage.vue');

describe('segmentReply', () => {
  const text = 'I will check the logs.\n\nThe logs show a timeout.\n\nFixed.';
  const firstBlock = 'I will check the logs.'.length;
  const secondBlock = 'I will check the logs.\n\nThe logs show a timeout.'.length;

  it('cuts the text where each call happened, keeping calls at one offset together and in order', () => {
    const { lead, segments } = segmentReply(text, [
      { id: 'b', name: 'read_file', textOffset: firstBlock },
      { id: 'a', name: 'grep', textOffset: firstBlock },
      { id: 'c', name: 'edit', textOffset: secondBlock },
    ]);
    expect(lead).toEqual([]);
    expect(segments.map((s) => [s.text, s.toolCalls.map((c) => c.id)])).toEqual([
      ['I will check the logs.', ['b', 'a']],
      ['\n\nThe logs show a timeout.', ['c']],
      ['\n\nFixed.', []],
    ]);
    expect(segments.map((s) => s.text).join('')).toBe(text);
  });

  it('calls without a position lead, ahead of the text', () => {
    const { lead, segments } = segmentReply('Answer', [{ name: 'search' }, { name: 'fetch', textOffset: null }]);
    expect(lead.map((c) => c.name)).toEqual(['search', 'fetch']);
    expect(segments).toEqual([{ text: 'Answer', toolCalls: [] }]);
    expect(hasPlacedToolCalls([{ name: 'search' }])).toBe(false);
  });

  it('places a call beyond the text so far at its end, and a call at 0 before any text', () => {
    const { segments } = segmentReply('Short', [{ id: 'x', name: 'run', textOffset: 0 }, { id: 'y', name: 'run', textOffset: 99 }]);
    expect(segments.map((s) => [s.text, s.toolCalls.map((c) => c.id)])).toEqual([
      ['', ['x']],
      ['Short', ['y']],
      ['', []],
    ]);
  });
});

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

async function mountMessage(component: unknown, props: Record<string, unknown>): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => () => h(component as never, props) }));
  app.mount(host);
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
  return host;
}

/** The answer's pieces in document order: text bodies and tool cards. */
function readingOrder(el: HTMLElement): string[] {
  return Array.from(el.querySelectorAll('.t-thread-message__body, .t-tool-card')).map((node) =>
    node.classList.contains('t-tool-card') ? 'card' : (node.textContent ?? '').replace(/\s+/g, ' ').trim(),
  );
}

describe('TThreadMessage places cards in the text', () => {
  const content = 'Checking.\n\nDone.';
  const message = {
    id: 'm1',
    role: 'assistant',
    createdAt: '2026-10-19T00:00:00Z',
    content,
    status: 'done',
    toolCalls: [{ id: 'c1', name: 'read_file', status: 'completed', textOffset: 'Checking.'.length }],
  };

  it('a call with a position sits between the text before and after it', async () => {
    const el = await mountMessage(TThreadMessage, { message });
    // The markdown renders on a later frame.
    await vi.waitFor(() => expect(readingOrder(el)).toEqual(['Checking.', 'card', 'Done.']));
  });

  it('a call without one still leads, and hiding cards leaves the answer whole', async () => {
    const lead = { ...message, toolCalls: [{ id: 'c1', name: 'search', status: 'completed' }] };
    const first = await mountMessage(TThreadMessage, { message: lead });
    await vi.waitFor(() => expect(readingOrder(first)).toEqual(['card', 'Checking. Done.']));
    app?.unmount();
    host?.remove();
    const hidden = await mountMessage(TThreadMessage, { message, showToolCalls: false });
    await vi.waitFor(() => expect(readingOrder(hidden)).toEqual(['Checking. Done.']));
  });

  it('while streaming, the text after the last call keeps the caret even before any arrives', async () => {
    const streaming = { ...message, content: 'Checking.', isStreaming: true, status: 'streaming' };
    const el = await mountMessage(TThreadMessage, { message: streaming });
    await vi.waitFor(() => expect(readingOrder(el).slice(0, 2)).toEqual(['Checking.', 'card']));
    expect(el.querySelectorAll('.t-thread-message__body')).toHaveLength(2);
  });
});

describe('TChatMessage', () => {
  it('shows the tool cards, not the one-line view', async () => {
    const el = await mountMessage(TChatMessage, {
      message: {
        id: 'm2', role: 'assistant', createdAt: '2026-10-19T00:00:00Z', content: 'Ok',
        toolCalls: [{ id: 'c9', name: 'read_file', status: 'completed' }],
      },
    });
    expect(el.querySelector('.t-tool-card')).not.toBeNull();
  });
});
