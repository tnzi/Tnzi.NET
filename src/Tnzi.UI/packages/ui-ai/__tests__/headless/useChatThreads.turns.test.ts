import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * `useChatThreads` on an external CLI run that answers more than once (the CLI started a background
 * task, answered, and answered again when the task finished: `turn_completed` between the answers),
 * and on a turn that only compacted the conversation (`compacted`, `/compact`).
 */
type StreamOpts = Record<string, unknown>;
let streamRunner: (opts: StreamOpts) => Promise<{ text: string; reasoning?: string }>;
class ChatStreamRequestError extends Error {
  constructor(readonly status: number, readonly detail = '') {
    super(`Stream request failed: ${status} ${detail}`);
  }
}
vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: (opts: StreamOpts) => streamRunner(opts),
  ChatStreamRequestError,
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');

type Cb<T> = (v: T) => void;

function deps() {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
      getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: [], hasMore: false } })),
      delete: vi.fn(async () => ({ succeeded: true })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] as unknown[] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
    },
  };
}

beforeEach(() => {
  sessionStorage.clear();
});

describe('a run that answers again after a background task', () => {
  it('closes the first answer at turn_completed and streams the next one into a new row', async () => {
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      const onDelta = opts.onDelta as Cb<string>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-1', sequence: 1 });
      onDelta('Started the build.');
      onEvent({
        eventType: 'turn_completed',
        sequence: 5,
        eventData: { assistantMessageId: 'srv-a1', backgroundTasks: [{ id: 'b1', description: 'Build', kind: 'local_bash' }] },
      });
      onDelta('The build passed.');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a2', runId: 'r-1', threadId: 't-1' });
      // The stream's own total covers both answers
      return { text: 'Started the build.The build passed.' };
    };

    const chat = useChatThreads(deps() as never);
    await chat.send('build it');

    const replies = chat.messages.value.filter((m) => m.role === 'assistant');
    expect(replies.map((m) => [m.id, m.content, m.status])).toEqual([
      ['srv-a1', 'Started the build.', 'done'],
      ['srv-a2', 'The build passed.', 'done'],
    ]);
    expect(replies.every((m) => !m.isStreaming)).toBe(true);
    expect(chat.messages.value.map((m) => m.role)).toEqual(['user', 'assistant', 'assistant']);
  });

  /**
   * The last turn said nothing (the background tasks ended without another answer, or the wait
   * for them ran out): the server stores no answer for it, so no empty row is left behind, the
   * same as a reload shows. The run's terminal event may name the earlier answer; it stays one row.
   */
  it('a last turn that said nothing leaves no row, and does not repeat the first answer', async () => {
    streamRunner = async (opts) => {
      (opts.onDelta as Cb<string>)('Started.');
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'turn_completed', eventData: { assistantMessageId: 'srv-a1', backgroundTasks: [] } });
      (opts.onDone as Cb<Record<string, unknown>>)({ runId: 'r-1', assistantMessageId: 'srv-a1' });
      return { text: 'Started.' };
    };

    const chat = useChatThreads(deps() as never);
    await chat.send('build it');

    const replies = chat.messages.value.filter((m) => m.role === 'assistant');
    expect(replies.map((m) => [m.id, m.content, m.status])).toEqual([['srv-a1', 'Started.', 'done']]);
    expect(chat.isStreaming.value).toBe(false);
  });
});

