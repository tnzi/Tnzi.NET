import { describe, it, expect } from 'vitest';
import {
  groupCliEvents,
  pendingToolCount,
  type CliTimelineEvent,
  type CliTimelineToolRow,
} from '../../src/components/cli/timeline';

/**
 * The wire format for an external CLI run is a flat, append-only sequence.
 * Everything that makes it readable happens in `groupCliEvents`, so that is
 * where the tests are - the rendering on top of it is trivial by comparison.
 */

function text(content: string): CliTimelineEvent {
  return { type: 'Text', content } as CliTimelineEvent;
}

function toolUse(tool: string, callId: string, input?: unknown): CliTimelineEvent {
  return {
    type: 'ToolUse',
    tool,
    callId,
    input: input as Record<string, unknown>,
  } as CliTimelineEvent;
}

function toolResult(callId: string, output: string, tool = 'unknown'): CliTimelineEvent {
  return { type: 'ToolResult', tool, callId, output } as CliTimelineEvent;
}

describe('groupCliEvents', () => {
  it('concatenates consecutive text deltas into one row', () => {
    // Text arrives token by token. One row per delta would be thousands of rows
    // for a single reply.
    const rows = groupCliEvents([text('Hel'), text('lo '), text('world')]);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ kind: 'text', content: 'Hello world' });
  });

  it('starts a new row when a different event interrupts the text', () => {
    const rows = groupCliEvents([
      text('before '),
      { type: 'Status', status: 'running' } as CliTimelineEvent,
      text('after'),
    ]);

    expect(rows.map((r) => r.kind)).toEqual(['text', 'status', 'text']);
    expect((rows[0] as { content: string }).content).toBe('before ');
    expect((rows[2] as { content: string }).content).toBe('after');
  });

  it('keeps text and reasoning in separate rows', () => {
    const rows = groupCliEvents([
      text('answer'),
      { type: 'Thinking', content: 'hmm' } as CliTimelineEvent,
      { type: 'Thinking', content: '...' } as CliTimelineEvent,
    ]);

    expect(rows.map((r) => r.kind)).toEqual(['text', 'thinking']);
    expect((rows[1] as { content: string }).content).toBe('hmm...');
  });

  it('pairs a tool result onto its call instead of adding a second row', () => {
    // The wire reports the two halves as independent events. Rendering them
    // verbatim makes every tool appear twice and leaves the reader to correlate
    // them by eye.
    const rows = groupCliEvents([
      toolUse('read_file', 'call-1', { path: 'a.ts' }),
      toolResult('call-1', 'file contents'),
    ]);

    expect(rows).toHaveLength(1);
    const tool = rows[0] as CliTimelineToolRow;
    expect(tool.tool).toBe('read_file');
    expect(tool.output).toBe('file contents');
    expect(tool.settled).toBe(true);
  });

  it('interleaves concurrent tool calls onto the right rows', () => {
    // Providers issue several calls before any result comes back, and the
    // results do not have to arrive in call order.
    const rows = groupCliEvents([
      toolUse('read_file', 'a'),
      toolUse('run_tests', 'b'),
      toolResult('b', 'all green'),
      toolResult('a', 'contents'),
    ]);

    expect(rows).toHaveLength(2);
    expect(rows[0] as CliTimelineToolRow).toMatchObject({ tool: 'read_file', output: 'contents' });
    expect(rows[1] as CliTimelineToolRow).toMatchObject({ tool: 'run_tests', output: 'all green' });
  });

  it('does not write a second result onto a row whose call id was reused', () => {
    const rows = groupCliEvents([
      toolUse('grep', 'call-1'),
      toolResult('call-1', 'first'),
      toolResult('call-1', 'second'),
    ]);

    expect(rows).toHaveLength(2);
    expect((rows[0] as CliTimelineToolRow).output).toBe('first');
    expect((rows[1] as CliTimelineToolRow).output).toBe('second');
  });

  it('still shows a result whose call was never seen', () => {
    // Happens whenever a run is replayed from a sequence number that lands
    // between the two halves of one call. Dropping it would silently hide output.
    const rows = groupCliEvents([toolResult('orphan', 'output from before the replay', 'grep')]);

    expect(rows).toHaveLength(1);
    expect(rows[0] as CliTimelineToolRow).toMatchObject({
      tool: 'grep',
      output: 'output from before the replay',
      settled: true,
    });
  });

  it('leaves an unanswered call pending', () => {
    const rows = groupCliEvents([toolUse('slow_thing', 'call-1')]);

    expect((rows[0] as CliTimelineToolRow).settled).toBe(false);
    expect(pendingToolCount(rows)).toBe(1);
  });

  it('hides logs unless asked', () => {
    const events = [text('hi'), { type: 'Log', content: 'spawned pid 1234' } as CliTimelineEvent];

    expect(groupCliEvents(events).map((r) => r.kind)).toEqual(['text']);
    expect(groupCliEvents(events, { includeLogs: true }).map((r) => r.kind)).toEqual([
      'text',
      'log',
    ]);
  });

  it('reads arguments from the persisted inputJson shape', () => {
    // Persisted history carries `inputJson`; live events carry `input`. A
    // consumer replaying history then attaching the stream holds both.
    const rows = groupCliEvents([
      { type: 'ToolUse', tool: 'grep', callId: 'c', inputJson: '{"pattern":"todo"}' } as CliTimelineEvent,
    ]);

    expect((rows[0] as CliTimelineToolRow).input).toEqual({ pattern: 'todo' });
  });

  it('treats unparseable or non-object arguments as absent', () => {
    // Malformed arguments must not take the timeline down with them, and a JSON
    // scalar is valid JSON but not an argument object - rendering it as one
    // would produce `0: 'a'` style rows.
    const rows = groupCliEvents([
      { type: 'ToolUse', tool: 'a', callId: '1', inputJson: '{ not json' } as CliTimelineEvent,
      { type: 'ToolUse', tool: 'b', callId: '2', inputJson: '"just a string"' } as CliTimelineEvent,
    ]);

    expect((rows[0] as CliTimelineToolRow).input).toBeNull();
    expect((rows[1] as CliTimelineToolRow).input).toBeNull();
  });

  it('drops event types it does not understand rather than rendering them raw', () => {
    // The backend downgrades anything it does not recognise to `Log`, so a value
    // arriving here means the two sides are on different versions - and a
    // half-understood row is worse than a missing one.
    const rows = groupCliEvents([
      text('hi'),
      { type: 'SomethingNewerBackendsSend' } as unknown as CliTimelineEvent,
    ]);

    expect(rows.map((r) => r.kind)).toEqual(['text']);
  });

  it('surfaces errors as their own row', () => {
    const rows = groupCliEvents([{ type: 'Error', content: 'exit code 1' } as CliTimelineEvent]);

    expect(rows[0]).toMatchObject({ kind: 'error', content: 'exit code 1' });
  });
});
