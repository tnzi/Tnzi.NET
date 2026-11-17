import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, inject } from 'vue';

/**
 * `useChatThreads().approvals`: the external agent's requests from the stream (shown at once,
 * taken off by their outcome) and from the pending endpoint (a conversation opened with a run
 * still waiting), plus the application's own sources; provided to the component tree.
 */
type StreamOpts = Record<string, unknown>;
type Cb<T> = (v: T) => void;
let streamRunner: (opts: StreamOpts) => Promise<{ text: string }>;

vi.mock('@tnzi/core/services/ai', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  streamChat: (opts: StreamOpts) => streamRunner(opts),
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');
const { CHAT_THREADS_KEY, pickChatApprovals } = await import('../../src/headless/chat-approvals-key');

const expiresAt = () => new Date(Date.now() + 5 * 60_000).toISOString();

function request(requestId: string, kind = 'permission') {
  return {
    requestId,
    kind,
    status: 'Pending',
    tool: 'Bash',
    input: { command: 'ls' },
    options: [{ id: 'allow_once', name: 'Allow', kind: 'allow_once' }],
    expiresAt: expiresAt(),
  };
}

function deps(pending: unknown[] = []) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0 } })),
      getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: [], hasMore: false } })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] as unknown[] })),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
      getPendingPermissions: vi.fn(async () => ({ succeeded: true, code: 200, data: pending })),
      answerRunPermission: vi.fn(async () => ({ succeeded: true, code: 200 })),
    },
  };
}

beforeEach(() => {
  sessionStorage.clear();
});

