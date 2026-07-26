/**
 * useChat - core chat state store.
 *
 * Owns the message list, streaming flag, thread lifecycle and abort control.
 * All message updates are immutable: arrays are replaced, never mutated.
 *
 * ## Transport is the consumer's
 *
 * This composable deliberately performs no network I/O. Applications differ in
 * how they reach the backend (admin gateway, embedded widget with its own
 * origin, a proxy that injects auth), so the transport stays outside and feeds
 * results back in through `addMessage` / `appendDelta` / `updateMessage` /
 * `setStreaming` / `setError`.
 *
 * `send()` still owns the AbortController for the turn, and exposes it as
 * `signal` - pass that to `streamChat()` from `@tnzi/core/services/ai` (or any
 * `fetch`) so `abort()`, `clearThread()`, `loadThread()` and unmounting all
 * actually cancel the in-flight request instead of letting it run to
 * completion in the background.
 *
 * @example
 * ```ts
 * const chat = useChat()
 * chat.send(text)
 * const assistantId = chat.messages.value.at(-1)!.id
 * await streamChat({
 *   url, body, signal: chat.signal.value ?? undefined,
 *   onDelta: (t) => chat.appendDelta(assistantId, 'content', t),
 *   onError: (e) => chat.setError(e instanceof Error ? e : new Error(String(e))),
 *   onDone: () => { chat.updateMessage(assistantId, { isStreaming: false, status: 'done' }); chat.setStreaming(false) },
 * })
 * ```
 */

import {
  ref,
  shallowRef,
  readonly,
  computed,
  onScopeDispose,
  type Ref,
  type DeepReadonly,
  type ComputedRef,
} from 'vue';
import { scheduleFrame } from '@/lib/scheduleFrame';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type MessageRole = 'user' | 'assistant' | 'system' | 'tool';

export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  cachedInputTokens: number;
  cacheCreationTokens: number;
}

export interface ToolCallInfo {
  name: string;
  durationMs?: number | null;
  input?: string | null;
  output?: string | null;
}

export interface MessageAttachment {
  type: 'image' | 'file';
  url?: string | null;
  fileId?: string | null;
  fileName?: string | null;
  base64Data?: string | null;
  mediaType?: string | null;
}

export interface ChatMessage {
  id: string;
  role: MessageRole;
  content: string;
  /** Reasoning / thinking process text (e.g., DeepSeek-R1). */
  reasoning?: string | null;
  toolCalls?: ToolCallInfo[] | null;
  attachments?: MessageAttachment[] | null;
  usage?: TokenUsage | null;
  /** Agent name (for multi-agent / Handoff scenarios). */
  agentName?: string | null;
  /** ISO date string. */
  createdAt: string;
  /** Whether this message is still being streamed. */
  isStreaming?: boolean;
  /** Model used to generate this message. */
  model?: string | null;
  /** Parent message ID for branching. */
  parentId?: string | null;
  /** Feedback rating (true = positive, false = negative, null = none). */
  feedbackRating?: boolean | null;
  /** Lifecycle status - 'error'/'stopped' render dedicated UI in TChatApp/ChatMessage. */
  status?: 'streaming' | 'done' | 'stopped' | 'error';
  /** Error message shown when status === 'error'. */
  error?: string | null;
}

export interface UseChatOptions {
  /** Initial thread ID to load. */
  threadId?: string | null;
  /** Called whenever `setError` records a non-null error. */
  onError?: (error: Error) => void;
  /** Called by `send()` right after the placeholder messages are appended. */
  onStreamStart?: () => void;
  /** Called by `abort()` once the in-flight turn has been cancelled. */
  onStreamEnd?: () => void;
}

