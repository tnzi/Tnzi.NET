import { describe, it, expect } from 'vitest';
import {
  toChatMessage,
  toChatMessages,
  toMessageRole,
  toThreadItem,
  toThreadItems,
} from '../../src/adapters/index';

describe('toMessageRole', () => {
  it.each(['user', 'assistant', 'system', 'tool'] as const)('passes %s through', (role) => {
    expect(toMessageRole(role)).toBe(role);
  });

  it('is case-insensitive - the backend field is a free-form string', () => {
    expect(toMessageRole('Assistant')).toBe('assistant');
    expect(toMessageRole('USER')).toBe('user');
  });

  /**
   * `AgentThreadMessage.Role` is a plain string server-side, so a value this
   * client does not know about is possible. Rendering it as an ordinary reply
   * beats putting an invalid member into the union and breaking rendering
   * somewhere far from here.
   */
  it.each([null, undefined, '', 'function', 'developer'])(
    'degrades %p to assistant rather than emitting an invalid role',
    (role) => {
      expect(toMessageRole(role as string)).toBe('assistant');
    },
  );
});

describe('toChatMessage', () => {
  const dto = {
    id: 'm1',
    role: 'Assistant',
    content: 'hello',
    creationTime: '2026-08-02T10:00:00Z',
  };

  it('maps the wire shape onto the view model', () => {
    expect(toChatMessage(dto)).toEqual({
      id: 'm1',
      role: 'assistant',
      content: 'hello',
      createdAt: '2026-08-02T10:00:00Z',
      feedbackRating: null,
      // Absent on the wire → null, not undefined (see the rating note below).
      toolCalls: null,
      usage: null,
    });
  });

  it('normalises a missing rating to null rather than undefined', () => {
    // `undefined` would read as "not part of this object" to a consumer doing
    // `'feedbackRating' in message`; null says "no rating yet".
    expect(toChatMessage(dto).feedbackRating).toBeNull();
    expect(toChatMessage({ ...dto, feedbackRating: true }).feedbackRating).toBe(true);
    expect(toChatMessage({ ...dto, feedbackRating: false }).feedbackRating).toBe(false);
  });

  /**
   * These used to be dropped, so a conversation looked complete while streaming
   * and then lost its tool-call blocks and token counts the moment the thread
   * was reopened - a gap nobody reports because it reads as "the history is
   * just shorter". The wire type is a JSON string, the view model wants objects.
   */
  it('parses the JSON-string toolCalls and usage fields', () => {
    const m = toChatMessage({
      ...dto,
      toolCalls: '[{"id":"c1","name":"search"}]',
      usage: '{"promptTokens":10,"completionTokens":5}',
    });
    expect(m.toolCalls).toEqual([{ id: 'c1', name: 'search' }]);
    expect(m.usage).toEqual({ promptTokens: 10, completionTokens: 5 });
  });

  /** A message that cannot be fully understood must still render its text. */
  it.each(['not json', '', 'null', '42', '"a string"'])(
    'degrades unusable %p to null instead of throwing',
    (raw) => {
      const m = toChatMessage({ ...dto, toolCalls: raw, usage: raw });
      expect(m.toolCalls).toBeNull();
      expect(m.usage).toBeNull();
      expect(m.content).toBe('hello');
    },
  );

  it('maps a list', () => {
    expect(toChatMessages([dto, { ...dto, id: 'm2' }]).map((m) => m.id)).toEqual(['m1', 'm2']);
  });
});

describe('toThreadItem', () => {
  const dto = { id: 't1', title: 'Planning', lastActivityTime: '2026-08-02T10:00:00Z' };

  it('maps the wire shape onto the sidebar entry', () => {
    expect(toThreadItem(dto)).toEqual({
      id: 't1',
      title: 'Planning',
      updatedAt: '2026-08-02T10:00:00Z',
    });
  });

  /** A blank row in the history list looks unclickable and says nothing. */
  it.each([null, undefined, ''])('falls back to a placeholder for %p', (title) => {
    expect(toThreadItem({ ...dto, title: title as string }).title).toBe('New chat');
  });

  it('lets the product name its own untitled threads', () => {
    expect(toThreadItem({ ...dto, title: null }, 'Untitled').title).toBe('Untitled');
    expect(toThreadItems([{ ...dto, title: null }], 'Untitled')[0].title).toBe('Untitled');
  });
});