describe('waiting on background tasks', () => {
  const tasks = [{ id: 'b1', description: 'Build', kind: 'local_bash' }, { id: 'b2', description: null, kind: 'local_agent' }];

  it('after an answer the conversation is free: a quiet row names the tasks, until the run answers again', async () => {
    let next: (() => void) | undefined;
    let answering: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-1', sequence: 1 });
      (opts.onDelta as Cb<string>)('Started the build.');
      onEvent({ eventType: 'turn_completed', sequence: 5, eventData: { assistantMessageId: 'srv-a1', backgroundTasks: tasks } });
      await new Promise<void>((r) => (next = r));
      (opts.onDelta as Cb<string>)('The build passed.');
      await new Promise<void>((r) => (answering = r));
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a2', runId: 'r-1', threadId: 't-1' });
      return { text: 'Started the build.The build passed.' };
    };
    const chat = useChatThreads(deps() as never);
    const turn = chat.send('build it');
    await vi.waitFor(() => expect(next).toBeDefined());

    const waiting = chat.messages.value[chat.messages.value.length - 1];
    expect(waiting).toMatchObject({ role: 'assistant', content: '', isStreaming: false, status: 'waiting', backgroundTasks: tasks });
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.activeRunId.value).toBeUndefined();

    // The run answers by itself when the tasks finish: busy again while it does
    next?.();
    await vi.waitFor(() => expect(answering).toBeDefined());
    expect(chat.isStreaming.value).toBe(true);
    expect(chat.activeRunId.value).toBe('r-1');
    expect(chat.messages.value[chat.messages.value.length - 1]).toMatchObject({ status: 'streaming', isStreaming: true, content: 'The build passed.' });

    answering?.();
    await turn;
    expect(chat.isStreaming.value).toBe(false);
    const last = chat.messages.value[chat.messages.value.length - 1];
    expect(last).toMatchObject({ id: 'srv-a2', content: 'The build passed.', status: 'done', isStreaming: false, backgroundTasks: null });
  });

  it('the first sign of the next answer ends the wait, before any text', async () => {
    let next: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', threadId: 't-1' });
      onEvent({ eventType: 'turn_completed', eventData: { assistantMessageId: 'srv-a1', backgroundTasks: tasks } });
      (opts.onToolCall as Cb<string[]>)(['Bash']);
      await new Promise<void>((r) => (next = r));
      return { text: '' };
    };
    const chat = useChatThreads(deps() as never);
    const turn = chat.send('build it');
    await vi.waitFor(() => expect(next).toBeDefined());

    const row = chat.messages.value[chat.messages.value.length - 1];
    expect(row).toMatchObject({ status: 'streaming', backgroundTasks: null, toolCalls: [{ name: 'Bash' }] });
    chat.abort();
    next?.();
    await turn;
  });

  it('a message sent while waiting goes at once: the waiting stream is let go, not cancelled', async () => {
    let released = false;
    const requests: StreamOpts[] = [];
    streamRunner = async (opts) => {
      requests.push(opts);
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      if (requests.length === 1) {
        onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u1', threadId: 't-1' });
        (opts.onDelta as Cb<string>)('The dev server is running.');
        onEvent({ eventType: 'turn_completed', eventData: { assistantMessageId: 'srv-a1', backgroundTasks: tasks } });
        await new Promise<void>((resolve) => {
          (opts.signal as AbortSignal).addEventListener('abort', () => {
            released = true;
            resolve();
          });
        });
        return { text: 'The dev server is running.' };
      }
      onEvent({ eventType: 'run_started', runId: 'r-2', userMessageId: 'srv-u2', threadId: 't-1' });
      (opts.onDelta as Cb<string>)('It listens on 20240.');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a2', runId: 'r-2', threadId: 't-1' });
      return { text: 'It listens on 20240.' };
    };
    const d = deps();
    const chat = useChatThreads(d as never);
    const first = chat.send('start the dev server');
    await vi.waitFor(() => expect(chat.messages.value.some((m) => m.status === 'waiting')).toBe(true));
    expect(chat.isStreaming.value).toBe(false);

    await chat.send('what port is it on?');
    await first;

    expect(released).toBe(true);
    expect(d.chatApi.cancelRun).not.toHaveBeenCalled();
    expect(requests).toHaveLength(2);
    expect(chat.messages.value.map((m) => [m.role, m.content, m.status ?? null])).toEqual([
      ['user', 'start the dev server', null],
      ['assistant', 'The dev server is running.', 'done'],
      ['user', 'what port is it on?', null],
      ['assistant', 'It listens on 20240.', 'done'],
    ]);
    expect(chat.isStreaming.value).toBe(false);
  });

  it('Stop while waiting cancels the run (its tasks end with it) and the waiting row goes', async () => {
    let next: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', threadId: 't-1' });
      onEvent({ eventType: 'turn_completed', eventData: { assistantMessageId: 'srv-a1', backgroundTasks: tasks } });
      await new Promise<void>((r) => (next = r));
      return { text: '' };
    };
    const d = deps();
    const chat = useChatThreads(d as never);
    const turn = chat.send('build it');
    await vi.waitFor(() => expect(next).toBeDefined());

    chat.abort();
    next?.();
    await turn;
    expect(d.chatApi.cancelRun).toHaveBeenCalledWith('r-1');
    expect(chat.messages.value.some((m) => m.status === 'waiting')).toBe(false);
    expect(chat.isStreaming.value).toBe(false);
  });

  /**
   * After a reload (or on coming back to the conversation) the run's stream is followed from the
   * start: it replays the answer the transcript already shows, then the turn boundary. The screen
   * ends up as it was before the reload: the answer once, then the waiting row with its tasks.
   */
  it('a reload shows the same state: the stored answer once, then the waiting row', async () => {
    const d = deps();
    d.threadApi.getMessages = vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'srv-u', role: 'user', content: 'build it', creationTime: '2026-11-04T10:00:00Z', order: 1 },
          { id: 'srv-a1', role: 'assistant', content: 'Started the build.', creationTime: '2026-11-04T10:00:05Z', order: 2 },
        ],
        hasMore: false,
      },
    }));
    d.chatApi.getActiveRuns = vi.fn(async () => ({ succeeded: true, data: [{ runId: 'r-1', userMessageId: 'srv-u', status: 'Running' }] }));
    let opened: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-1' });
      onEvent({ eventType: 'delta', threadId: 't-1', sequence: 2 });
      (opts.onDelta as Cb<string>)('Started the build.');
      onEvent({ eventType: 'turn_completed', sequence: 5, eventData: { assistantMessageId: 'srv-a1', backgroundTasks: tasks } });
      await new Promise<void>((r) => (opened = r));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t-1');
    await vi.waitFor(() => expect(opened).toBeDefined());

    expect(chat.messages.value.map((m) => [m.id.startsWith('m_') ? 'local' : m.id, m.content])).toEqual([
      ['srv-u', 'build it'],
      ['srv-a1', 'Started the build.'],
      ['local', ''],
    ]);
    expect(chat.messages.value[2]).toMatchObject({ isStreaming: false, status: 'waiting', backgroundTasks: tasks });
    expect(chat.isStreaming.value).toBe(false);
    chat.newChat();
    opened?.();
  });
});

