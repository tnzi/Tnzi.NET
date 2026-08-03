/**
 * `useChatThreads` - the whole conversation loop against a Tnzi AI backend.
 *
 * ## Relationship to the transport rule
 *
 * This package's rule is "transport stays outside" (`useChat` / `TChatApp` own
 * state, never I/O) and it still holds for the COMPONENTS. This hook is an
 * explicit, opt-in exception in the same spirit as `createTnziChat`: a product
 * talking to the framework's own AI module should not have to re-derive the
 * streaming wiring, and every one that did ended up with a near-identical
 * ~250-line copy that then drifted.
 *
 * Choose per product:
 *   - default backend, no special routing  → this hook
 *   - own transport, custom protocol, BYO  → `useChat` + wire it yourself
 *
 * ## What it actually buys you
 *
 * The parts that are easy to get subtly wrong, all of which came from a working
 * implementation rather than from imagination:
 *
 *   - **Optimistic sidebar entry** on the first turn of a new conversation, so
 *     the user sees their context appear immediately - AND its rollback when
 *     the stream fails before the backend committed a thread.
 *   - **Temp-id reconciliation**: local ids are swapped for the persisted ones
 *     the moment the backend reports them, so feedback / regenerate address
 *     real rows. Post-stream finalisation writes to the *live* ids, which is
 *     why they are tracked separately from the ids the turn started with.
 *   - **Abort that leaves no message stuck streaming** - cancelling mid-answer
 *     must clear `isStreaming` on the row too, or the caret blinks forever.
 *
 * Errors are reported through `onError` rather than a toast: this package owns
 * no notification system, and which surface an error belongs on is the
 * product's call.
 */
import { ref, shallowRef, getCurrentScope, onScopeDispose, type Ref } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { streamChat, type useChatApi, type useThreadApi } from '@tnzi/core/services/ai';
import { createPagedQuery } from '@tnzi/core/types';
import type { ChatMessage } from './useChat';
import type { ThreadItem } from '../components/chat/TThreadList.vue';
import { toChatMessages, toThreadItem, toThreadItems } from '../adapters/index';

export interface UseChatThreadsOptions {
  /** The wired client - supplies the bearer token for the stream request. */
  http: HttpClient;
  /** `useThreadApi(http)`. */
  threadApi: ReturnType<typeof useThreadApi>;
  /** `useChatApi(http)` - used for the stream URL. */
  chatApi: ReturnType<typeof useChatApi>;
  /**
   * Which agent the conversation is pinned to, read per turn.
   *
   * Worth setting: with no agent the backend runs a bare provider/model call,
   * which also means an external-CLI binding can never apply - the routing
   * facade keys off the agent id and treats "no agent" as always-built-in.
   */
  agentId?: () => string | undefined | null;
  /** Threads fetched per page of the sidebar list. Default 50. */
  pageSize?: number;
  /** Title shown for a thread the backend has not named yet. */
  untitledLabel?: string;
  /** How many messages to load when opening a thread. Default 100. */
  messageLimit?: number;
  /** Surface a failure to the user. Without one, failures are silent. */
  onError?: (message: string) => void;
}

export interface UseChatThreadsReturn {
  threads: Ref<ThreadItem[]>;
  activeThreadId: Ref<string | undefined>;
  messages: Ref<ChatMessage[]>;
  isStreaming: Ref<boolean>;
  inputText: Ref<string>;
  /** (Re)load the sidebar list. */
  loadThreads: () => Promise<void>;
  /** Open a thread and load its messages. No-op when already active. */
  selectThread: (id: string) => Promise<void>;
  /** Drop back to the empty state without creating anything server-side. */
  newChat: () => void;
  deleteThread: (id: string) => Promise<void>;
  /** Send a turn and stream the answer. Resolves when the turn settles. */
  send: (content: string) => Promise<void>;
  /** Cancel an in-flight turn. */
  abort: () => void;
  /** Patch one rendered message (feedback, edits). */
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
}

