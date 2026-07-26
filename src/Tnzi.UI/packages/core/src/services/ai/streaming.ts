/**
 * AI Streaming Utilities - SSE stream consumer for Tnzi.NET AI chat endpoints.
 *
 * Provides correct SSE frame parsing, preventing common bugs such as:
 * - Adding newlines between text deltas (causes "one word per line" rendering)
 * - Not buffering incomplete SSE frames (causes JSON parse failures)
 * - Missing [DONE] signal handling
 */

import type { ChatStreamEvent, ChatRequestDto } from './types';

/** Options for streaming chat */
export interface ChatStreamOptions {
  /** Full URL to the streaming endpoint (use getChatStreamUrl() to build) */
  url: string;
  /** Chat request body */
  body: ChatRequestDto;
  /** Additional headers (Authorization, etc.) */
  headers?: Record<string, string>;
  /** Called for each text delta - append directly, DO NOT add newlines */
  onDelta?: (text: string) => void;
  /** Called for each reasoning delta (thinking process) */
  onReasoningDelta?: (text: string) => void;
  /** Called for each parsed event (all event types) */
  onEvent?: (event: ChatStreamEvent) => void;
  /** Called when a tool call starts (for loading UI) */
  onToolCall?: (toolNames: string[]) => void;
  /** Called when agent switches (Handoff scenario) */
  onAgentSwitch?: (agentName: string) => void;
  /** Called when the stream completes (isDone=true or [DONE]) */
  onDone?: (event: ChatStreamEvent) => void;
  /** Called on error (stream error or network error) */
  onError?: (error: Error | ChatStreamEvent) => void;
  /** AbortSignal to cancel the stream */
  signal?: AbortSignal;
}

/** Result returned after the stream completes */
export interface ChatStreamResult {
  /** Full accumulated text response */
  text: string;
  /** Full accumulated reasoning text */
  reasoning: string;
  /** Thread ID (available after first event) */
  threadId?: string | null;
  /** Final event (isDone=true) */
  finalEvent?: ChatStreamEvent | null;
  /** Whether the stream completed normally */
  completed: boolean;
  /** Error if stream failed */
  error?: Error | null;
}

/**
 * Consume an SSE chat stream from the Tnzi.NET AI backend.
 *
 * This function correctly handles:
 * - SSE frame parsing (data: {...}\n\n delimiters)
 * - Incomplete frame buffering across chunk boundaries
 * - [DONE] termination signal
 * - Comment lines (: heartbeat)
 * - Text delta accumulation without unwanted newlines
 *
 * @example
 * ```ts
 * const chatApi = useChatApi(httpClient);
 * const result = await streamChat({
 *   url: chatApi.getChatStreamUrl(),
 *   body: { message: 'Hello', threadId },
 *   headers: { Authorization: `Bearer ${token}` },
 *   onDelta: (text) => { messageContent.value += text; },
 *   onDone: (event) => { threadId = event.threadId; },
 * });
 * ```
 */
export async function streamChat(options: ChatStreamOptions): Promise<ChatStreamResult> {
  const result: ChatStreamResult = {
    text: '',
    reasoning: '',
    threadId: null,
    finalEvent: null,
    completed: false,
    error: null,
  };

  try {
    const response = await fetch(options.url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
        ...options.headers,
      },
      body: JSON.stringify(options.body),
      signal: options.signal,
    });

    if (!response.ok) {
      const errorText = await response.text().catch(() => response.statusText);
      const error = new Error(`Stream request failed: ${response.status} ${errorText}`);
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

    /**
     * Process one complete SSE frame. Returns true when the `[DONE]`
     * termination signal was seen and the caller should stop reading.
     */
    const processFrame = (frame: string): boolean => {
      // Each frame can have multiple lines (e.g., event: + data:)
      // We only care about lines starting with "data: "
      for (const line of frame.split('\n')) {
        // Skip empty lines and comment lines (: heartbeat)
        if (!line || line.startsWith(':')) continue;
        if (!line.startsWith('data: ')) continue;

        const data = line.slice(6);

        // [DONE] is the SSE termination signal
        if (data === '[DONE]') return true;

        // Parse the JSON event
        let event: ChatStreamEvent;
        try {
          event = JSON.parse(data) as ChatStreamEvent;
        } catch {
          // Malformed JSON - skip this event
          continue;
        }

        // Track threadId from first event that has it
        if (event.threadId && !result.threadId) {
          result.threadId = event.threadId;
        }

        // Dispatch callbacks based on event content
        options.onEvent?.(event);

        if (event.delta) {
          result.text += event.delta;
          options.onDelta?.(event.delta);
        }

        if (event.reasoningDelta) {
          result.reasoning += event.reasoningDelta;
          options.onReasoningDelta?.(event.reasoningDelta);
        }

        if (event.isToolCall) {
          options.onToolCall?.(event.toolCallNames ?? []);
        }

        if (event.agentName) {
          options.onAgentSwitch?.(event.agentName);
        }

        if (event.isError) {
          // Record it as well as announcing it: `result.error` is documented as
          // "error if stream failed", and a caller that awaits the result
          // instead of wiring onError used to see a clean, empty success.
          result.error ??= new Error(event.errorMessage ?? 'Stream reported an error');
          options.onError?.(event);
        }

        if (event.isDone) {
          result.finalEvent = event;
          result.threadId = event.threadId ?? result.threadId;
          options.onDone?.(event);
          // Don't return immediately - continue reading until [DONE] or stream end
          // This allows the SSE connection to close cleanly
        }
      }
      return false;
    };

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        // SSE frames are separated by \n\n. Split and process complete frames.
        // Keep the last segment in buffer (it may be an incomplete frame).
        const frames = buffer.split('\n\n');
        buffer = frames.pop()!;

        for (const frame of frames) {
          if (processFrame(frame)) {
            result.completed = true;
            return result;
          }
        }
      }

      // Flush the decoder and whatever is left in the buffer: a server that
      // closes without a trailing blank line leaves its LAST frame - typically
      // the isDone event or [DONE] itself - parked here, and dropping it made
      // the stream look truncated.
      buffer += decoder.decode();
      if (buffer.trim() && processFrame(buffer)) {
        result.completed = true;
        return result;
      }

      // Stream ended without [DONE] - still mark as completed if we got isDone
      result.completed = result.finalEvent != null;
      return result;
    } finally {
      // Release the body stream on every exit path. Returning early on [DONE]
      // used to leave the reader locked and the connection un-recycled until GC.
      reader.cancel().catch(() => {});
    }
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err));
    // AbortError is expected when signal is aborted
    if (error.name !== 'AbortError') {
      result.error = error;
      options.onError?.(error);
    }
    return result;
  }
}
