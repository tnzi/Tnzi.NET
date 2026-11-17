import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * `useChatThreads` and sub-agents of an external CLI run: the work of a background sub-agent that goes on after the
 * answer that dispatched it updates that answer's card (live and after a reload), not the answer streaming now.
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

function deps(messages: unknown[] = []) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ({ succeeded: true, data: { items: [{ id: 't-1', title: 'T', lastActivityTime: '2026-11-09T15:00:00Z' }], totalCount: 1 } })),
      getMessages: vi.fn(async () => ({ succeeded: true, data: { messages, hasMore: false } })),
      delete: vi.fn(async () => ({ succeeded: true })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] as unknown[] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
      stopTask: vi.fn(async (_threadId: string, _taskId: string) => ({ succeeded: true })),
    },
  };
}

beforeEach(() => {
  sessionStorage.clear();
});

const dispatch = (subAgent: Record<string, unknown>) => ({
  id: 'agent-1',
  name: 'Agent',
  status: 'completed',
  textOffset: 0,
  task: { kind: 'agent', status: 'running', background: true, description: 'Read alpha', steps: [], ...subAgent },
});

describe('a background sub-agent', () => {
  it('goes on updating the card of the answer that dispatched it, and the waiting row stays waiting', async () => {
    let next: (() => void) | undefined;
    streamRunner = async (opts) => {
      const onEvent = opts.onEvent as Cb<Record<string, unknown>>;
      onEvent({ eventType: 'run_started', runId: 'r-1', userMessageId: 'srv-u', threadId: 't-1', sequence: 1 });
      onEvent({ isToolCall: true, toolCallNames: ['Agent'], toolCalls: [dispatch({})] });
      (opts.onDelta as Cb<string>)('Started.');
      onEvent({ eventType: 'turn_completed', sequence: 5, eventData: { assistantMessageId: 'srv-a1', backgroundTasks: [{ id: 't1', kind: 'local_agent' }] } });
      // The sub-agent works after the boundary: its call and its card's progress
      onEvent({
        isToolCall: true,
        toolCallNames: ['Read'],
        toolCalls: [
          { id: 'read-1', name: 'Read', status: 'running', parentId: 'agent-1' },
          dispatch({ steps: [{ type: 'tool', toolCallId: 'read-1' }] }),
        ],
      });
      await new Promise<void>((r) => (next = r));
      onEvent({ toolCalls: [dispatch({ status: 'completed', result: 'alpha.', steps: [{ type: 'tool', toolCallId: 'read-1' }] })] });
      (opts.onDelta as Cb<string>)('It finished.');
      (opts.onDone as Cb<Record<string, unknown>>)({ assistantMessageId: 'srv-a2', runId: 'r-1', threadId: 't-1' });
      return { text: 'Started.It finished.' };
    };

    const chat = useChatThreads(deps() as never);
    const turn = chat.send('read alpha in the background');
    await vi.waitFor(() => expect(next).toBeDefined());

    const first = chat.messages.value.find((m) => m.id === 'srv-a1')!;
    expect(first.toolCalls?.map((c) => c.id)).toEqual(['agent-1', 'read-1']);
    expect(first.toolCalls?.[0]?.task?.steps).toEqual([{ type: 'tool', toolCallId: 'read-1' }]);
    const waiting = chat.messages.value[chat.messages.value.length - 1]!;
    expect(waiting).toMatchObject({ status: 'waiting', isStreaming: false });
    expect(waiting.toolCalls ?? []).toEqual([]);

    next?.();
    await turn;

    const replies = chat.messages.value.filter((m) => m.role === 'assistant');
    expect(replies.map((m) => m.id)).toEqual(['srv-a1', 'srv-a2']);
    expect(replies[0]!.toolCalls?.[0]?.task).toMatchObject({ status: 'completed', result: 'alpha.' });
    expect(replies[1]!.toolCalls ?? []).toEqual([]);
    expect(replies[1]!.content).toBe('It finished.');
  });

  it('reported by a later run is folded into its card when the conversation loads', async () => {
    const stored = [
      { id: 'u-1', role: 'User', content: 'read alpha', creationTime: '2026-11-09T15:00:00Z' },
      {
        id: 'a-1', role: 'Assistant', content: 'Started.', creationTime: '2026-11-09T15:00:01Z',
        toolCalls: JSON.stringify([{ Id: 'agent-1', Name: 'Agent', Status: 'completed', TextOffset: 0, Task: { Kind: 'agent', Status: 'running', Background: true, Steps: [] } }]),
      },
      { id: 'u-2', role: 'User', content: 'and now?', creationTime: '2026-11-09T15:01:00Z' },
      {
        id: 'a-2', role: 'Assistant', content: 'It is done.', creationTime: '2026-11-09T15:01:01Z',
        toolCalls: JSON.stringify([
          { Id: 'agent-1', Name: '', Status: 'running', TextOffset: 0, Task: { Kind: 'agent', Status: 'completed', Result: 'alpha.', Steps: [{ Type: 'tool', ToolCallId: 'read-1' }] } },
          { Id: 'read-1', Name: 'Read', Status: 'completed', ParentId: 'agent-1' },
        ]),
      },
    ];
    const chat = useChatThreads(deps(stored) as never);

    await chat.selectThread('t-1');

    const byId = Object.fromEntries(chat.messages.value.map((m) => [m.id, m]));
    expect(byId['a-1']!.toolCalls?.map((c) => c.id)).toEqual(['agent-1', 'read-1']);
    expect(byId['a-1']!.toolCalls?.[0]).toMatchObject({ name: 'Agent', task: { status: 'completed', result: 'alpha.', background: true } });
    expect(byId['a-2']!.toolCalls).toEqual([]);
  });
});

describe('stopping a task', () => {
  it('asks the server for the open conversation, and reports a refusal', async () => {
    const d = deps([{ id: 'u-1', role: 'User', content: 'go', creationTime: '2026-11-09T15:00:00Z' }]);
    const errors: unknown[] = [];
    const chat = useChatThreads({ ...d, onError: (_m: string, e: unknown) => errors.push(e) } as never);
    await chat.selectThread('t-1');

    expect(await chat.stopTask('a9d5c37c')).toBe(true);
    expect(d.chatApi.stopTask).toHaveBeenCalledWith('t-1', 'a9d5c37c');

    d.chatApi.stopTask.mockResolvedValueOnce({ succeeded: false, message: 'No run is going on in this conversation.', code: 409 } as never);
    expect(await chat.stopTask('a9d5c37c')).toBe(false);
    const stopErrors = errors.filter((e) => (e as { operation?: string }).operation === 'stop-task');
    expect(stopErrors).toHaveLength(1);
    expect(stopErrors[0]).toMatchObject({ operation: 'stop-task', status: 409 });
  });

  it('does nothing without an open conversation', async () => {
    const d = deps();
    const chat = useChatThreads(d as never);

    expect(await chat.stopTask('a9d5c37c')).toBe(false);
    expect(d.chatApi.stopTask).not.toHaveBeenCalled();
  });
});
