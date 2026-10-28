import { describe, it, expect, vi } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { stashWelcomeMessage, takeWelcomeMessage } from '../../src/headless/welcome-message';
import { useAttachmentUrl } from '../../src/headless/useAttachmentUrl';
import {
  formatToolDuration,
  formatToolInput,
  readableToolName,
  toolCallState,
  toolInputSummary,
} from '../../src/components/reasoning/tool-call-status';
import { defaultToolFileHref, resolveToolRenderer } from '../../src/components/chat/chat-render-context';
import { formatResetTime } from '../../src/utils/format';
import { toChatMessage } from '../../src/adapters/index';

/**
 * The pure parts of showing a conversation with an external CLI agent: stored messages
 * (statuses, attachments, tool input kept as the tool sent it), tool calls, renderer
 * matching, file links, reset times, and the message kept across sign-in.
 */

describe('stored messages', () => {
  it('keep a tool call input as the tool sent it, keys and all', () => {
    const message = toChatMessage({
      id: 'a1',
      role: 'assistant',
      content: 'done',
      creationTime: 'x',
      toolCalls: JSON.stringify([{ id: 'c1', name: 'Write', status: 'completed', input: { Path: 'a.txt', URL: 'x', nested: { Key: 1 } } }]),
    });
    expect(message.toolCalls?.[0].input).toEqual({ Path: 'a.txt', URL: 'x', nested: { Key: 1 } });
    expect(message.toolCalls?.[0].status).toBe('completed');
  });

  it('still read the legacy PascalCase shape', () => {
    const message = toChatMessage({
      id: 'a1', role: 'assistant', content: '', creationTime: 'x',
      toolCalls: JSON.stringify([{ Name: 'Read', DurationMs: 3, IsSuccess: true }]),
    });
    expect(message.toolCalls).toEqual([{ name: 'Read', durationMs: 3, isSuccess: true }]);
  });

  it('carry their outcome, order, metadata and uploads', () => {
    const limited = toChatMessage({
      id: 'a2', role: 'assistant', content: '', creationTime: 'x', order: 4,
      status: 'usage_limit', usageLimitResetsAt: '2026-10-19T15:00:00Z',
      attachments: [{ id: 'img', origin: 'ToolResult', mediaType: 'image/png', size: 2 }],
    });
    expect(limited).toMatchObject({ status: 'usage_limit', usageLimitResetsAt: '2026-10-19T15:00:00Z', order: 4 });
    expect(limited.attachments).toBeUndefined();

    const asked = toChatMessage({
      id: 'u1', role: 'user', content: 'look', creationTime: 'x', metadata: { targets: ['pc-1'] },
      attachments: [
        { id: 'p', origin: 'Upload', mediaType: 'image/png', fileName: 'a.png', size: 2 },
        { id: 'd', origin: 'Upload', mediaType: 'application/pdf', fileName: 'b.pdf', size: 9 },
      ],
    });
    expect(asked.metadata).toEqual({ targets: ['pc-1'] });
    expect(asked.attachments).toEqual([
      { type: 'image', attachmentId: 'p', fileName: 'a.png', mediaType: 'image/png', size: 2 },
      { type: 'file', attachmentId: 'd', fileName: 'b.pdf', mediaType: 'application/pdf', size: 9 },
    ]);

    expect(toChatMessage({ id: 'a3', role: 'assistant', content: 'half', creationTime: 'x', status: 'failed' }).status).toBe('error');
    expect(toChatMessage({ id: 'a4', role: 'assistant', content: 'half', creationTime: 'x', status: 'stopped' }).status).toBe('stopped');
  });
});

