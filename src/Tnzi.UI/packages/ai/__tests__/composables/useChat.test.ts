import { describe, it, expect, vi } from 'vitest';
import { useChat } from '../../src/composables/useChat';
import type { ChatMessage } from '../../src/composables/useChat';

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
});
