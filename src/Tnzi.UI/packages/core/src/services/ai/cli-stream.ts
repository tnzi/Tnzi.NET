/**
 * SSE consumer for external CLI agent run event streams.
 *
 * Separate from `streamChat` because the two protocols differ in the two things
 * that matter: this endpoint is a **GET** (a run is addressed by id, not posted
 * as a body) and it takes a **`fromSequence`** so a reconnect can resume
 * precisely instead of replaying or dropping. The live events themselves carry
 * no sequence number: read the persisted history (`/messages`) first and resume
 * from {@link lastCliRunSequence} of what it returned.
 *
 * Event `type` arrives as the enum name (`'Text'`, `'ToolUse'`, ...), as on every
 * other endpoint.
 */

import type { CliAgentEvent, CliRunMessageDto } from './cli';
import { StreamRequestError } from './stream-request-error';

/** Options for consuming an external run's event stream. */
export interface CliRunStreamOptions {
  /** Full URL of the SSE endpoint (build it with `useAdminCliRunApi().streamUrl`). */
  url: string;
  /** Additional headers (Authorization, etc.). */
  headers?: Record<string, string>;
  /** Called for every normalised event. */
  onEvent?: (event: CliAgentEvent) => void;
  /** Called for text deltas only - append directly, do NOT add newlines. */
  onText?: (text: string) => void;
  /** Called for reasoning/thinking deltas. */
  onThinking?: (text: string) => void;
  /**
   * Called when the server closes the stream cleanly. That is how a run that
   * reached a terminal status ends, but also how the server answers a run
   * that does not exist or that the caller may not see (see
   * {@link streamCliRun}).
   */
  onDone?: () => void;
  /** Called on transport failure. */
  onError?: (error: Error) => void;
  /** AbortSignal to stop consuming. */
  signal?: AbortSignal;
}

/** Result of consuming an external run's stream. */
export interface CliRunStreamResult {
  /** Accumulated text. */
  text: string;
  /** Accumulated reasoning text. */
  thinking: string;
  /** Every event received, in arrival order. */
  events: CliAgentEvent[];
  /**
   * Whether the server closed the stream normally. Not proof that the run
   * exists: see {@link streamCliRun} for the empty stream.
   */
  completed: boolean;
  /** Transport error, if any. */
  error?: Error | null;
}

/**
 * Consume an external CLI agent run's SSE stream.
 *
 * The server closes the stream when the run reaches a terminal status, so a
 * clean end-of-body is the completion signal - there is no `[DONE]` sentinel.
 *
 * A clean end with no events is ambiguous on purpose. The server answers a run
 * id that does not exist, or that the caller may not see, with an empty 200
 * stream (the response headers are already written, and a different answer
 * would tell a prober which ids are real). Resuming a finished run from its
 * last sequence ends the same way. When `events` is empty and the caller needs
 * to tell these apart, read the run with `useCliRunApi().getById` (or
 * `useAdminCliRunApi().getById`): a missing or hidden run answers 404 there.
 *
 * @example
 * ```ts
 * const runApi = useAdminCliRunApi(httpClient);
 * await streamCliRun({
 *   url: runApi.streamUrl(runId, lastSequence),
 *   headers: { Authorization: `Bearer ${token}` },
 *   onText: (t) => { output.value += t; },
 * });
 * ```
 */
export async function streamCliRun(
  options: CliRunStreamOptions,
): Promise<CliRunStreamResult> {
  const result: CliRunStreamResult = {
    text: '',
    thinking: '',
    events: [],
    completed: false,
    error: null,
  };

  try {
    const response = await fetch(options.url, {
      method: 'GET',
      headers: { Accept: 'text/event-stream', ...options.headers },
      signal: options.signal,
    });

    if (!response.ok) {
      const detail = await response.text().catch(() => response.statusText);
      // Typed like streamChat's failure so the documented 401 refresh dance
      // (branch on `status`, `http.refreshAccessToken()`, retry once) applies
      // to this feed too instead of leaving the status buried in the message.
      const error = new StreamRequestError(response.status, detail, 'CLI run stream');
      result.error = error;
      options.onError?.(error);
      return result;
    }

    if (!response.body) {
      const error = new Error('Response body is null - streaming not supported');
      result.error = error;
      options.onError?.(error);
      return result;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    const processFrame = (frame: string): void => {
      for (const line of frame.split('\n')) {
        if (!line || line.startsWith(':')) continue;
        if (!line.startsWith('data: ')) continue;

        let event: CliAgentEvent;
        try {
          event = JSON.parse(line.slice(6)) as CliAgentEvent;
        } catch {
          // Malformed frame - skip it rather than aborting the whole stream.
          continue;
        }

        result.events.push(event);
        options.onEvent?.(event);

        if (event.type === 'Text' && event.content) {
          result.text += event.content;
          options.onText?.(event.content);
        } else if (event.type === 'Thinking' && event.content) {
          result.thinking += event.content;
          options.onThinking?.(event.content);
        }
      }
    };

    try {
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        const frames = buffer.split('\n\n');
        buffer = frames.pop()!;
        for (const frame of frames) processFrame(frame);
      }

      // A server that closes without a trailing blank line parks its LAST frame
      // here; dropping it makes the run look truncated at exactly the moment it
      // finished.
      buffer += decoder.decode();
      if (buffer.trim()) processFrame(buffer);

      result.completed = true;
      options.onDone?.();
      return result;
    } finally {
      // Release the body on every exit path, otherwise the connection stays
      // locked until GC.
      reader.cancel().catch(() => {});
    }
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err));
    if (error.name !== 'AbortError') {
      result.error = error;
      options.onError?.(error);
    }
    return result;
  }
}

/**
 * Highest sequence number among replayed messages, i.e. where a live stream
 * should resume from.
 *
 * Returns `0` for an empty history so the caller can pass it straight through.
 */
export function lastCliRunSequence(messages: readonly CliRunMessageDto[]): number {
  return messages.reduce((max, m) => (m.sequence > max ? m.sequence : max), 0);
}
