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
import { streamChat, ChatStreamRequestError, type useChatApi, type useThreadApi } from '@tnzi/core/services/ai';
import { createPagedQuery } from '@tnzi/core/types';
import type { ChatMessage, ToolCallInfo } from './useChat';
import { filesToContentParts, type UploadedAttachment } from './attachment-parts';
import type { ThreadItem } from '../components/chat/TThreadList.vue';
import { toChatMessages, toThreadItem, toThreadItems } from '../adapters/index';

export interface UseChatThreadsOptions {
  /**
   * The wired client - supplies the bearer token for the stream request.
   *
   * The stream is a raw `fetch` (the client's request pipeline cannot carry an
   * SSE body), so it never sees the client's 401 refresh-and-retry. The hook
   * runs that dance itself through `http.refreshAccessToken()` /
   * `http.reportUnauthorized()`: a token that expired while the tab sat idle
   * is refreshed once and the turn retried; a 401 that survives the refresh is
   * reported so the app's session-expired listener moves the user to login.
   */
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
  /**
   * Upload a non-image attachment and return its storage id, so the turn can
   * reference it as a `file` content part (images travel inline as base64 and
   * need nothing). Typically `useStorageApi(http).upload` unwrapped. Without
   * it a non-image file is REFUSED through `onError` and the turn is not sent:
   * the endpoint has no inline form for such a file, and sending the text
   * alone would answer "I don't see a document" to a user who attached one.
   */
  uploadFile?: (file: File) => Promise<UploadedAttachment>;
  /** Per-attachment ceiling in bytes. Default 10 MB. */
  maxAttachmentBytes?: number;
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
  /**
   * Send a turn and stream the answer. Resolves when the turn settles.
   * `files` are the composer's attachments; a turn may be files only.
   */
  send: (content: string, files?: readonly File[]) => Promise<void>;
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
  /**
   * Same idea for `selectThread`: two clicks in a row each await their own
   * `getDetail`, and whichever resolved LAST used to win `messages` - so A's
   * transcript could land under B's active id.
   */
  let selectSeq = 0;
  let idCounter = 0;
  const newId = () => `m_${Date.now()}_${++idCounter}`;
  const PENDING_PREFIX = 'pending_';
  const newTempThreadId = () => `${PENDING_PREFIX}${Date.now()}_${++idCounter}`;
  /**
   * Local ids exist only in this hook: the backend routes on GUIDs, so a
   * `pending_` id 404s on detail and fails model binding on the next stream
   * body. Nothing that talks to the server may ever be handed one.
   */
  const isPendingId = (id: string) => id.startsWith(PENDING_PREFIX);
  /**
   * The optimistic sidebar row of the first turn of a new conversation, until
   * the backend reports the persisted thread id. Hook-scoped rather than
   * turn-scoped so `abort()` and `selectThread()` can see it.
   */
  let pendingThreadId: string | null = null;

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

  /**
   * Drop the optimistic row. A sidebar entry for a conversation that does not
   * exist (the stream failed before the backend committed one) is worse than
   * none; and after an abort the truth is unknown - the backend may well have
   * persisted the thread - so the list is re-fetched to show whatever it did.
   */
  function dropPendingRow(refresh: boolean): void {
    if (!pendingThreadId) return;
    threads.value = threads.value.filter((t) => t.id !== pendingThreadId);
    pendingThreadId = null;
    if (refresh) void loadThreads();
  }

