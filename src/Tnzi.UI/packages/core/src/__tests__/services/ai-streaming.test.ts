import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { streamChat } from '../../services/ai/streaming';
import type { ChatStreamOptions } from '../../services/ai/streaming';

// ---------------------------------------------------------------------------
// Helpers - build SSE byte streams from text frames
// ---------------------------------------------------------------------------

/** Encode a string into a Uint8Array (UTF-8). */
function encode(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}

/** Build an SSE frame: `data: <json>\n\n` */
function sseFrame(data: Record<string, unknown> | string): string {
  const payload = typeof data === 'string' ? data : JSON.stringify(data);
  return `data: ${payload}\n\n`;
}

/** Build a [DONE] terminator. */
function sseDone(): string {
  return 'data: [DONE]\n\n';
}

/** Build a heartbeat comment. */
function sseComment(text = 'heartbeat'): string {
  return `: ${text}\n\n`;
}

/**
 * Create a mock Response with a ReadableStream that yields the given chunks.
 * Each chunk is a raw string that gets encoded to Uint8Array.
 */
function mockStreamResponse(chunks: string[], status = 200): Response {
  let index = 0;
  const stream = new ReadableStream<Uint8Array>({
    pull(controller) {
      if (index < chunks.length) {
        controller.enqueue(encode(chunks[index]!));
        index++;
      } else {
        controller.close();
      }
    },
  });

  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    body: stream,
    headers: new Headers(),
    text: async () => 'error body',
  } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('streamChat', () => {
  let fetchSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function defaultOptions(overrides?: Partial<ChatStreamOptions>): ChatStreamOptions {
    return {
      url: 'http://localhost/api/chat/stream',
      body: { message: 'Hello' },
      ...overrides,
    };
  }

  // ----- Basic SSE parsing -----

  it('should parse a single text delta event', async () => {
    const deltas: string[] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'Hello' }),
        sseFrame({ delta: ' world' }),
        sseFrame({ isDone: true, threadId: 'thread-1' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(
      defaultOptions({ onDelta: (t) => deltas.push(t) }),
    );

    expect(result.text).toBe('Hello world');
    expect(result.completed).toBe(true);
    expect(result.threadId).toBe('thread-1');
    expect(result.error).toBeNull();
    expect(deltas).toEqual(['Hello', ' world']);
  });

  it('should accumulate reasoning deltas separately', async () => {
    const reasoningDeltas: string[] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ reasoningDelta: 'Let me think' }),
        sseFrame({ reasoningDelta: '...' }),
        sseFrame({ delta: 'Answer is 42' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(
      defaultOptions({ onReasoningDelta: (t) => reasoningDeltas.push(t) }),
    );

    expect(result.reasoning).toBe('Let me think...');
    expect(result.text).toBe('Answer is 42');
    expect(reasoningDeltas).toEqual(['Let me think', '...']);
  });

  it('should capture threadId from first event that has it', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'a' }),
        sseFrame({ delta: 'b', threadId: 'tid-1' }),
        sseFrame({ delta: 'c', threadId: 'tid-2' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());
    // First event with threadId wins
    expect(result.threadId).toBe('tid-1');
  });

  // ----- Event callbacks -----

  it('should call onEvent for every parsed event', async () => {
    const events: unknown[] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'x' }),
        sseFrame({ isDone: true }),
        sseDone(),
      ]),
    );

    await streamChat(defaultOptions({ onEvent: (e) => events.push(e) }));

    expect(events).toHaveLength(2);
  });

  it('should call onToolCall when isToolCall is true', async () => {
    const toolCalls: string[][] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ isToolCall: true, toolCallNames: ['search', 'fetch'] }),
        sseDone(),
      ]),
    );

    await streamChat(defaultOptions({ onToolCall: (names) => toolCalls.push(names) }));

    expect(toolCalls).toEqual([['search', 'fetch']]);
  });

  it('should call onToolCall with empty array when toolCallNames is null', async () => {
    const toolCalls: string[][] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ isToolCall: true }),
        sseDone(),
      ]),
    );

    await streamChat(defaultOptions({ onToolCall: (names) => toolCalls.push(names) }));

    expect(toolCalls).toEqual([[]]);
  });

  it('should call onAgentSwitch when agentName is present', async () => {
    const agents: string[] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ agentName: 'research-agent' }),
        sseFrame({ delta: 'result' }),
        sseDone(),
      ]),
    );

    await streamChat(defaultOptions({ onAgentSwitch: (name) => agents.push(name) }));

    expect(agents).toEqual(['research-agent']);
  });

  it('should call onDone when isDone event arrives', async () => {
    let doneEvent: unknown = null;
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'hi' }),
        sseFrame({ isDone: true, threadId: 't-1', usage: { inputTokens: 10, outputTokens: 5, totalTokens: 15, cachedInputTokens: 0, cacheCreationTokens: 0 } }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions({ onDone: (e) => { doneEvent = e; } }));

    expect(doneEvent).not.toBeNull();
    expect(result.finalEvent).not.toBeNull();
    expect(result.finalEvent?.isDone).toBe(true);
  });

  it('should call onError for error events', async () => {
    const errors: unknown[] = [];
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ isError: true, errorMessage: 'rate limit', errorCode: 'RATE_LIMIT' }),
        sseDone(),
      ]),
    );

    await streamChat(defaultOptions({ onError: (e) => errors.push(e) }));

    expect(errors).toHaveLength(1);
  });

  // ----- SSE frame buffering -----

  it('should handle incomplete frames split across chunks', async () => {
    // Split a single SSE frame across two chunks
    const json = JSON.stringify({ delta: 'buffered' });
    const fullFrame = `data: ${json}\n\n`;
    const splitPoint = Math.floor(fullFrame.length / 2);

    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        fullFrame.slice(0, splitPoint),
        fullFrame.slice(splitPoint),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('buffered');
    expect(result.completed).toBe(true);
  });

  it('should handle multiple frames in a single chunk', async () => {
    const combined =
      sseFrame({ delta: 'one' }) +
      sseFrame({ delta: 'two' }) +
      sseFrame({ delta: 'three' }) +
      sseDone();

    fetchSpy.mockResolvedValue(mockStreamResponse([combined]));

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('onetwothree');
    expect(result.completed).toBe(true);
  });

  it('should handle JSON split across chunk boundaries', async () => {
    // The JSON itself is split mid-token
    const part1 = 'data: {"delta":"spl';
    const part2 = 'it"}\n\n';

    fetchSpy.mockResolvedValue(
      mockStreamResponse([part1, part2, sseDone()]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('split');
  });

  // ----- Comment lines and empty lines -----

  it('should skip SSE comment lines (heartbeats)', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseComment('heartbeat'),
        sseFrame({ delta: 'data' }),
        sseComment('keepalive'),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('data');
  });

  it('should skip empty lines gracefully', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        '\n\n',
        sseFrame({ delta: 'ok' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('ok');
  });

  // ----- [DONE] termination -----

  it('should return completed=true on [DONE]', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'hi' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.completed).toBe(true);
    expect(result.text).toBe('hi');
  });

  it('should mark completed=true when isDone received but no [DONE]', async () => {
    // Stream ends naturally (reader.done) after isDone event
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'hi' }),
        sseFrame({ isDone: true }),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.completed).toBe(true);
    expect(result.finalEvent).not.toBeNull();
  });

  it('should mark completed=false when stream ends without isDone or [DONE]', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([sseFrame({ delta: 'partial' })]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.completed).toBe(false);
    expect(result.text).toBe('partial');
  });

  // ----- Malformed JSON -----

  it('should skip malformed JSON events gracefully', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        'data: {broken json\n\n',
        sseFrame({ delta: 'valid' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    expect(result.text).toBe('valid');
    expect(result.completed).toBe(true);
  });

  // ----- HTTP error responses -----

  it('should handle non-OK HTTP response', async () => {
    let errorCaught: unknown = null;
    fetchSpy.mockResolvedValue(
      mockStreamResponse([], 429),
    );

    const result = await streamChat(
      defaultOptions({ onError: (e) => { errorCaught = e; } }),
    );

    expect(result.completed).toBe(false);
    expect(result.error).not.toBeNull();
    expect(result.error?.message).toContain('429');
    expect(errorCaught).not.toBeNull();
  });

  it('should handle null response body', async () => {
    fetchSpy.mockResolvedValue({
      ok: true,
      status: 200,
      body: null,
      headers: new Headers(),
    } as unknown as Response);

    const result = await streamChat(defaultOptions());

    expect(result.completed).toBe(false);
    expect(result.error?.message).toContain('null');
  });

  // ----- Network errors -----

  it('should handle fetch rejection (network error)', async () => {
    let errorCaught: unknown = null;
    fetchSpy.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await streamChat(
      defaultOptions({ onError: (e) => { errorCaught = e; } }),
    );

    expect(result.completed).toBe(false);
    expect(result.error?.message).toBe('Failed to fetch');
    expect(errorCaught).not.toBeNull();
  });

  it('should not call onError for AbortError', async () => {
    let errorCaught = false;
    const abortError = new DOMException('The operation was aborted', 'AbortError');
    fetchSpy.mockRejectedValue(abortError);

    const result = await streamChat(
      defaultOptions({ onError: () => { errorCaught = true; } }),
    );

    expect(result.completed).toBe(false);
    expect(errorCaught).toBe(false);
    // AbortError is expected - no error set on result
  });

  // ----- AbortSignal -----

  it('should pass AbortSignal to fetch', async () => {
    const controller = new AbortController();
    fetchSpy.mockResolvedValue(mockStreamResponse([sseDone()]));

    await streamChat(defaultOptions({ signal: controller.signal }));

    expect(fetchSpy).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ signal: controller.signal }),
    );
  });

  it('should not inject any implicit timeout signal (streaming is exempt from HttpClient timeouts)', async () => {
    // streamChat bypasses HttpClient entirely - a long-lived SSE stream must
    // never be killed by the default request timeout. Lock in that the only
    // signal fetch receives is the caller-provided one (here: none).
    fetchSpy.mockResolvedValue(mockStreamResponse([sseDone()]));

    await streamChat(defaultOptions());

    const callArgs = fetchSpy.mock.calls[0]![1]!;
    expect(callArgs.signal).toBeUndefined();
  });

  // ----- Headers -----

  it('should merge custom headers with defaults', async () => {
    fetchSpy.mockResolvedValue(mockStreamResponse([sseDone()]));

    await streamChat(
      defaultOptions({ headers: { Authorization: 'Bearer tok123' } }),
    );

    const callArgs = fetchSpy.mock.calls[0]![1]!;
    expect(callArgs.headers).toMatchObject({
      'Content-Type': 'application/json',
      Accept: 'text/event-stream',
      Authorization: 'Bearer tok123',
    });
  });

  it('should send body as JSON string', async () => {
    fetchSpy.mockResolvedValue(mockStreamResponse([sseDone()]));

    await streamChat(
      defaultOptions({ body: { message: 'test', threadId: 't-1' } }),
    );

    const callArgs = fetchSpy.mock.calls[0]![1]!;
    expect(callArgs.body).toBe(JSON.stringify({ message: 'test', threadId: 't-1' }));
    expect(callArgs.method).toBe('POST');
  });

  // ----- threadId update from isDone -----

  it('should update threadId from isDone event even if already set', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'a', threadId: 'temp-id' }),
        sseFrame({ isDone: true, threadId: 'final-id' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions());

    // isDone event overrides threadId
    expect(result.threadId).toBe('final-id');
  });

  // ----- Complex scenario: full conversation -----

  it('should handle a realistic multi-event conversation stream', async () => {
    const deltas: string[] = [];
    const reasoningDeltas: string[] = [];
    const toolCalls: string[][] = [];
    let doneReceived = false;

    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseComment('connected'),
        sseFrame({ reasoningDelta: 'Thinking about the question' }),
        sseFrame({ reasoningDelta: '...' }),
        sseFrame({ isToolCall: true, toolCallNames: ['web_search'] }),
        sseFrame({ delta: 'Based on my research, ' }),
        sseFrame({ delta: 'the answer is ' }),
        sseFrame({ delta: '42.' }),
        sseFrame({
          isDone: true,
          threadId: 'conv-123',
          finishReason: 'stop',
          model: 'claude-3',
          usage: { inputTokens: 100, outputTokens: 50, totalTokens: 150, cachedInputTokens: 0, cacheCreationTokens: 0 },
        }),
        sseDone(),
      ]),
    );

    const result = await streamChat(defaultOptions({
      onDelta: (t) => deltas.push(t),
      onReasoningDelta: (t) => reasoningDeltas.push(t),
      onToolCall: (names) => toolCalls.push(names),
      onDone: () => { doneReceived = true; },
    }));

    expect(result.text).toBe('Based on my research, the answer is 42.');
    expect(result.reasoning).toBe('Thinking about the question...');
    expect(result.threadId).toBe('conv-123');
    expect(result.completed).toBe(true);
    expect(result.finalEvent?.finishReason).toBe('stop');
    expect(result.finalEvent?.model).toBe('claude-3');
    expect(result.error).toBeNull();

    expect(deltas).toEqual(['Based on my research, ', 'the answer is ', '42.']);
    expect(reasoningDeltas).toEqual(['Thinking about the question', '...']);
    expect(toolCalls).toEqual([['web_search']]);
    expect(doneReceived).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Stream lifecycle: trailing frame, error recording, reader release
