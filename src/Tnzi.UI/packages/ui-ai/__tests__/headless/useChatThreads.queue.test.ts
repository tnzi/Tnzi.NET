import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, inject } from 'vue';

/**
 * `useChatThreads().messageQueue`: while an external agent's turn runs, `send` queues the message
 * on the server (with the turn's model and metadata) instead of dropping it; a built-in turn has
 * no server run to wait for, so nothing is queued behind it. When a queued message goes out as a
 * turn this page did not start, the hook follows the conversation again.
 */
type StreamOpts = Record<string, unknown>;
type Cb<T> = (v: T) => void;
let streamRunner: (opts: StreamOpts) => Promise<{ text: string }>;

vi.mock('@tnzi/core/services/ai', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  streamChat: (opts: StreamOpts) => streamRunner(opts),
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');
const { CHAT_THREADS_KEY, pickChatMessageQueue } = await import('../../src/headless/chat-approvals-key');

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data });

function deps() {
  let queue: Record<string, unknown>[] = [];
  return {
    setQueue: (next: Record<string, unknown>[]) => (queue = next),
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(async () => 'fresh'), reportUnauthorized: vi.fn() },
    threadApi: {
      getList: vi.fn(async () => ok({ items: [], totalCount: 0 })),
      getMessages: vi.fn(async () => ok({ messages: [], hasMore: false })),
    },
    chatApi: {
      getChatStreamUrl: () => '/api/chat/stream',
      getActiveRuns: vi.fn(async () => ok([] as unknown[])),
      getRunStreamUrl: (runId: string, from = 0) => `/api/chat/runs/${runId}/stream?fromSequence=${from}`,
      cancelRun: vi.fn(async () => ({ succeeded: true })),
      getPendingPermissions: vi.fn(async () => ok([])),
      answerRunPermission: vi.fn(async () => ({ succeeded: true, code: 200 })),
      getQueue: vi.fn(async () => ok([...queue])),
      queueMessage: vi.fn(async (threadId: string, input: { text: string }) => {
        const queued = { id: `q-${queue.length + 1}`, threadId, text: input.text, state: 'Queued', sendNow: false, afterRunId: 'r-1', queuedTime: '' };
        queue = [...queue, queued];
        return ok(queued);
      }),
      updateQueuedMessage: vi.fn(),
      removeQueuedMessage: vi.fn(),
      sendQueuedMessageNow: vi.fn(),
    },
  };
}

/** A turn that streams until released; `runId` names a server run (an external agent), or none (built-in). */
function holdTurn(runId: string | null): { release: () => void } {
  const handle = { release: () => {} };
  streamRunner = async (opts) => {
    const emit = opts.onEvent as Cb<Record<string, unknown>>;
    emit(runId ? { eventType: 'run_started', runId, threadId: 't-1' } : { delta: 'thinking', threadId: 't-1' });
    await new Promise<void>((r) => (handle.release = r));
    (opts.onDone as Cb<Record<string, unknown>>)({ eventType: 'done', threadId: 't-1', runId, assistantMessageId: 'a-1' });
    return { text: 'done' };
  };
  return handle;
}

beforeEach(() => sessionStorage.clear());