describe('a turn that only compacted the conversation', () => {
  it('ends as `compacted` with the token counts, not as an empty answer', async () => {
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'compacted', eventData: { trigger: 'manual', preTokens: 28750, postTokens: 3533 } });
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a', runId: 'r-1' });
      return { text: '' };
    };

    const chat = useChatThreads(deps() as never);
    await chat.send('/compact');

    const reply = chat.messages.value.find((m) => m.role === 'assistant')!;
    expect(reply.status).toBe('compacted');
    expect(reply.compaction).toEqual({ trigger: 'manual', preTokens: 28750, postTokens: 3533 });
  });

  it('an automatic compaction inside an answered turn leaves the answer as it is', async () => {
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'compacted', eventData: { trigger: 'auto' } });
      (opts.onDelta as Cb<string>)('Here is the answer.');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a', runId: 'r-1' });
      return { text: 'Here is the answer.' };
    };

    const chat = useChatThreads(deps() as never);
    await chat.send('go on');

    const reply = chat.messages.value.find((m) => m.role === 'assistant')!;
    expect(reply.status).toBe('done');
    expect(reply.content).toBe('Here is the answer.');
  });
});

describe('a turn that said nothing', () => {
  /**
   * A turn boundary with nothing said since the last one and no stored answer (the CLI woke up for
   * a background task and stayed quiet): the waiting row stays the one row, no empty answer is left.
   */
  it('leaves no empty row: the waiting row takes the next answer', async () => {
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-1', sequence: 1 });
      (opts.onDelta as Cb<string>)('Started the build.');
      onEvent({ eventType: 'turn_completed', sequence: 5, eventData: { assistantMessageId: 'srv-a1', backgroundTasks: [] } });
      onEvent({ eventType: 'turn_completed', sequence: 6, eventData: { backgroundTasks: [{ id: 'b1', description: 'Build', kind: 'local_bash' }] } });
      (opts.onDelta as Cb<string>)('The build passed.');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a2', runId: 'r-1', threadId: 't-1' });
      return { text: 'Started the build.The build passed.' };
    };

    const chat = useChatThreads(deps() as never);
    await chat.send('build it');

    expect(chat.messages.value.map((m) => [m.role, m.id.startsWith('m_') ? 'local' : m.id, m.content, m.status ?? null])).toEqual([
      ['user', 'srv-u', 'build it', null],
      ['assistant', 'srv-a1', 'Started the build.', 'done'],
      ['assistant', 'srv-a2', 'The build passed.', 'done'],
    ]);
  });
});

