import { describe, it, expect } from 'vitest';
import { groupMessages } from '../../src/headless/use-message-group';
import type { ChatMessage } from '../../src/headless/useChat';

function msg(id: string, role: ChatMessage['role'], content = ''): ChatMessage {
  return { id, role, content, createdAt: '2026-01-01T00:00:00Z' };
}

describe('groupMessages', () => {
  it('should return empty array for empty input', () => {
    expect(groupMessages([])).toEqual([]);
  });

  it('should create a human group for a user message', () => {
    const groups = groupMessages([msg('u1', 'user', 'Hello')]);
    expect(groups).toHaveLength(1);
    expect(groups[0]?.type).toBe('human');
    expect(groups[0]?.messages).toHaveLength(1);
    expect(groups[0]?.id).toBe('u1');
  });

  it('should create an assistant group for an assistant message', () => {
    const groups = groupMessages([msg('a1', 'assistant', 'Hi')]);
    expect(groups).toHaveLength(1);
    expect(groups[0]?.type).toBe('assistant');
  });

  it('should merge consecutive assistant and tool messages', () => {
    const groups = groupMessages([
      msg('a1', 'assistant', 'Let me check...'),
      msg('t1', 'tool', 'Result: 42'),
      msg('a2', 'assistant', 'The answer is 42.'),
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0]?.type).toBe('assistant');
    expect(groups[0]?.messages).toHaveLength(3);
    expect(groups[0]?.id).toBe('a1');
  });

  it('should separate user messages into individual groups', () => {
    const groups = groupMessages([
      msg('u1', 'user', 'First'),
      msg('u2', 'user', 'Second'),
    ]);
    expect(groups).toHaveLength(2);
    expect(groups[0]?.type).toBe('human');
    expect(groups[1]?.type).toBe('human');
  });

  it('should handle a typical conversation flow', () => {
    const groups = groupMessages([
      msg('u1', 'user', 'Hello'),
      msg('a1', 'assistant', 'Hi!'),
      msg('u2', 'user', 'How are you?'),
      msg('a2', 'assistant', 'I am fine'),
      msg('t1', 'tool', 'Weather: sunny'),
      msg('a3', 'assistant', 'It is sunny today.'),
    ]);
    expect(groups).toHaveLength(4);
    expect(groups[0]?.type).toBe('human');
    expect(groups[1]?.type).toBe('assistant');
    expect(groups[1]?.messages).toHaveLength(1);
    expect(groups[2]?.type).toBe('human');
    expect(groups[3]?.type).toBe('assistant');
    expect(groups[3]?.messages).toHaveLength(3); // a2 + t1 + a3
  });

  it('should create a system group for system messages', () => {
    const groups = groupMessages([
      msg('s1', 'system', 'System prompt'),
      msg('u1', 'user', 'Hello'),
    ]);
    expect(groups).toHaveLength(2);
    expect(groups[0]?.type).toBe('system');
    expect(groups[1]?.type).toBe('human');
  });

  it('should not merge system messages with adjacent assistant messages', () => {
    const groups = groupMessages([
      msg('a1', 'assistant', 'Response'),
      msg('s1', 'system', 'System info'),
      msg('a2', 'assistant', 'More response'),
    ]);
    expect(groups).toHaveLength(3);
    expect(groups[0]?.type).toBe('assistant');
    expect(groups[1]?.type).toBe('system');
    expect(groups[2]?.type).toBe('assistant');
  });

  it('should handle tool messages at the start', () => {
    const groups = groupMessages([
      msg('t1', 'tool', 'Tool output'),
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0]?.type).toBe('assistant');
  });

  it('should use first message id as group id', () => {
    const groups = groupMessages([
      msg('a1', 'assistant', 'First'),
      msg('t1', 'tool', 'Tool'),
    ]);
    expect(groups[0]?.id).toBe('a1');
  });
});
