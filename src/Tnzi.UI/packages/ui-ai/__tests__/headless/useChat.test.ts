import { describe, it, expect, vi } from 'vitest';
import { useChat } from '../../src/headless/useChat';
import type { ChatMessage } from '../../src/headless/useChat';

describe('useChat', () => {
  it('should initialize with empty state', () => {
    const chat = useChat();
    expect(chat.messages.value).toEqual([]);
    expect(chat.isStreaming.value).toBe(false);
    expect(chat.error.value).toBeNull();
    expect(chat.currentThreadId.value).toBeNull();
    expect(chat.currentAgentName.value).toBeNull();
    expect(chat.messageCount.value).toBe(0);
  });

  it('should accept initial threadId from options', () => {
    const chat = useChat({ threadId: 'thread-123' });
    expect(chat.currentThreadId.value).toBe('thread-123');
  });

  it('should add user and assistant messages on send', () => {
    const chat = useChat();
    chat.send('Hello');

    // Should have 2 messages: user + assistant placeholder
    expect(chat.messages.value).toHaveLength(2);
    expect(chat.messages.value[0]?.role).toBe('user');
    expect(chat.messages.value[0]?.content).toBe('Hello');
    expect(chat.messages.value[1]?.role).toBe('assistant');
    expect(chat.messages.value[1]?.content).toBe('');
    expect(chat.messages.value[1]?.isStreaming).toBe(true);
    expect(chat.messageCount.value).toBe(2);
  });

  it('should set isStreaming to true on send', () => {
    const chat = useChat();
    chat.send('Hi');
    expect(chat.isStreaming.value).toBe(true);
  });

  it('should not send while already streaming', () => {
    const chat = useChat();
    chat.send('First');
    chat.send('Second');
    // Only first send should go through
    expect(chat.messages.value).toHaveLength(2);
  });

  it('should call onStreamStart callback on send', () => {
    const onStreamStart = vi.fn();
    const chat = useChat({ onStreamStart });
    chat.send('Hi');
    expect(onStreamStart).toHaveBeenCalledOnce();
  });

  it('should abort and mark streaming messages as done', () => {
    const onStreamEnd = vi.fn();
    const chat = useChat({ onStreamEnd });
    chat.send('Hello');
    expect(chat.isStreaming.value).toBe(true);

    chat.abort();
    expect(chat.isStreaming.value).toBe(false);
    expect(onStreamEnd).toHaveBeenCalledOnce();
    // Assistant message should no longer be streaming
    const assistantMsg = chat.messages.value[1];
    expect(assistantMsg?.isStreaming).toBe(false);
  });

  it('should clear thread state', () => {
    const chat = useChat({ threadId: 'thread-1' });
    chat.send('Hello');
    chat.abort();

    chat.clearThread();
    expect(chat.messages.value).toEqual([]);
    expect(chat.currentThreadId.value).toBeNull();
    expect(chat.currentAgentName.value).toBeNull();
    expect(chat.error.value).toBeNull();
  });

  it('should load existing thread with messages', () => {
    const chat = useChat();
    const existingMessages: ChatMessage[] = [
      { id: '1', role: 'user', content: 'Hi', createdAt: '2026-01-01T00:00:00Z' },
      { id: '2', role: 'assistant', content: 'Hello!', createdAt: '2026-01-01T00:00:01Z' },
    ];

    chat.loadThread('thread-xyz', existingMessages);
    expect(chat.currentThreadId.value).toBe('thread-xyz');
    expect(chat.messages.value).toHaveLength(2);
    expect(chat.messages.value[0]?.content).toBe('Hi');
    expect(chat.messages.value[1]?.content).toBe('Hello!');
  });

  it('should regenerate an assistant message', () => {
    const chat = useChat();
    // Set up a conversation manually via loadThread
    const msgs: ChatMessage[] = [
      { id: 'u1', role: 'user', content: 'What is 2+2?', createdAt: '2026-01-01T00:00:00Z' },
      { id: 'a1', role: 'assistant', content: '4', createdAt: '2026-01-01T00:00:01Z' },
    ];
    chat.loadThread('t1', msgs);

    chat.regenerate('a1');
    // Old assistant message should be removed, new user+assistant pair added
    expect(chat.messages.value).toHaveLength(3); // original user + new user + new assistant
    expect(chat.messages.value[0]?.id).toBe('u1'); // original user stays
    // The last two are the re-sent pair
    const secondUser = chat.messages.value[1];
    const secondAssistant = chat.messages.value[2];
    expect(secondUser?.role).toBe('user');
    expect(secondUser?.content).toBe('What is 2+2?');
    expect(secondAssistant?.role).toBe('assistant');
    expect(secondAssistant?.isStreaming).toBe(true);
  });

  it('should not regenerate a user message', () => {
    const chat = useChat();
    chat.loadThread('t1', [
      { id: 'u1', role: 'user', content: 'Hello', createdAt: '2026-01-01T00:00:00Z' },
    ]);

    chat.regenerate('u1');
    expect(chat.messages.value).toHaveLength(1);
  });

  it('should not regenerate a non-existent message', () => {
    const chat = useChat();
    chat.regenerate('nonexistent');
    expect(chat.messages.value).toEqual([]);
  });

  it('should include attachments in user message', () => {
    const chat = useChat();
    chat.send('See this', [{ type: 'image', url: 'https://example.com/img.png' }]);

    const userMsg = chat.messages.value[0];
    expect(userMsg?.attachments).toHaveLength(1);
    expect(userMsg?.attachments?.[0]?.type).toBe('image');
  });

  it('should use immutable message updates (no mutation)', () => {
    const chat = useChat();
    chat.send('Hello');
    const firstRef = chat.messages.value;
    chat.abort();
    // After abort, messages array should be a new reference
    expect(chat.messages.value).not.toBe(firstRef);
  });

  it('should dispose by aborting', () => {
    const chat = useChat();
    chat.send('Hello');
    chat.dispose();
    expect(chat.isStreaming.value).toBe(false);
  });

  // --- streaming integration API (added 0.2.x) ---

  it('addMessage appends a verbatim message', () => {
    const chat = useChat();
    const msg: ChatMessage = {
      id: 'm1',
      role: 'assistant',
      content: 'verbatim',
      createdAt: '2026-01-01T00:00:00Z',
    };
    chat.addMessage(msg);
    expect(chat.messages.value).toHaveLength(1);
    expect(chat.messages.value[0]?.id).toBe('m1');
    expect(chat.messages.value[0]?.content).toBe('verbatim');
  });

  it('appendDelta appends to content synchronously', () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: 'Hel',
      createdAt: '2026-01-01T00:00:00Z',
      isStreaming: true,
    });
    chat.appendDelta('a1', 'content', 'lo');
    chat.appendDelta('a1', 'content', ' world');
    expect(chat.messages.value[0]?.content).toBe('Hello world');
  });

  it('appendDelta appends to reasoning, treating undefined as empty', () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: '',
      createdAt: '2026-01-01T00:00:00Z',
      isStreaming: true,
    });
    chat.appendDelta('a1', 'reasoning', 'Thinking…');
    chat.appendDelta('a1', 'reasoning', ' done.');
    expect(chat.messages.value[0]?.reasoning).toBe('Thinking… done.');
  });

  it('appendDelta is a no-op for an empty delta', () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: 'kept',
      createdAt: '2026-01-01T00:00:00Z',
    });
    const before = chat.messages.value;
    chat.appendDelta('a1', 'content', '');
    expect(chat.messages.value).toBe(before);
  });

  it('appendDelta ignores unknown message id without error', () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: 'kept',
      createdAt: '2026-01-01T00:00:00Z',
    });
    expect(() => chat.appendDelta('does-not-exist', 'content', 'x')).not.toThrow();
    expect(chat.messages.value[0]?.content).toBe('kept');
  });

  it('updateMessage patches an existing message via rAF', async () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: 'old',
      createdAt: '2026-01-01T00:00:00Z',
      isStreaming: true,
    });
    chat.updateMessage('a1', { content: 'new', isStreaming: false });
    // Wait one animation frame for the batched flush.
    await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    expect(chat.messages.value[0]?.content).toBe('new');
    expect(chat.messages.value[0]?.isStreaming).toBe(false);
  });

  it('setStreaming flips the streaming flag', () => {
    const chat = useChat();
    expect(chat.isStreaming.value).toBe(false);
    chat.setStreaming(true);
    expect(chat.isStreaming.value).toBe(true);
    chat.setStreaming(false);
    expect(chat.isStreaming.value).toBe(false);
  });
  // -------------------------------------------------------------------------
  // Regressions
  // -------------------------------------------------------------------------

  it('merges several same-frame updates for one message instead of keeping only the last', async () => {
    const chat = useChat();
    chat.addMessage({
      id: 'a1',
      role: 'assistant',
      content: 'hi',
      createdAt: '2026-01-01T00:00:00Z',
      isStreaming: true,
    });

    // Streaming routinely emits status then usage inside a single frame; the
    // old single-slot buffer dropped everything but the final call.
    chat.updateMessage('a1', { status: 'done' });
    chat.updateMessage('a1', {
      usage: {
        inputTokens: 1,
        outputTokens: 2,
        totalTokens: 3,
        cachedInputTokens: 0,
        cacheCreationTokens: 0,
      },
    });

    await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    expect(chat.messages.value[0]?.status).toBe('done');
    expect(chat.messages.value[0]?.usage?.totalTokens).toBe(3);
  });

  it('applies same-frame updates to two different messages', async () => {
    const chat = useChat();
    chat.addMessage({ id: 'm1', role: 'assistant', content: '', createdAt: 'x' });
    chat.addMessage({ id: 'm2', role: 'assistant', content: '', createdAt: 'x' });

    chat.updateMessage('m1', { content: 'first' });
    chat.updateMessage('m2', { content: 'second' });

    await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    expect(chat.messages.value[0]?.content).toBe('first');
    expect(chat.messages.value[1]?.content).toBe('second');
  });

  it('exposes an AbortSignal for the in-flight turn', () => {
    const chat = useChat();
    expect(chat.signal.value).toBeNull();

    chat.send('Hello');
    const signal = chat.signal.value;
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(signal?.aborted).toBe(false);
  });

  it('aborts the exposed signal on abort()', () => {
    const chat = useChat();
    chat.send('Hello');
    const signal = chat.signal.value;

    chat.abort();
    expect(signal?.aborted).toBe(true);
    expect(chat.signal.value).toBeNull();
  });

  it('aborts the exposed signal on dispose()', () => {
    const chat = useChat();
    chat.send('Hello');
    const signal = chat.signal.value;

    chat.dispose();
    expect(signal?.aborted).toBe(true);
  });

  it('aborts the exposed signal when the thread is cleared or reloaded', () => {
    const chat = useChat();
    chat.send('Hello');
    const first = chat.signal.value;
    chat.clearThread();
    expect(first?.aborted).toBe(true);

    chat.send('Again');
    const second = chat.signal.value;
    chat.loadThread('t1', []);
    expect(second?.aborted).toBe(true);
  });

  it('records errors and notifies onError', () => {
    const onError = vi.fn();
    const chat = useChat({ onError });
    const err = new Error('stream failed');

    chat.setError(err);
    expect(chat.error.value).toBe(err);
    expect(onError).toHaveBeenCalledWith(err);

    chat.setError(null);
    expect(chat.error.value).toBeNull();
    expect(onError).toHaveBeenCalledTimes(1);
  });

  it('stamps new assistant placeholders with the current agent name', () => {
    const chat = useChat();
    chat.setAgentName('Researcher');
    expect(chat.currentAgentName.value).toBe('Researcher');

    chat.send('Hello');
    expect(chat.messages.value[1]?.agentName).toBe('Researcher');
  });
});