describe('a reload in the middle of a run that answered several times and took queued messages', () => {
  /**
   * The transcript already shows every message the run stored; the stream replays the run from its
   * first event. The screen ends with the transcript in its order, and the answer being given now
   * right after the last user message, streaming what comes next.
   */
  it('keeps the transcript order and streams the current answer after the last user message', async () => {
    const d = deps();
    const at = (s: number) => `2026-11-13T10:00:${String(s).padStart(2, '0')}Z`;
    d.threadApi.getMessages = vi.fn(async () => ({
      succeeded: true,
      data: {
        messages: [
          { id: 'u1', role: 'user', content: 'first', creationTime: at(1), order: 1 },
          { id: 'a1', role: 'assistant', content: 'one', creationTime: at(2), order: 2 },
          { id: 'u2', role: 'user', content: 'second', creationTime: at(3), order: 3 },
          { id: 'a2', role: 'assistant', content: 'two', creationTime: at(4), order: 4 },
          { id: 'a3', role: 'assistant', content: 'three', creationTime: at(5), order: 5 },
          { id: 'u3', role: 'user', content: 'third', creationTime: at(6), order: 6 },
        ],
        hasMore: false,
      },
    }));
    d.chatApi.getActiveRuns = vi.fn(async () => ({ succeeded: true, data: [{ runId: 'r-1', userMessageId: 'u1', status: 'Running' }] }));
    let replayed: (() => void) | undefined;
    let more: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      const onDelta = opts.onDelta as Cb<string>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'u1', threadId: 't-1', sequence: 1 });
      onDelta('one');
      onEvent({ eventType: 'user_message', sequence: 115, eventData: { messageId: 'q-1', taken: true, text: 'second', threadMessageId: 'u2', assistantMessageId: 'a1' } });
      onDelta('two');
      onEvent({ eventType: 'turn_completed', sequence: 184, eventData: { assistantMessageId: 'a2', backgroundTasks: [] } });
      onEvent({ eventType: 'turn_completed', sequence: 190, eventData: { backgroundTasks: [] } });
      onDelta('three');
      onEvent({ eventType: 'user_message', sequence: 225, eventData: { messageId: 'q-2', taken: true, text: 'third', threadMessageId: 'u3', assistantMessageId: 'a3' } });
      onDelta('four');
      await new Promise<void>((r) => (replayed = r));
      onDelta(' and more');
      await new Promise<void>((r) => (more = r));
      return { text: '' };
    };
    const chat = useChatThreads(d as never);
    await chat.selectThread('t-1');
    await vi.waitFor(() => expect(replayed).toBeDefined());

    const shown = () => chat.messages.value.map((m) => [m.role, m.id.startsWith('m_') ? 'live' : m.id, m.content]);
    expect(shown()).toEqual([
      ['user', 'u1', 'first'],
      ['assistant', 'a1', 'one'],
      ['user', 'u2', 'second'],
      ['assistant', 'a2', 'two'],
      ['assistant', 'a3', 'three'],
      ['user', 'u3', 'third'],
      ['assistant', 'live', 'four'],
    ]);
    expect(chat.messages.value[6]).toMatchObject({ isStreaming: true, status: 'streaming' });

    replayed?.();
    await vi.waitFor(() => expect(more).toBeDefined());
    expect(chat.messages.value).toHaveLength(7);
    expect(chat.messages.value[6].content).toBe('four and more');
    chat.newChat();
    more?.();
  });
});