describe('useChatThreads approvals', () => {
  it('a request on the stream is a card at once, and its outcome takes it off', async () => {
    const d = deps();
    let emit: Cb<Record<string, unknown>> = () => {};
    let release: (() => void) | undefined;
    streamRunner = async (opts) => {
      emit = opts.onEvent as Cb<Record<string, unknown>>;
      emit({ eventType: 'run_started', runId: 'r-1', threadId: 't-1' });
      await new Promise<void>((r) => (release = r));
      return { text: 'done' };
    };
    const chat = useChatThreads(d as never);
    const turn = chat.send('clean up');
    await vi.waitFor(() => expect(chat.activeThreadId.value).toBe('t-1'));

    emit({ eventType: 'permission_request', threadId: 't-1', eventData: request('q-1') });
    expect(chat.approvals.items.value.map((i) => [i.key, i.kind])).toEqual([['run:q-1', 'permission']]);
    expect((chat.approvals.items.value[0]!.data as { runId: string }).runId).toBe('r-1');

    await chat.approvals.answer('run:q-1', { optionId: 'allow_once' });
    expect(d.chatApi.answerRunPermission).toHaveBeenCalledWith('r-1', 'q-1', { optionId: 'allow_once', message: null, answers: null });

    emit({ eventType: 'permission_request', threadId: 't-1', eventData: request('q-2', 'plan') });
    expect(chat.approvals.items.value.map((i) => i.kind)).toEqual(['plan']);
    emit({ eventType: 'permission_resolved', threadId: 't-1', eventData: { requestId: 'q-2', status: 'TimedOut' } });
    expect(chat.approvals.items.value).toEqual([]);

    release?.();
    void turn;
  });

  it('asks the pending endpoint for a conversation with a running turn, and the app sources too', async () => {
    const d = deps([{ ...request('q-9', 'question'), runId: 'r-0', questions: [{ question: 'Which?', options: [{ label: 'A' }], multiSelect: false }] }]);
    const own = {
      id: 'mine',
      load: vi.fn(async () => [{ key: 'mine:1', kind: 'confirmation', expiresAt: expiresAt(), data: {} }]),
      answer: vi.fn(async () => undefined),
    };
    let release: (() => void) | undefined;
    streamRunner = async (opts) => {
      (opts.onEvent as Cb<Record<string, unknown>>)({ eventType: 'run_started', runId: 'r-0', threadId: 't-9' });
      await new Promise<void>((r) => (release = r));
      (opts.onDone as Cb<Record<string, unknown>>)({ eventType: 'done', threadId: 't-9', runId: 'r-0', assistantMessageId: 'a-9' });
      return { text: 'done' };
    };
    const chat = useChatThreads({ ...(d as never), approvalSources: [own], approvalPollMs: 0 });
    const turn = chat.send('go');

    await vi.waitFor(() => expect(chat.approvals.items.value.map((i) => i.key).sort()).toEqual(['mine:1', 'run:q-9']));
    expect(d.chatApi.getPendingPermissions).toHaveBeenCalledWith('t-9');
    expect(own.load).toHaveBeenCalledWith('t-9');

    await chat.approvals.answer('mine:1', { optionId: 'approve' });
    expect(own.answer).toHaveBeenCalled();

    // The turn is over and its request went with it: the ask when it ends finds nothing
    d.chatApi.getPendingPermissions.mockResolvedValue({ succeeded: true, code: 200, data: [] });
    const asked = d.chatApi.getPendingPermissions.mock.calls.length;
    release?.();
    void turn;
    await vi.waitFor(() => expect(chat.isStreaming.value).toBe(false));
    await vi.waitFor(() => expect(d.chatApi.getPendingPermissions.mock.calls.length).toBeGreaterThan(asked));
    await vi.waitFor(() => expect(chat.approvals.items.value).toEqual([]));
  });

  it('a conversation opened with no running turn shows the cards its sources still list', async () => {
    const d = deps([{ ...request('q-5'), runId: 'r-5' }]);
    const own = {
      id: 'mine',
      load: vi.fn(async () => [{ key: 'mine:5', kind: 'confirmation', expiresAt: expiresAt(), data: {} }]),
      answer: vi.fn(async () => undefined),
    };
    const chat = useChatThreads({ ...(d as never), approvalSources: [own], approvalPollMs: 50 });
    await chat.selectThread('t-5');
    expect(chat.isStreaming.value).toBe(false);

    await vi.waitFor(() => expect(chat.approvals.items.value.map((i) => i.key).sort()).toEqual(['mine:5', 'run:q-5']));
    expect(d.chatApi.getPendingPermissions).toHaveBeenCalledWith('t-5');
    expect(own.load).toHaveBeenCalledWith('t-5');

    // Idle: not asked again on the interval
    const calls = own.load.mock.calls.length;
    await new Promise((r) => setTimeout(r, 200));
    expect(own.load.mock.calls.length).toBe(calls);

    await chat.approvals.answer('mine:5', { optionId: 'approve' });
    expect(own.answer).toHaveBeenCalled();
    expect(chat.approvals.items.value.map((i) => i.key)).toEqual(['run:q-5']);
  });

  it('registers with the component it runs in, both instances when it is called twice', () => {
    let registry: ReturnType<typeof inject<unknown>>;
    const Child = defineComponent({
      setup() {
        registry = inject(CHAT_THREADS_KEY, null);
        return () => h('i');
      },
    });
    let first: ReturnType<typeof useChatThreads> | undefined;
    let second: ReturnType<typeof useChatThreads> | undefined;
    const Parent = defineComponent({
      setup() {
        first = useChatThreads(deps() as never);
        second = useChatThreads(deps() as never);
        return () => h(Child);
      },
    });
    const host = document.createElement('div');
    const app = createApp(Parent);
    app.mount(host);
    const found = registry as Parameters<typeof pickChatApprovals>[0];
    expect(found!.bindings.value.map((b) => b.approvals)).toEqual([first!.approvals, second!.approvals]);
    expect(pickChatApprovals(found, first!.messages.value).approvals).toBe(first!.approvals);
    expect(pickChatApprovals(found, second!.messages.value).approvals).toBe(second!.approvals);
    app.unmount();
  });
});