export interface UseChatReturn {
  /** Reactive message list. */
  messages: DeepReadonly<Ref<readonly ChatMessage[]>>;
  /** Whether an assistant response is currently streaming. */
  isStreaming: DeepReadonly<Ref<boolean>>;
  /** Current error (null if none). */
  error: DeepReadonly<Ref<Error | null>>;
  /** Current thread ID (may be set after first message). */
  currentThreadId: DeepReadonly<Ref<string | null>>;
  /** Current agent name (for Handoff tracking). */
  currentAgentName: DeepReadonly<Ref<string | null>>;
  /**
   * AbortSignal for the turn started by the most recent `send()`, or null when
   * nothing is in flight. Hand this to the transport so cancellation works.
   */
  signal: ComputedRef<AbortSignal | null>;
  /** Number of messages. */
  messageCount: ComputedRef<number>;
  /** Send a user message. */
  send: (content: string, files?: MessageAttachment[]) => void;
  /** Regenerate (re-request) a specific assistant message. */
  regenerate: (messageId: string) => void;
  /** Abort the current streaming request. */
  abort: () => void;
  /** Clear all messages and reset thread. */
  clearThread: () => void;
  /** Load an existing thread's messages. */
  loadThread: (threadId: string, messages: ChatMessage[]) => void;
  /** Cleanup resources (abort controller, etc.). */
  dispose: () => void;

  // -- Streaming integration API ------------------------------------
  // The composable does not own SSE transport (see `send` docstring
  // for rationale). The following hooks let a consumer's transport
  // adapter feed deltas back into the reactive message list without
  // re-implementing the message store.