describe('tool calls', () => {
  it('trust a reported status over the default success flag', () => {
    expect(toolCallState({ name: 'x', status: 'running', isSuccess: true }, true)).toBe('running');
    expect(toolCallState({ name: 'x', status: 'running', isSuccess: true }, false)).toBe('pending');
    expect(toolCallState({ name: 'x', status: 'failed', isSuccess: true }, false)).toBe('failed');
    expect(toolCallState({ name: 'x', durationMs: 5 }, false)).toBe('completed');
    expect(toolCallState({ name: 'x' }, true)).toBe('running');
  });

  it('read as names, summaries and durations', () => {
    expect(readableToolName('mcp__acme__take_screenshot')).toBe('Take Screenshot');
    expect(readableToolName('readFile')).toBe('Read File');
    expect(toolInputSummary({ count: 2, path: '/var/log/app.log' })).toBe('/var/log/app.log');
    expect(toolInputSummary({ count: 2 })).toBeNull();
    expect(formatToolInput({ a: 1 })).toBe('{\n  "a": 1\n}');
    expect(formatToolInput(undefined)).toBeNull();
    expect(formatToolDuration(1234)).toBe('1.2s');
    expect(formatToolDuration(0)).toBeNull();
  });

  it('find their renderer by exact name, then by the name after an MCP prefix', () => {
    const shot = { name: 'shot' };
    const exact = { name: 'exact' };
    const renderers = { take_screenshot: shot, mcp__acme__take_screenshot: exact };
    expect(resolveToolRenderer('mcp__acme__take_screenshot', renderers)).toBe(exact);
    expect(resolveToolRenderer('mcp__other__take_screenshot', renderers)).toBe(shot);
    expect(resolveToolRenderer('take_screenshot', renderers)).toBe(shot);
    expect(resolveToolRenderer('Bash', renderers)).toBeUndefined();
    expect(resolveToolRenderer(undefined, renderers)).toBeUndefined();
  });

  it('link a handed-over file only where a browser can open it', () => {
    expect(defaultToolFileHref({ uri: 'https://files.example.com/a.pdf' })).toBe('https://files.example.com/a.pdf');
    expect(defaultToolFileHref({ uri: '/files/a.pdf' })).toBe('/files/a.pdf');
    expect(defaultToolFileHref({ uri: '//evil.example/a' })).toBeNull();
    expect(defaultToolFileHref({ uri: 'storage://files/9' })).toBeNull();
    expect(defaultToolFileHref({ uri: 'javascript:alert(1)' })).toBeNull();
  });
});

describe('reset times', () => {
  it('show the time of day when it is today, else the day too', () => {
    const now = new Date('2026-10-19T09:00:00');
    expect(formatResetTime(new Date('2026-10-19T15:30:00').toISOString(), now, 'en-GB')).toBe('15:30');
    expect(formatResetTime(new Date('2026-10-21T15:30:00').toISOString(), now, 'en-GB')).toContain('15:30');
    expect(formatResetTime(new Date('2026-10-21T15:30:00').toISOString(), now, 'en-GB')).not.toBe('15:30');
    expect(formatResetTime(null)).toBeNull();
    expect(formatResetTime('not a date')).toBeNull();
  });
});

describe('the welcome message', () => {
  it('is taken once', () => {
    sessionStorage.clear();
    expect(stashWelcomeMessage('  hello  ')).toBe(true);
    expect(takeWelcomeMessage()).toBe('hello');
    expect(takeWelcomeMessage()).toBeNull();
  });

  it('is not kept when there is nothing to keep, and expires', () => {
    sessionStorage.clear();
    expect(stashWelcomeMessage('   ')).toBe(false);
    stashWelcomeMessage('old');
    expect(takeWelcomeMessage(-1)).toBeNull();
  });

  it('survives an unreadable entry', () => {
    sessionStorage.setItem('tnzi-ui-ai:welcome-message', '{broken');
    expect(takeWelcomeMessage()).toBeNull();
    expect(stashWelcomeMessage('x', null)).toBe(false);
  });
});

describe('useAttachmentUrl', () => {
  it('shows the URL of the current id only', async () => {
    const id = ref<string | null>('a');
    let resolveA: ((url: string) => void) | undefined;
    const resolver = vi.fn((attachmentId: string) =>
      attachmentId === 'a'
        ? new Promise<string>((r) => (resolveA = r))
        : Promise.resolve(`blob:${attachmentId}`));
    const scope = effectScope();
    const state = scope.run(() => useAttachmentUrl(() => id.value, () => resolver))!;
    expect(state.loading.value).toBe(true);

    id.value = 'b';
    await nextTick();
    await vi.waitFor(() => expect(state.url.value).toBe('blob:b'));
    resolveA?.('blob:a');
    await Promise.resolve();
    expect(state.url.value).toBe('blob:b');
    scope.stop();
  });

  it('fails without a resolver, and on an unreadable attachment', async () => {
    const scope = effectScope();
    const none = scope.run(() => useAttachmentUrl(() => 'a', () => undefined))!;
    expect(none.failed.value).toBe(true);
    const gone = scope.run(() => useAttachmentUrl(() => 'a', () => async () => null))!;
    await vi.waitFor(() => expect(gone.failed.value).toBe(true));
    scope.stop();
  });
});
