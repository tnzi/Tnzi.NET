import { describe, it, expect, afterEach, vi } from 'vitest';
import { streamCliRun } from '../../src/services/ai/cli-stream';
import { CliAgentEventType } from '../../src/services/ai/cli';

/**
 * The live feed of an external CLI run, in the wire shape the server writes:
 * `type` is the enum MEMBER NAME, the same as the REST replay (`/messages`).
 * The stream writer used to emit `"type":0`, so `streamCliRun` never matched
 * 'Text' / 'Thinking' and a consumer comparing against `CliAgentEventType.Error`
 * never saw a failure. The backend side is pinned by the CLI controller tests;
 * this pins what the client does with the agreed shape.
 */
describe('streamCliRun event handling', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function sse(...events: unknown[]): Response {
    const text = events.map((e) => `data: ${JSON.stringify(e)}\n\n`).join('');
    const bytes = new TextEncoder().encode(text);
    return {
      ok: true,
      status: 200,
      statusText: 'OK',
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      body: new ReadableStream<Uint8Array>({
        start(controller) {
          controller.enqueue(bytes);
          controller.close();
        },
      }),
    } as unknown as Response;
  }

  it('accumulates text and thinking from member-name event types', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sse(
          { type: 'Status', status: 'running' },
          { type: 'Thinking', content: 'Let me look. ' },
          { type: 'Text', content: 'Hello, ' },
          { type: 'Text', content: 'world.' },
          { type: 'Error', content: 'tool failed' },
        ),
      ),
    );
    const onText = vi.fn();

    const result = await streamCliRun({ url: 'http://localhost/api/ai/cli-runs/r1/stream', onText });

    expect(result.text).toBe('Hello, world.');
    expect(result.thinking).toBe('Let me look. ');
    expect(onText).toHaveBeenCalledTimes(2);
    expect(result.events.some((e) => e.type === CliAgentEventType.Error)).toBe(true);
    expect(result.completed).toBe(true);
  });
});