describe('useChatThreads message queue', () => {
  it('sending while an external turn runs queues the message with the model and metadata', async () => {
    const d = deps();
    const turn = holdTurn('r-1');
    const chat = useChatThreads({ ...(d as never), model: () => 'sonnet', effort: () => 'high', messageQueuePollMs: 0 });
    void chat.send('refactor the parser');
    await vi.waitFor(() => expect(chat.messageQueue.available.value).toBe(true));

    await chat.send('  and run the tests  ', [], { metadata: { device: 'd-7' } });

    expect(d.chatApi.queueMessage).toHaveBeenCalledWith('t-1', {
      text: 'and run the tests', model: 'sonnet', effort: 'high', metadata: { device: 'd-7' },
    });
    expect(chat.messageQueue.items.value.map((m) => m.text)).toEqual(['and run the tests']);
    expect(chat.messages.value.filter((m) => m.role === 'user')).toHaveLength(1);
    turn.release();
  });

  it('a queued message carries its attachments inline, under the rules of a normal send', async () => {
    const d = deps();
    const turn = holdTurn('r-1');
    const chat = useChatThreads({ ...(d as never), inlineFiles: true, messageQueuePollMs: 0 });
    void chat.send('first');
    await vi.waitFor(() => expect(chat.messageQueue.available.value).toBe(true));

    await chat.send('look at these', [
      new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'shot.png', { type: 'image/png' }),
      new File(['Subject: Invoice'], 'invoice.eml', { type: 'message/rfc822' }),
    ]);

    const [, input] = d.chatApi.queueMessage.mock.calls[0]! as unknown as [string, { text: string; content: Record<string, unknown>[] }];
    expect(input.text).toBe('look at these');
    expect(input.content.map((part) => [part.type, part.fileName])).toEqual([['image', 'shot.png'], ['document', 'invoice.eml']]);
    expect(input.content[1]!.mediaType).toBe('message/rfc822');
    expect(input.content.every((part) => typeof part.base64Data === 'string' && (part.base64Data as string).length > 0)).toBe(true);
    turn.release();
  });

  it('a file the rules refuse is reported, nothing is queued, and the words come back', async () => {
    const d = deps();
    const onError = vi.fn();
    const turn = holdTurn('r-1');
    const chat = useChatThreads({ ...(d as never), onError, messageQueuePollMs: 0 });
    void chat.send('first');
    await vi.waitFor(() => expect(chat.messageQueue.available.value).toBe(true));

    // Without `inlineFiles` (and no upload) a file that is not an image or a PDF cannot be sent, queued or not.
    await chat.send('see attached', [new File(['x'], 'invoice.eml', { type: 'message/rfc822' })]);

    expect(d.chatApi.queueMessage).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ operation: 'attachment' }));
    expect(chat.inputText.value).toBe('see attached');
    turn.release();
  });

  it('a queued message the agent takes in mid-turn shows up between the answer so far and the rest of it', async () => {
    const d = deps();
    let release = () => {};
    streamRunner = async (opts) => {
      const emit = opts.onEvent as Cb<Record<string, unknown>>;
      const delta = opts.onDelta as Cb<string>;
      emit({ eventType: 'run_started', runId: 'r-1', threadId: 't-1', userMessageId: 'u-1' });
      delta('Building it now.');
      await new Promise<void>((r) => (release = r));
      emit({ eventType: 'user_message', eventData: { messageId: 'q-1', taken: true, text: 'also run the linter', threadMessageId: 'u-2', assistantMessageId: 'a-1' } });
      delta('Linted too.');
      (opts.onDone as Cb<Record<string, unknown>>)({ eventType: 'done', threadId: 't-1', runId: 'r-1', assistantMessageId: 'a-2' });
      return { text: 'Building it now.Linted too.' };
    };
    const chat = useChatThreads({ ...(d as never), messageQueuePollMs: 0 });
    const turn = chat.send('build and test');
    await vi.waitFor(() => expect(chat.messages.value.at(-1)?.content).toBe('Building it now.'));

    release();
    await turn;

    expect(chat.messages.value.map((m) => [m.role, m.content])).toEqual([
      ['user', 'build and test'],
      ['assistant', 'Building it now.'],
      ['user', 'also run the linter'],
      ['assistant', 'Linted too.'],
    ]);
    expect(chat.messages.value[1]!.id).toBe('a-1');
    expect(chat.messages.value[2]!.id).toBe('u-2');
  });

  it('a queued message the agent declined changes nothing in the conversation', async () => {
    const d = deps();
    streamRunner = async (opts) => {
      const emit = opts.onEvent as Cb<Record<string, unknown>>;
      emit({ eventType: 'run_started', runId: 'r-1', threadId: 't-1' });
      (opts.onDelta as Cb<string>)('Working.');
      emit({ eventType: 'user_message', eventData: { messageId: 'q-1', taken: false, text: 'and deploy', reason: 'declined' } });
      (opts.onDone as Cb<Record<string, unknown>>)({ eventType: 'done', threadId: 't-1', runId: 'r-1', assistantMessageId: 'a-1' });
      return { text: 'Working.' };
    };
    const chat = useChatThreads({ ...(d as never), messageQueuePollMs: 0 });
    await chat.send('work');

    expect(chat.messages.value.map((m) => [m.role, m.content])).toEqual([['user', 'work'], ['assistant', 'Working.']]);
  });

  it('a refused queueing puts the words back into an empty composer', async () => {
    const d = deps();
    d.chatApi.queueMessage.mockResolvedValueOnce({ succeeded: false, code: 409, errorCode: 'AI_QUEUE_FULL', message: 'full' } as never);
    const onError = vi.fn();
    const turn = holdTurn('r-1');
    const chat = useChatThreads({ ...(d as never), onError, messageQueuePollMs: 0 });
    void chat.send('first');
    await vi.waitFor(() => expect(chat.messageQueue.available.value).toBe(true));

    await chat.send('one more');

    expect(onError).toHaveBeenCalledWith('full', expect.objectContaining({ operation: 'queue', code: 'AI_QUEUE_FULL' }));
    expect(chat.inputText.value).toBe('one more');
    turn.release();
  });

  it('nothing is queued behind a built-in turn', async () => {
    const d = deps();
    const turn = holdTurn(null);
    const chat = useChatThreads({ ...(d as never), messageQueuePollMs: 0 });
    void chat.send('hello');
    await vi.waitFor(() => expect(chat.activeThreadId.value).toBe('t-1'));
    expect(chat.isStreaming.value).toBe(true);
    expect(chat.messageQueue.available.value).toBe(false);

    await chat.send('are you there?');

    expect(d.chatApi.queueMessage).not.toHaveBeenCalled();
    turn.release();
  });

  it('when the queued messages went out after the turn, the conversation is read again to follow the new turn', async () => {
    const d = deps();
    const turn = holdTurn('r-1');
    const chat = useChatThreads({ ...(d as never), messageQueuePollMs: 0 });
    void chat.send('first');
    await vi.waitFor(() => expect(chat.messageQueue.available.value).toBe(true));
    await chat.send('second');
    const reads = d.threadApi.getMessages.mock.calls.length;

    // The server sent it as the next turn by the time the page's turn ended.
    d.setQueue([]);
    turn.release();
    await vi.waitFor(() => expect(chat.isStreaming.value).toBe(false));

    await vi.waitFor(() => expect(d.threadApi.getMessages.mock.calls.length).toBeGreaterThan(reads));
    expect(chat.messageQueue.items.value).toEqual([]);
  });

  it('registers with the component tree it runs in, picked by the conversation on screen', () => {
    const picked: unknown[] = [];
    let chat: ReturnType<typeof useChatThreads> | undefined;
    let code: ReturnType<typeof useChatThreads> | undefined;
    const Child = defineComponent({
      setup: () => {
        const registry = inject(CHAT_THREADS_KEY, null);
        picked.push(pickChatMessageQueue(registry, chat!.messages.value), pickChatMessageQueue(registry, code!.messages.value));
        return () => h('div');
      },
    });
    const Parent = defineComponent({
      setup: () => {
        chat = useChatThreads(deps() as never);
        code = useChatThreads(deps() as never);
        return () => h(Child);
      },
    });
    const app = createApp(Parent);
    app.mount(document.createElement('div'));
    expect(picked[0]).toBe(chat!.messageQueue);
    expect(picked[1]).toBe(code!.messageQueue);
    app.unmount();
  });
});