// ---------------------------------------------------------------------------

describe('streamChat lifecycle', () => {
  let fetchSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function options(overrides?: Partial<ChatStreamOptions>): ChatStreamOptions {
    return { url: 'http://localhost/api/chat/stream', body: { message: 'Hi' }, ...overrides };
  }

  it('processes a trailing frame that has no terminating blank line', async () => {
    // A server that closes right after its last event leaves that event parked
    // in the buffer; dropping it made a finished stream look truncated.
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ delta: 'partial' }),
        'data: {"isDone":true,"threadId":"t-9","finishReason":"stop"}',
      ]),
    );

    const result = await streamChat(options());

    expect(result.text).toBe('partial');
    expect(result.finalEvent?.finishReason).toBe('stop');
    expect(result.threadId).toBe('t-9');
    expect(result.completed).toBe(true);
  });

  it('honours a trailing [DONE] with no blank line', async () => {
    fetchSpy.mockResolvedValue(
      mockStreamResponse([sseFrame({ delta: 'x' }), 'data: [DONE]']),
    );

    const result = await streamChat(options());

    expect(result.completed).toBe(true);
  });

  it('records an isError event on result.error, not just via onError', async () => {
    const onError = vi.fn();
    fetchSpy.mockResolvedValue(
      mockStreamResponse([
        sseFrame({ isError: true, errorMessage: 'model refused' }),
        sseDone(),
      ]),
    );

    const result = await streamChat(options({ onError }));

    expect(onError).toHaveBeenCalledTimes(1);
    expect(result.error).toBeInstanceOf(Error);
    expect(result.error?.message).toBe('model refused');
  });

  it('releases the body reader when [DONE] returns early', async () => {
    const cancel = vi.fn(async () => {});
    const chunks = [sseFrame({ delta: 'a' }), sseDone(), sseFrame({ delta: 'never' })];
    let index = 0;
    const reader = {
      read: async () => (index < chunks.length
        ? { done: false, value: new TextEncoder().encode(chunks[index++]!) }
        : { done: true, value: undefined }),
      cancel,
    };
    fetchSpy.mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      body: { getReader: () => reader },
      headers: new Headers(),
      text: async () => '',
    } as unknown as Response);

    const result = await streamChat(options());

    expect(result.completed).toBe(true);
    // Without this the reader stayed locked and the connection un-recycled.
    expect(cancel).toHaveBeenCalledTimes(1);
  });

  it('releases the body reader on normal completion too', async () => {
    const cancel = vi.fn(async () => {});
    let served = false;
    const reader = {
      read: async () => {
        if (served) return { done: true, value: undefined };
        served = true;
        return { done: false, value: new TextEncoder().encode(sseFrame({ isDone: true })) };
      },
      cancel,
    };
    fetchSpy.mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      body: { getReader: () => reader },
      headers: new Headers(),
      text: async () => '',
    } as unknown as Response);

    await streamChat(options());

    expect(cancel).toHaveBeenCalledTimes(1);
  });
});