  /** Append a user-authored or assistant-authored message verbatim. */
  addMessage: (message: ChatMessage) => void;
  /** Patch an existing message by ID. rAF-batched. */
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
  /** Append a delta chunk to a message's `content` or `reasoning`
   *  field, preserving prior content. Synchronous so deltas never
   *  race or coalesce. */
  appendDelta: (id: string, field: 'content' | 'reasoning', delta: string) => void;
  /** Set the global `isStreaming` flag explicitly. Useful when the
   *  consumer owns the streaming lifecycle (e.g. external SSE
   *  client) and needs to mirror its state into the composable. */
  setStreaming: (value: boolean) => void;
  /** Record (or clear) the current transport error. A non-null value also
   *  invokes `options.onError`. */
  setError: (error: Error | null) => void;
  /** Record the agent currently answering, so subsequent assistant
   *  placeholders are stamped with it (Handoff scenarios). */
  setAgentName: (name: string | null) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let idCounter = 0;

function generateId(): string {
  idCounter += 1;
  return `msg_${Date.now()}_${idCounter}`;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useChat(options: UseChatOptions = {}): UseChatReturn {
  const messages = ref<readonly ChatMessage[]>([]) as Ref<readonly ChatMessage[]>;
  const isStreaming = ref(false);
  const error = ref<Error | null>(null);
  const currentThreadId = ref<string | null>(options.threadId ?? null);
  const currentAgentName = ref<string | null>(null);

  const abortController = shallowRef<AbortController | null>(null);
  const signal = computed<AbortSignal | null>(() => abortController.value?.signal ?? null);

  const messageCount = computed(() => messages.value.length);

  // -- Immutable message helpers --

  function addMessage(message: ChatMessage): void {
    messages.value = [...messages.value, message];
  }

  /* Pending patches are keyed by message id and merged, not overwritten.
     A single-slot buffer silently dropped everything but the last call in a
     frame, and during streaming several patches routinely land in the same
     frame: two different messages updating together, or one message getting
     `status` and then `usage`. */
  const pendingUpdates = new Map<string, Partial<ChatMessage>>();
  let updatePending = false;

  function flushMessageUpdate(): void {
    updatePending = false;
    if (pendingUpdates.size === 0) return;
    const batch = new Map(pendingUpdates);
    pendingUpdates.clear();
    messages.value = messages.value.map((msg) => {
      const patch = batch.get(msg.id);
      return patch ? { ...msg, ...patch } : msg;
    });
  }

  function updateMessage(id: string, updates: Partial<ChatMessage>): void {
    const existing = pendingUpdates.get(id);
    pendingUpdates.set(id, existing ? { ...existing, ...updates } : { ...updates });
    if (!updatePending) {
      updatePending = true;
      scheduleFrame(flushMessageUpdate);
    }
  }

  function removeMessage(id: string): void {
    messages.value = messages.value.filter((msg) => msg.id !== id);
  }

  function appendDelta(id: string, field: 'content' | 'reasoning', delta: string): void {
    if (delta === '') return;
    messages.value = messages.value.map((msg) => {
      if (msg.id !== id) return msg;
      if (field === 'content') {
        return { ...msg, content: msg.content + delta };
      }
      return { ...msg, reasoning: (msg.reasoning ?? '') + delta };
    });
  }

  function setStreaming(value: boolean): void {
    isStreaming.value = value;
  }

  function setError(nextError: Error | null): void {
    error.value = nextError;
    if (nextError) options.onError?.(nextError);
  }

  function setAgentName(name: string | null): void {
    currentAgentName.value = name;
  }

  // -- Public API --

  function send(content: string, files?: MessageAttachment[]): void {
    if (isStreaming.value) return;
    error.value = null;

    // Add user message
    const userMessage: ChatMessage = {
      id: generateId(),
      role: 'user',
      content,
      attachments: files ?? null,
      createdAt: new Date().toISOString(),
    };
    addMessage(userMessage);

    // Create assistant placeholder
    const assistantMessage: ChatMessage = {
      id: generateId(),
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
      isStreaming: true,
      agentName: currentAgentName.value,
    };
    addMessage(assistantMessage);

    isStreaming.value = true;
    // The consumer's transport must forward `signal` (see the file header) or
    // abort() cannot actually stop the request.
    abortController.value = new AbortController();
    options.onStreamStart?.();
  }

  function regenerate(messageId: string): void {
    const idx = messages.value.findIndex((m) => m.id === messageId);
    if (idx === -1) return;
    const target = messages.value[idx];
    if (target?.role !== 'assistant') return;

    // Find the preceding user message
    let userMessage: ChatMessage | null = null;
    for (let i = idx - 1; i >= 0; i--) {
      if (messages.value[i]?.role === 'user') {
        userMessage = messages.value[i] ?? null;
        break;
      }
    }
    if (!userMessage) return;

    // Remove the old assistant message
    removeMessage(messageId);

    // Re-send with the same content
    send(userMessage.content, userMessage.attachments ?? undefined);
  }

  function abort(): void {
    if (abortController.value) {
      abortController.value.abort();
      abortController.value = null;
    }

    // Mark any streaming messages as done
    messages.value = messages.value.map((msg) =>
      msg.isStreaming ? { ...msg, isStreaming: false } : msg,
    );
    isStreaming.value = false;
    options.onStreamEnd?.();
  }

  function clearThread(): void {
    abort();
    messages.value = [];
    currentThreadId.value = null;
    currentAgentName.value = null;
    error.value = null;
  }

  function loadThread(threadId: string, threadMessages: ChatMessage[]): void {
    abort();
    currentThreadId.value = threadId;
    messages.value = [...threadMessages];
    error.value = null;
  }

  function dispose(): void {
    abort();
    pendingUpdates.clear();
  }

  /* Tearing down the owning component (route change, thread switch that
     re-creates the store) must cancel the in-flight turn; otherwise the
     transport keeps streaming to completion against a store nobody reads. */
  onScopeDispose(dispose, true);

  return {
    messages: readonly(messages) as DeepReadonly<Ref<readonly ChatMessage[]>>,
    isStreaming: readonly(isStreaming),
    error: readonly(error),
    currentThreadId: readonly(currentThreadId),
    currentAgentName: readonly(currentAgentName),
    signal,
    messageCount,
    send,
    regenerate,
    abort,
    clearThread,
    loadThread,
    dispose,
    addMessage,
    updateMessage,
    appendDelta,
    setStreaming,
    setError,
    setAgentName,
  };
}