export function useChatThreads(options: UseChatThreadsOptions): UseChatThreadsReturn {
  const { http, threadApi, chatApi } = options;
  const pageSize = options.pageSize ?? 50;
  const messageLimit = options.messageLimit ?? 100;
  const untitled = options.untitledLabel ?? 'New chat';

  const threads = shallowRef<ThreadItem[]>([]);
  const activeThreadId = ref<string | undefined>(undefined);
  const messages = shallowRef<ChatMessage[]>([]);
  const isStreaming = ref(false);
  const inputText = ref('');

  let abortController: AbortController | null = null;
  /**
   * Increments on every turn AND on every abort, so a turn can tell whether it
   * is still the current one when its `await streamChat(...)` finally returns.
   *
   * Without it: abort mid-answer, send again immediately, and the FIRST turn's
   * tail (`isStreaming = false`, the finalising `updateMessage`) lands after the
   * second turn has already started - clearing the streaming state of a turn
   * that is still running. `abortController` alone cannot express this: the
   * second turn replaces it, so the first turn has nothing left to compare
   * against.
   */
  let turnSeq = 0;
  let idCounter = 0;
  const newId = () => `m_${Date.now()}_${++idCounter}`;
  const newTempThreadId = () => `pending_${Date.now()}_${++idCounter}`;

  const fail = (message: string) => options.onError?.(message);

  function updateMessage(id: string, patch: Partial<ChatMessage>): void {
    messages.value = messages.value.map((m) => (m.id === id ? { ...m, ...patch } : m));
  }

  function replaceMessageId(oldId: string, realId: string): void {
    if (!realId || oldId === realId) return;
    messages.value = messages.value.map((m) => (m.id === oldId ? { ...m, id: realId } : m));
  }

  /** First line of the turn, for the optimistic sidebar row. */
  function previewTitle(content: string): string {
    const trimmed = content.trim().replace(/\s+/g, ' ');
    return trimmed.length > 40 ? `${trimmed.slice(0, 40)}…` : trimmed || untitled;
  }

  function abort(): void {
    turnSeq += 1;
    if (abortController) {
      abortController.abort();
      abortController = null;
    }
    if (isStreaming.value) {
      // Clear the flag on the row too - otherwise the caret keeps blinking on a
      // message nothing is writing to any more.
      messages.value = messages.value.map((m) => (m.isStreaming ? { ...m, isStreaming: false } : m));
      isStreaming.value = false;
    }
  }

  async function loadThreads(): Promise<void> {
    const result = await threadApi.getList(createPagedQuery(1, pageSize));
    if (result.succeeded && result.data) {
      threads.value = toThreadItems(result.data.items ?? [], untitled);
    } else {
      fail(result.message || 'Could not load conversations');
    }
  }

  async function selectThread(id: string): Promise<void> {
    if (id === activeThreadId.value) return;
    abort();
    activeThreadId.value = id;
    const result = await threadApi.getDetail(id, messageLimit);
    if (result.succeeded && result.data) {
      messages.value = toChatMessages(result.data.messages ?? []);
    } else {
      fail(result.message || 'Could not load this conversation');
    }
  }

  function newChat(): void {
    abort();
    activeThreadId.value = undefined;
    messages.value = [];
  }

  async function deleteThread(id: string): Promise<void> {
    const result = await threadApi.delete(id);
    if (!result.succeeded) {
      fail(result.message || 'Could not delete the conversation');
      return;
    }
    threads.value = threads.value.filter((t) => t.id !== id);
    if (activeThreadId.value === id) newChat();
  }

  async function send(content: string): Promise<void> {
    if (!content.trim() || isStreaming.value) return;

    const turn = ++turnSeq;
    /** Whether this turn is still the one the hook is running. */
    const isCurrent = () => turn === turnSeq;

    // Optimistic sidebar entry for a brand-new conversation: the user should see
    // their context appear at once, not after the first token arrives.
    let pendingThreadId: string | null = null;
    if (!activeThreadId.value) {
      pendingThreadId = newTempThreadId();
      threads.value = [
        { id: pendingThreadId, title: previewTitle(content), updatedAt: new Date().toISOString() },
        ...threads.value,
      ];
    }

    const userId = newId();
    const assistantId = newId();
    const now = new Date().toISOString();
    messages.value = [
      ...messages.value,
      { id: userId, role: 'user', content, createdAt: now },
      { id: assistantId, role: 'assistant', content: '', reasoning: '', createdAt: now, isStreaming: true },
    ];
    inputText.value = '';
    isStreaming.value = true;

    abortController = new AbortController();
    const headers: Record<string, string> = {};
    const token = http.getAccessToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;

    let bufferedText = '';
    let bufferedReasoning = '';
    let streamFailed = false;
    // The turn's ids change under us once the backend reports the persisted
    // ones, so finalisation has to write to the LIVE ids, not the initial ones.
    let liveUserId = userId;
    let liveAssistantId = assistantId;

    const agentId = options.agentId?.() || undefined;

    const result = await streamChat({
      url: chatApi.getChatStreamUrl(),
      body: {
        message: content,
        threadId: activeThreadId.value ?? null,
        ...(agentId ? { agentId } : {}),
      },
      headers,
      signal: abortController.signal,
      onDelta: (text) => {
        bufferedText += text;
        updateMessage(liveAssistantId, { content: bufferedText });
      },
      onReasoningDelta: (text) => {
        bufferedReasoning += text;
        updateMessage(liveAssistantId, { reasoning: bufferedReasoning });
      },
      onDone: (event) => {
        // Defence in depth: aborting should stop the stream before this fires,
        // but a late `done` frame writing `activeThreadId` would drag the user
        // out of the conversation they just opened.
        if (!isCurrent()) return;
        // The backend may have created the thread for this turn.
        if (event.threadId && !activeThreadId.value) {
          activeThreadId.value = event.threadId;
          if (pendingThreadId) {
            threads.value = threads.value.map((t) =>
              t.id === pendingThreadId
                ? toThreadItem(
                    { id: event.threadId!, title: previewTitle(content), lastActivityTime: new Date().toISOString() },
                    untitled,
                  )
                : t,
            );
            pendingThreadId = null;
          }
          // Refresh in the background so server-side state (an auto-generated
          // title, for one) replaces the local guess.
          void loadThreads();
        }
        // Swap local ids for persisted ones so feedback / regenerate address
        // real rows.
        if (event.userMessageId) {
          replaceMessageId(liveUserId, event.userMessageId);
          liveUserId = event.userMessageId;
        }
        if (event.assistantMessageId) {
          replaceMessageId(liveAssistantId, event.assistantMessageId);
          liveAssistantId = event.assistantMessageId;
        }
      },
      onError: (err) => {
        streamFailed = true;
        const msg = err instanceof Error ? err.message : (err.errorMessage ?? 'Stream failed');
        updateMessage(liveAssistantId, { content: `${bufferedText}\n\n_Error: ${msg}_` });
      },
    });

    // On failure the row already carries the error text `onError` wrote. Writing
    // the stream result over it would replace the only thing telling the user
    // what went wrong with an empty message - a silent failure that looks like
    // the assistant simply had nothing to say.
    // Superseded turn (aborted, or the user already started another one): its
    // tail must not touch shared state. `isStreaming = false` here would clear
    // the flag of the turn that is currently running.
    if (!isCurrent()) return;

    updateMessage(
      liveAssistantId,
      streamFailed
        ? { isStreaming: false }
        : {
            content: result.text || bufferedText,
            reasoning: result.reasoning || bufferedReasoning || null,
            isStreaming: false,
          },
    );
    isStreaming.value = false;
    abortController = null;

    // Roll the optimistic row back if the stream died before the backend
    // committed a thread - a sidebar entry for a conversation that does not
    // exist is worse than none.
    if (streamFailed && pendingThreadId) {
      threads.value = threads.value.filter((t) => t.id !== pendingThreadId);
    }
  }

  // Guarded: the hook is usable outside a component (a store, a test), where an
  // unguarded `onScopeDispose` only emits a Vue warning and registers nothing.
  if (getCurrentScope()) onScopeDispose(() => abort());

  return {
    threads,
    activeThreadId,
    messages,
    isStreaming,
    inputText,
    loadThreads,
    selectThread,
    newChat,
    deleteThread,
    send,
    abort,
    updateMessage,
  };
}
