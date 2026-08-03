/**
 * SSE consumer for external CLI agent run event streams.
 *
 * Separate from `streamChat` because the two protocols differ in the two things
 * that matter: this endpoint is a **GET** (a run is addressed by id, not posted
 * as a body) and its events carry a **sequence number** so a reconnect can
 * resume precisely instead of replaying or dropping.
 */

import type { CliAgentEvent, CliRunMessageDto } from './cli';

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
  /** Called when the stream ends normally (the run reached a terminal status). */
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
  /** Whether the server closed the stream normally. */
  completed: boolean;
  /** Transport error, if any. */
  error?: Error | null;
}

/**
 * Consume an external CLI agent run's SSE stream.
 *
 * The server closes the stream when the run reaches a terminal status, so a
 * clean end-of-body IS the completion signal - there is no `[DONE]` sentinel.
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
      const error = new Error(`CLI run stream failed: ${response.status} ${detail}`);
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