  function abort(refreshThreads = true): void {
    turnSeq += 1;
    if (abortController) {
      abortController.abort();
      abortController = null;
      // The turn's tail returns early once superseded, so its own rollback
      // never runs; the row would otherwise stay as a zombie that the next
      // send does not replace.
      dropPendingRow(refreshThreads);
    }
    if (isStreaming.value) {
      // Clear the flag on the row too - otherwise the caret keeps blinking on a
      // message nothing is writing to any more - and mark it stopped, which is
      // what the renderers key the "Generation stopped" mark off.
      messages.value = messages.value.map((m) =>
        m.isStreaming ? { ...m, isStreaming: false, status: 'stopped' } : m,
      );
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
    // The optimistic row IS the conversation on screen - clicking it is the
    // same as clicking the active one. Aborting the first turn here and then
    // fetching a local id was how `activeThreadId` got poisoned.
    if (isPendingId(id)) return;
    abort();
    const seq = ++selectSeq;
    activeThreadId.value = id;
    const result = await threadApi.getDetail(id, messageLimit);
    // Superseded: another selection (or New chat) happened while this one was
    // in flight. Its transcript belongs to a thread that is no longer open.
    if (seq !== selectSeq || activeThreadId.value !== id) return;
    if (result.succeeded && result.data) {
      messages.value = toChatMessages(result.data.messages ?? []);
    } else {
      fail(result.message || 'Could not load this conversation');
    }
  }

  function newChat(): void {
    abort();
    selectSeq += 1;
    activeThreadId.value = undefined;
    messages.value = [];
  }

  async function deleteThread(id: string): Promise<void> {
    if (isPendingId(id)) return;
    const result = await threadApi.delete(id);
    if (!result.succeeded) {
      fail(result.message || 'Could not delete the conversation');
      return;
    }
    threads.value = threads.value.filter((t) => t.id !== id);
    if (activeThreadId.value === id) newChat();
  }

  async function send(content: string, files: readonly File[] = []): Promise<void> {
    if ((!content.trim() && files.length === 0) || isStreaming.value) return;

    const turn = ++turnSeq;
    /** Whether this turn is still the one the hook is running. */
    const isCurrent = () => turn === turnSeq;

    // Attachments are resolved BEFORE anything on screen moves: a refusal (a
    // file the transport cannot carry, an oversized one, a failed upload)
    // leaves no optimistic row, no user bubble and the draft intact. The
    // composer has already cleared its chips, so the refusal is reported.
    // A text-only turn never awaits here, so its state changes stay
    // synchronous with the call, as they always were.
    let parts: Awaited<ReturnType<typeof filesToContentParts>> = { parts: null, message: content, attachments: [] };
    if (files.length > 0) {
      // Reading / uploading is part of the turn: the stop button shows, and
      // a second send cannot start underneath it.
      isStreaming.value = true;
      try {
        parts = await filesToContentParts(content, files, {
          uploadFile: options.uploadFile,
          maxBytes: options.maxAttachmentBytes,
        });
      } catch (err) {
        if (isCurrent()) isStreaming.value = false;
        fail(err instanceof Error ? err.message : 'The attachment could not be sent');
        return;
      }
      // Stopped (or superseded) while the upload was in flight.
      if (!isCurrent()) return;
    }

    // Optimistic sidebar entry for a brand-new conversation: the user should see
    // their context appear at once, not after the first token arrives.
    if (!activeThreadId.value) {
      pendingThreadId = newTempThreadId();
      threads.value = [
        { id: pendingThreadId, title: previewTitle(content), updatedAt: new Date().toISOString(), pending: true },
        ...threads.value,
      ];
    }

    const userId = newId();
    const assistantId = newId();
    const now = new Date().toISOString();
    messages.value = [
      ...messages.value,
      {
        id: userId,
        role: 'user',
        content,
        createdAt: now,
        ...(parts.attachments.length ? { attachments: parts.attachments } : {}),
      },
      {
        id: assistantId,
        role: 'assistant',
        content: '',
        reasoning: '',
        createdAt: now,
        isStreaming: true,
        status: 'streaming',
      },
    ];
    inputText.value = '';
    isStreaming.value = true;

    abortController = new AbortController();
    const signal = abortController.signal;

    let bufferedText = '';
    let bufferedReasoning = '';
    let streamFailed = false;
    /** What went wrong, for the row's error block. Set together with `streamFailed`. */
    let failure = '';
    /**
     * Set when the stream was refused with a 401. The raw fetch cannot go
     * through the client's refresh path, so the turn handles it below:
     * refresh once, retry once, and only then treat it as a failure.
     */
    let unauthorized = false;
    // The turn's ids change under us once the backend reports the persisted
    // ones, so finalisation has to write to the LIVE ids, not the initial ones.
    let liveUserId = userId;
    let liveAssistantId = assistantId;
    /**
     * Tool activity as the stream reports it: a name-only entry the moment a
     * tool starts (`isToolCall` + `toolCallNames`), filled in with duration and
     * outcome when the detail list arrives after it ran. The same tool called
     * twice is two entries; a detail fills the FIRST still-pending entry of
     * that name.
     */
    let toolCalls: ToolCallInfo[] = [];
    const isPendingCall = (c: ToolCallInfo) => c.isSuccess == null && c.durationMs == null;

    const agentId = options.agentId?.() || undefined;

    const streamOnce = (token: string | null) => streamChat({
      url: chatApi.getChatStreamUrl(),
      // `message` stays alongside `content`: the backend builds the model
      // input from the parts when present, but adds and persists the user
      // turn only from a non-blank `message` (text only - history does not
      // round-trip attachments). A files-only turn sends the attachment
      // names as its message for that reason.
      body: {
        message: parts.message,
        threadId: activeThreadId.value ?? null,
        ...(agentId ? { agentId } : {}),
        ...(parts.parts ? { content: parts.parts } : {}),
      },
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      signal,
      onDelta: (text) => {
        bufferedText += text;
        updateMessage(liveAssistantId, { content: bufferedText });
      },
      onReasoningDelta: (text) => {
        bufferedReasoning += text;
        updateMessage(liveAssistantId, { reasoning: bufferedReasoning });
      },
      onToolCall: (names) => {
        if (!isCurrent()) return;
        toolCalls = [...toolCalls, ...names.map((name) => ({ name }))];
        updateMessage(liveAssistantId, { toolCalls });
      },
      onEvent: (event) => {
        if (!isCurrent() || !event.toolCalls?.length) return;
        let next = toolCalls;
        for (const detail of event.toolCalls) {
          const idx = next.findIndex((c) => c.name === detail.name && isPendingCall(c));
          next = idx === -1
            ? [...next, { ...detail }]
            : next.map((c, i) => (i === idx ? { ...c, ...detail } : c));
        }
        toolCalls = next;
        updateMessage(liveAssistantId, { toolCalls });
      },
      onAgentSwitch: (name) => {
        if (!isCurrent()) return;
        updateMessage(liveAssistantId, { agentName: name });
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
        if (event.usage) updateMessage(liveAssistantId, { usage: event.usage });
      },
      onError: (err) => {
        if (err instanceof ChatStreamRequestError && err.status === 401) {
          // Not a failure yet: the turn decides after the refresh attempt.
          unauthorized = true;
          return;
        }
        streamFailed = true;
        failure = err instanceof Error ? err.message : (err.errorMessage ?? 'Stream failed');
      },
    });

    let result = await streamOnce(http.getAccessToken());

    if (unauthorized && isCurrent()) {
      // The access token expired while the tab sat idle. Refresh through the
      // client's own mutex (shared with any JSON call that hit the same 401)
      // and retry the turn once with the new token, exactly as the client does
      // for its own requests. A failed refresh has already notified the
      // session-expired handlers; a refreshed token that is still refused is
      // reported here, because nothing else on this path would.
      const fresh = await http.refreshAccessToken();
      if (fresh && isCurrent()) {
        unauthorized = false;
        result = await streamOnce(fresh);
        if (unauthorized) http.reportUnauthorized();
      }
    }
    if (unauthorized && isCurrent()) {
      streamFailed = true;
      failure = 'Session expired, please login again';
    }

    // Superseded turn (aborted, or the user already started another one): its
    // tail must not touch shared state. `isStreaming = false` here would clear
    // the flag of the turn that is currently running.
    if (!isCurrent()) return;

    // On failure the row keeps whatever text was buffered and carries the error
    // in `status` / `error`, which is what the renderers' error block reads.
    // Writing the (empty) stream result over the content would turn the
    // failure into an assistant that simply had nothing to say.
    updateMessage(
      liveAssistantId,
      streamFailed
        ? { isStreaming: false, status: 'error', error: failure }
        : {
            content: result.text || bufferedText,
            reasoning: result.reasoning || bufferedReasoning || null,
            isStreaming: false,
            status: 'done',
          },
    );
    isStreaming.value = false;
    abortController = null;

    // Roll the optimistic row back if the stream died before the backend
    // committed a thread. A turn that ended cleanly but never reported one
    // (persistence skipped, say) has nothing to keep the row for either; the
    // list is re-fetched so the sidebar shows what the server actually has.
    dropPendingRow(!streamFailed);
  }

  // Guarded: the hook is usable outside a component (a store, a test), where an
  // unguarded `onScopeDispose` only emits a Vue warning and registers nothing.
  if (getCurrentScope()) onScopeDispose(() => abort(false));

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
    abort: () => abort(),
    updateMessage,
  };
}
