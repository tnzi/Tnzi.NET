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
 *   - **Runs that outlive the page** (agents bound to an external CLI runtime):
 *     the first event of such a turn names its run. Stop cancels that run on
 *     the server (closing the stream alone leaves it running, and its reply
 *     would land in the thread anyway); opening a thread whose run is still
 *     going reattaches to it, so a refresh mid-answer picks the answer up again.
 *   - **A broken stream is not a failed turn**: when the connection drops (a
 *     phone froze the page in the background, the network went away) the
 *     server may have finished the turn, or still be on it. The hook asks it:
 *     a run's stream is picked up after the last event seen, otherwise the
 *     thread is read back. It asks again when the page is shown, comes back
 *     online or is focused, and Retry asks before it re-runs anything. Only a
 *     server that cannot be reached leaves the turn as an error.
 *   - **Edit and regenerate** through the backend's own rewind
 *     (`editMessageId` / `regenerate`), so the server thread and the agent's
 *     session lose the replaced turns too, not just the screen.
 *   - **History in pages**: the sidebar list pages and searches, and a thread
 *     opens on its newest messages and pages back on demand.
 *   - **Recents**: the sidebar list's filter (archive status, last activity),
 *     order and grouping are kept per browser and sent with every page; rename,
 *     archive and unarchive keep the sidebar and the full Recents page in step
 *     (`recents`, for `TChatApp`'s `recents` prop).
 *
 * Errors are reported through `onError` rather than a toast: this package owns
 * no notification system, and which surface an error belongs on is the
 * product's call. Every report carries the server's error code (and details)
 * where it gave one, and `describeError` rewords any of them in the product's
 * own copy, the ones written on a failed reply included.
 */
import { ref, readonly, shallowRef, getCurrentScope, onScopeDispose, watch, type Ref } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import {
  streamChat,
  ChatStreamRequestError,
  type ChatRequestDto,
  type ChatStreamEvent,
  type useChatApi,
  type useThreadApi,
} from '@tnzi/core/services/ai';
import { createPagedQuery } from '@tnzi/core/types';
import type { ChatMessage, ToolCallInfo } from './useChat';
import { filesToContentParts, type UploadedAttachment } from './attachment-parts';
import { takeWelcomeMessage } from './welcome-message';
import { toChatThreadsError, type ChatThreadsError, type ChatThreadsOperation } from './chat-errors';
import {
  CLI_USAGE_LIMIT_ERROR_CODE,
  USAGE_LIMIT_REACHED_ERROR_CODE,
  readUsageLimitDenial,
  type UsageLimitNotice,
} from './usage-limits';
import type { ThreadItem } from '../components/chat/TThreadList.vue';
import { toChatMessages, toMessageRole, toThreadItem, toThreadItems } from '../adapters/index';
import {
  RECENTS_STORAGE_KEY,
  changesRecentsQuery,
  matchesRecentsStatus,
  normalizeRecentsPreferences,
  readRecentsPreferences,
  recentsQuery,
  writeRecentsPreferences,
  type RecentThreadsController,
  type RecentsPreferences,
  type RecentsStatus,
} from './recents';

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
  /** `useChatApi(http)` - the stream URL, and the run endpoints (find, reattach, cancel). */
  chatApi: ReturnType<typeof useChatApi>;
  /**
   * Which agent the conversation is pinned to, read per turn.
   *
   * Worth setting: with no agent the backend runs a bare provider/model call,
   * which also means an external-CLI binding can never apply - the routing
   * facade keys off the agent id and treats "no agent" as always-built-in.
   *
   * A product that resolves its agent after start-up returns undefined until
   * then, from reactive state (a ref or computed): the welcome message waits for
   * it (see `sendWelcomeMessage`).
   */
  agentId?: () => string | undefined | null;
  /**
   * Limit the sidebar list, the full Recents page and their search to these agents' conversations,
   * read on every query (an id, several, or none/empty for no limit, which is the default). The
   * server applies it before paging, so `hasMoreThreads` and "Show more" stay correct. A product
   * with two agents that keep separate lists (chat and code sessions) passes its own agent here.
   * Read from reactive state, a change reloads the sidebar list and, once opened, the Recents page.
   * This only scopes what is listed; `agentId` still decides who answers a turn.
   */
  listAgentIds?: () => string | readonly string[] | undefined | null;
  /**
   * The model the user picked for the next turn, read per turn (typically
   * `useChatModels().selected`). Omitted or null: the agent's own model. The
   * server checks it against the models this user may use and refuses others
   * (`AI_MODEL_NOT_ALLOWED`).
   */
  model?: () => string | undefined | null;
  /**
   * A turn ran into a usage limit (see `usageLimit`). A product that shows
   * usage figures refreshes them here.
   */
  onUsageLimit?: (notice: UsageLimitNotice) => void;
  /** How many threads one page of the sidebar list holds. Default 50; `loadMoreThreads` adds the next page. */
  pageSize?: number;
  /**
   * localStorage key of the sidebar list's filter, order and grouping (`recents.preferences`).
   * Default `'tnzi-ui-ai-recents'`; null keeps them for this page only.
   */
  recentsStorageKey?: string | null;
  /** Title shown for a thread the backend has not named yet. */
  untitledLabel?: string;
  /** How many messages one page of a thread holds (opening it, and each `loadOlderMessages`). Default 100. */
  messageLimit?: number;
  /**
   * Surface a failure to the user. Without one, failures are silent. `message` is
   * the text to show (after `describeError`); `error` carries the operation, the
   * server's error code and details, and the HTTP status.
   *
   * A turn whose stream fails is not reported here: its reply row shows the
   * failure (`status: 'error'`, `error`, `errorCode`, `errorDetails`).
   */
  onError?: (message: string, error: ChatThreadsError) => void;
  /**
   * Word a failure in the product's own copy, typically by `error.code`. Applies
   * to everything the hook reports: through `onError`, and on a failed reply row.
   * Return null, undefined or an empty string to keep the message as it came.
   */
  describeError?: (error: ChatThreadsError) => string | null | undefined;
  /**
   * Upload a non-image attachment and return its storage id, so the turn can
   * reference it as a `file` content part (images and PDFs travel inline as base64 and
   * need nothing). Typically `useStorageApi(http).upload` unwrapped. Without
   * it any other file is REFUSED through `onError` and the turn is not sent:
   * the endpoint has no inline form for such a file, and sending the text
   * alone would answer "I don't see a document" to a user who attached one.
   * The framework's default backend does not load file contents and answers a
   * `file` part with 501, so pass this only to a backend that resolves them.
   */
  uploadFile?: (file: File) => Promise<UploadedAttachment>;
  /** Per-attachment ceiling in bytes. Default 10 MB. */
  maxAttachmentBytes?: number;
  /**
   * Send the message a signed-out visitor typed on the welcome screen before signing in
   * (`defineChatApp({ guestWelcome })`), once. Default true; it does nothing unless such a
   * message was kept (see `stashWelcomeMessage`).
   *
   * It is sent when the hook can send it the way the visitor meant it: with `agentId` set,
   * once that returns an agent (read reactively, so an agent resolved after start-up is
   * waited for), and never on top of a turn already running. Without `agentId` it goes at
   * start-up. A product whose agent can legitimately stay unset sends it itself:
   * `sendWelcomeMessage: false`, then `takeWelcomeMessage()` and `send()`.
   */
  sendWelcomeMessage?: boolean;
}

/** Per-turn options of `send`, `regenerate` and `editMessage`. */
export interface ChatTurnOptions {
  /**
   * Per-turn metadata (`ChatRequestDto.metadata`): a small set of values the product
   * defines, such as the ids of the devices this message points at. The server bounds it
   * (default 16 keys, 4 KB), hands it to its per-turn handlers and stores it with the user
   * message. A regenerate without it sends the stored metadata again.
   */
  metadata?: Record<string, unknown> | null;
}

export interface UseChatThreadsReturn {
  threads: Ref<ThreadItem[]>;
  activeThreadId: Ref<string | undefined>;
  messages: Ref<ChatMessage[]>;
  isStreaming: Ref<boolean>;
  inputText: Ref<string>;
  /** More threads exist than the list holds; `loadMoreThreads` fetches the next page. */
  hasMoreThreads: Ref<boolean>;
  /** The search the list is filtered by ('' = none). */
  threadKeyword: Ref<string>;
  /** The open thread has messages before the oldest one shown. */
  hasOlderMessages: Ref<boolean>;
  /** A page of older messages is being fetched. */
  loadingOlderMessages: Ref<boolean>;
  /** The run the current turn is in (agents bound to an external CLI runtime); undefined otherwise. */
  activeRunId: Ref<string | undefined>;
  /**
   * The usage limit the last turn ran into, or null. Set when the server
   * refused a turn because the user reached a limit (`AI_USAGE_LIMIT_REACHED`):
   * nothing of that turn was kept (its rows are taken off the screen and the
   * text goes back into an empty composer), so this is what tells the user why
   * and when they can send again. Bind it to `TChatApp`'s
   * `v-model:usage-limit-notice`; the next turn clears it, and so may the user.
   */
  usageLimit: Ref<UsageLimitNotice | null>;
  /**
   * The thread `selectThread` is loading, from the click until its transcript is shown, the
   * load fails, or something supersedes it (another selection, New chat, a send, deleting it).
   * `activeThreadId` only moves when the transcript arrives, so this is what a list highlights
   * and what the main area shows as loading in the meantime (`TChatApp`'s `openingThreadId`).
   */
  openingThreadId: Readonly<Ref<string | undefined>>;
  /** (Re)load the first page of the sidebar list, with the current search. */
  loadThreads: () => Promise<void>;
  /** Append the next page of the sidebar list. No-op when there is none or one is loading. */
  loadMoreThreads: () => Promise<void>;
  /** Filter the sidebar list by title ('' clears the filter) and reload its first page. */
  searchThreads: (keyword: string) => Promise<void>;
  /**
   * Open a thread: load its newest messages, then switch `activeThreadId` and
   * `messages` together. `openingThreadId` names it while it loads. A failed
   * load leaves the open conversation as it was (and reports through
   * `onError`). When the thread has a run still going, the hook reattaches to it.
   *
   * Selecting the conversation already on screen (the active thread, or the
   * optimistic row of a new one) cancels an open still loading, so that click
   * means "stay here" instead of being overtaken when the other transcript
   * lands; with nothing loading it does nothing. Selecting the thread already
   * being opened returns that open's promise rather than loading it again.
   */
  selectThread: (id: string) => Promise<void>;
  /** Prepend the page of messages before the oldest one shown. */
  loadOlderMessages: () => Promise<void>;
  /** Drop back to the empty state without creating anything server-side. */
  newChat: () => void;
  /** Delete a thread: it leaves the sidebar and the Recents page; deleting the open one starts a new chat. */
  deleteThread: (id: string) => Promise<void>;
  /**
   * The sidebar list's filter, order and grouping, and the full Recents page, with rename, archive
   * and unarchive. Hand it to `TChatApp`'s `recents` prop.
   */
  recents: RecentThreadsController;
  /**
   * Send a turn and stream the answer. Resolves when the turn settles.
   * `files` are the composer's attachments; a turn may be files only.
   */
  send: (content: string, files?: readonly File[], options?: ChatTurnOptions) => Promise<void>;
  /**
   * Ask again: the last user message is sent again (with its attachments) for a new reply,
   * and the server drops the old reply. No-op while a turn runs or without an open thread.
   */
  regenerate: (options?: ChatTurnOptions) => Promise<void>;
  /**
   * Replace a user message of the open thread with new text: it and everything after it
   * are dropped (on the server too), and the edited message is sent for a new reply.
   */
  editMessage: (messageId: string, content: string, options?: ChatTurnOptions) => Promise<void>;
  /** Cancel an in-flight turn (and its run on the server, when it has one). */
  abort: () => void;
  /** Patch one rendered message (feedback, edits). */
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
  /**
   * An object URL for a stored attachment of the open thread (an uploaded image or PDF, or
   * an image a tool returned), fetched with the signed-in client. Null when it cannot be read.
   * URLs are released when the thread changes and when the hook's scope ends.
   */
  resolveAttachmentUrl: (attachmentId: string) => Promise<string | null>;
}

/** What one streamed turn writes to: the rows it fills, and how it began. */
interface TurnRows {
  userId?: string;
  assistantId: string;
  /** Text for the optimistic sidebar row of a new conversation. */
  titleHint: string;
  /** The turn rewound the thread (edit / regenerate): on failure the transcript is reloaded. */
  rewinds: boolean;
  /** A new turn's text, put back into an empty composer when the server refuses the turn. */
  draft?: string;
}

/**
 * How far a turn's stream got: what it wrote to the reply, and where on the server it can be
 * picked up again. Carried across a broken stream so the next one continues the same reply.
 */
interface TurnProgress {
  text: string;
  reasoning: string;
  toolCalls: ToolCallInfo[];
  /** The run answering the turn (external CLI agents): its stream can be picked up again. */
  runId?: string;
  /** The thread the server put the turn in, as soon as an event named it. */
  threadId?: string;
  /** The last run event seen; a reattach continues after it. */
  sequence: number;
  /** The turn's rows as they are now (persisted ids once the server reported them). */
  userId?: string;
  assistantId: string;
}

/** A stream picking a broken turn up again: where it got to, which attempt this is, and what broke it. */
interface TurnResume {
  progress: TurnProgress;
  attempt: number;
  failure: ChatThreadsError;
}

/** A turn whose stream broke and whose outcome the server could not be asked about yet. */
interface UnresolvedTurn {
  rows: TurnRows;
  progress: TurnProgress;
  failure: ChatThreadsError;
}

/**
 * How a turn ended: `answered` (the reply is on screen), `failed` (for a reason the server
 * gave), `unanswered` (the server is reachable and has no reply and no run for it), `unresolved`
 * (the server could not be reached; asked again later), `superseded` (the user moved on).
 */
type TurnOutcome = 'answered' | 'failed' | 'unanswered' | 'unresolved' | 'superseded';

/** Attempts at asking the server about a broken turn before it is left as an error. */
const MAX_RECOVERY_ATTEMPTS = 2;
/** Pause before the second attempt: a phone just back from the background may not have its network yet. */
const RECOVERY_RETRY_DELAY_MS = 2_000;
/**
 * A stream over a run that sat in a hidden page this long is picked up again when the page
 * comes back: a frozen page's connection can be dead without the fetch ever saying so.
 */
const RECONNECT_AFTER_HIDDEN_MS = 5_000;

/**
 * The stream broke without the server answering (the fetch rejected, the body stopped): not a
 * refusal, so whether the turn went through is unknown. A refused request carries its HTTP
 * status; a stream error event is not an `Error` at all.
 */
function isConnectionFailure(err: unknown): err is Error {
  return err instanceof Error && typeof (err as { status?: unknown }).status !== 'number';
}

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

export function useChatThreads(options: UseChatThreadsOptions): UseChatThreadsReturn {
  const { http, threadApi, chatApi } = options;
  const pageSize = options.pageSize ?? 50;
  const recentsKey = options.recentsStorageKey === undefined ? RECENTS_STORAGE_KEY : options.recentsStorageKey;
  const messageLimit = options.messageLimit ?? 100;
  const untitled = options.untitledLabel ?? 'New chat';

  const threads = shallowRef<ThreadItem[]>([]);
  const activeThreadId = ref<string | undefined>(undefined);
  const messages = shallowRef<ChatMessage[]>([]);
  const isStreaming = ref(false);
  const inputText = ref('');
  const hasMoreThreads = ref(false);
  const threadKeyword = ref('');
  const hasOlderMessages = ref(false);
  const loadingOlderMessages = ref(false);
  const activeRunId = ref<string | undefined>(undefined);
  const usageLimit = ref<UsageLimitNotice | null>(null);
  const recentsPreferences = ref<RecentsPreferences>(readRecentsPreferences(recentsKey));

  /** Pages of the sidebar list loaded so far. */
  let threadPage = 1;
  /** When the sidebar list's first page was asked for: later pages use the same activity window. */
  let listStartedAt = new Date();
  /** Bumped by every list (re)load, so a slow page cannot land under a newer search. */
  let listSeq = 0;
  let loadingMoreThreads = false;
  /** Order of the oldest message shown; the next older page ends before it. */
  let oldestOrder: number | undefined;

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
   * load, and whichever resolved LAST used to win `messages` - so A's
   * transcript could land under B's active id. New chat and send bump it too:
   * either one means the user moved on before the load finished.
   */
  let selectSeq = 0;
  /** The thread the current `selectThread` is loading, until its answer is handled. */
  const openingThreadId = ref<string | undefined>(undefined);
  /** The promise of that load, so selecting the same thread again waits for it instead of loading twice. */
  let openingLoad: Promise<void> | null = null;
  let idCounter = 0;
  const newId = () => `m_${Date.now()}_${++idCounter}`;
  const LOCAL_MESSAGE_PREFIX = 'm_';
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

  /** The last turn of the open thread, when its stream broke and the server could not be asked yet. */
  const unresolved = shallowRef<UnresolvedTurn | null>(null);
  /** Recoveries waiting for a hidden page to be shown again. */
  let visibleWaiters: Array<() => void> = [];
  /** The turn whose stream was closed on purpose to pick it up again (`turnSeq` of it). */
  let reconnectTurn: number | null = null;

  /** Object URLs of the open thread's attachments, by attachment id. */
  let attachmentUrls = new Map<string, Promise<string | null>>();

  /** The text to show for a failure: the product's own wording when `describeError` gives one. */
  const wordingOf = (error: ChatThreadsError): string => {
    const worded = options.describeError?.(error);
    return worded == null || worded === '' ? error.message : worded;
  };

  /** Report a failure through `onError`. `source` is what failed (an `ApiResult`, an `Error`); `fallback` the message when it carries none. */
  const fail = (operation: ChatThreadsOperation, fallback: string, source?: unknown) => {
    if (!options.onError) return;
    const error = toChatThreadsError(operation, source, fallback);
    options.onError(wordingOf(error), error);
  };

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

  /** Release the open thread's attachment URLs (the thread changed, or the hook is done). */
  function releaseAttachmentUrls(): void {
    const urls = attachmentUrls;
    attachmentUrls = new Map();
    for (const pending of urls.values()) {
      void pending.then((url) => {
        if (url) URL.revokeObjectURL(url);
      });
    }
  }

  function abort(refreshThreads = true): void {
    turnSeq += 1;
    // A recovery in flight or waiting is the turn being stopped too; it sees it is superseded.
    unresolved.value = null;
    reconnectTurn = null;
    releaseVisibleWaiters();
    // The run goes on without a client unless it is cancelled where it runs: its reply
    // would still be written to the thread. What it had written stays as a stopped reply.
    const runId = activeRunId.value;
    activeRunId.value = undefined;
    if (runId) {
      void chatApi.cancelRun(runId).then((result) => {
        if (!result.succeeded) fail('stop-run', 'Could not stop the reply on the server', result);
      });
    }
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

  /** The agents the lists are limited to, deduplicated; empty = no limit. */
  function scopedAgentIds(): string[] {
    const raw = options.listAgentIds?.();
    const ids = typeof raw === 'string' ? [raw] : [...(raw ?? [])];
    return [...new Set(ids.filter((id) => !!id))];
  }

  function withAgentScope<T extends object>(query: T): T | (T & { agentIds: string[] }) {
    const agentIds = scopedAgentIds();
    return agentIds.length > 0 ? { ...query, agentIds } : query;
  }

  function listQuery(page: number) {
    const query = { ...createPagedQuery(page, pageSize), ...recentsQuery(recentsPreferences.value, listStartedAt) };
    const keyword = threadKeyword.value.trim();
    return withAgentScope(keyword ? { ...query, keyword } : query);
  }

  async function loadThreads(): Promise<void> {
    const seq = ++listSeq;
    listStartedAt = new Date();
    const result = await threadApi.getList(listQuery(1));
    if (seq !== listSeq) return;
    if (result.succeeded && result.data) {
      const items = toThreadItems(result.data.items ?? [], untitled);
      // The optimistic row of a turn in flight stays on top: the list does not know it yet.
      const pending = pendingThreadId ? threads.value.filter((t) => t.id === pendingThreadId) : [];
      threads.value = [...pending, ...items];
      threadPage = 1;
      hasMoreThreads.value = (result.data.totalCount ?? 0) > items.length;
    } else {
      fail('load-threads', 'Could not load conversations', result);
    }
  }

  async function loadMoreThreads(): Promise<void> {
    if (!hasMoreThreads.value || loadingMoreThreads) return;
    loadingMoreThreads = true;
    const seq = listSeq;
    try {
      const result = await threadApi.getList(listQuery(threadPage + 1));
      if (seq !== listSeq) return;
      if (!result.succeeded || !result.data) {
        fail('load-more-threads', 'Could not load more conversations', result);
        return;
      }
      const known = new Set(threads.value.map((t) => t.id));
      const next = toThreadItems(result.data.items ?? [], untitled).filter((t) => !known.has(t.id));
      threads.value = [...threads.value, ...next];
      threadPage += 1;
      const total = result.data.totalCount ?? 0;
      hasMoreThreads.value = threadPage * pageSize < total && (result.data.items?.length ?? 0) > 0;
    } finally {
      loadingMoreThreads = false;
    }
  }

  async function searchThreads(keyword: string): Promise<void> {
    threadKeyword.value = keyword;
    await loadThreads();
  }

  /**
   * The user moved on from a thread still loading (New chat, a send, an edit, the
   * conversation on screen picked again, the loading thread deleted): its transcript
   * must not land, and nothing is being opened any more. Also bumped by every new
   * selection, which is what `loadOlderMessages` / `reattach` compare against.
   */
  function supersedeSelection(): void {
    selectSeq += 1;
    openingThreadId.value = undefined;
    openingLoad = null;
  }

  /**
   * The switch happens when the transcript arrives, not when the row is
   * clicked: `activeThreadId` and `messages` change together, so what is on
   * screen is always the conversation a send goes to.
   *
   * Moving the id first would leave the previous thread's transcript under
   * the new one for the length of the load: a message typed in that window is
   * shown under A, persisted into B, then wiped from the screen when B's
   * history lands, and a failed load makes the mismatch permanent (the row
   * cannot be clicked again, it already counts as open). So a failed load
   * leaves everything where it was and the row can be retried, and a send in
   * the window cancels the switch: the user carried on in the conversation in
   * front of them.
   *
   * What the click does at once is `openingThreadId`: the row lights up and the
   * shell shows that this conversation is loading, instead of nothing at all
   * until the transcript arrives (which on a slow connection read as a dead
   * click).
   */
  function selectThread(id: string): Promise<void> {
    // The optimistic row IS the conversation on screen - clicking it is the
    // same as clicking the active one. Aborting the first turn here and then
    // fetching a local id was how `activeThreadId` got poisoned.
    if (id === activeThreadId.value || isPendingId(id)) {
      // Picking the conversation in front of you while another one loads means
      // "stay here": without this the click did nothing and the other
      // transcript replaced this one a moment later.
      if (openingThreadId.value !== undefined) supersedeSelection();
      // Opening it again (a notification's link to it, say) while its last turn is unresolved
      // is the moment to ask the server how that turn ended.
      if (unresolved.value && !isStreaming.value) return resumeUnresolved().then(() => undefined);
      return Promise.resolve();
    }
    // Already on its way: a second click is not a second load.
    if (id === openingThreadId.value && openingLoad) return openingLoad;
    supersedeSelection();
    openingThreadId.value = id;
    const load = openThread(id, selectSeq);
    openingLoad = load;
    return load;
  }

  async function openThread(id: string, seq: number): Promise<void> {
    let result: Awaited<ReturnType<typeof threadApi.getMessages>>;
    try {
      result = await threadApi.getMessages(id, { limit: messageLimit });
    } catch (err) {
      // A load that throws must still end: otherwise the row stays highlighted
      // and the main area says "loading" for good.
      if (seq !== selectSeq) return;
      openingThreadId.value = undefined;
      openingLoad = null;
      fail('load-thread', 'Could not load this conversation', err);
      return;
    }
    // Superseded: another selection, New chat, a send or the deletion of this
    // thread happened while this one was in flight. Its transcript belongs to
    // a thread that is not open.
    if (seq !== selectSeq) return;
    openingThreadId.value = undefined;
    openingLoad = null;
    if (!result.succeeded || !result.data) {
      fail('load-thread', 'Could not load this conversation', result);
      return;
    }
    abort();
    releaseAttachmentUrls();
    activeThreadId.value = id;
    showPage(result.data.messages ?? [], result.data.hasMore ?? false);
    void reattach(id, seq);
  }

  /** Show a thread's newest page. */
  function showPage(page: Parameters<typeof toChatMessages>[0], hasMore: boolean): void {
    messages.value = toChatMessages(page);
    oldestOrder = page[0]?.order;
    hasOlderMessages.value = hasMore && oldestOrder != null;
  }

  async function loadOlderMessages(): Promise<void> {
    const threadId = activeThreadId.value;
    if (!threadId || !hasOlderMessages.value || loadingOlderMessages.value || oldestOrder == null) return;
    const seq = selectSeq;
    loadingOlderMessages.value = true;
    try {
      const result = await threadApi.getMessages(threadId, { beforeOrder: oldestOrder, limit: messageLimit });
      if (seq !== selectSeq || threadId !== activeThreadId.value) return;
      if (!result.succeeded || !result.data) {
        fail('load-older-messages', 'Could not load earlier messages', result);
        return;
      }
      const page = result.data.messages ?? [];
      const known = new Set(messages.value.map((m) => m.id));
      messages.value = [...toChatMessages(page).filter((m) => !known.has(m.id)), ...messages.value];
      if (page[0]) oldestOrder = page[0].order;
      hasOlderMessages.value = (result.data.hasMore ?? false) && page.length > 0;
    } finally {
      loadingOlderMessages.value = false;
    }
  }

  /**
   * The thread has a run still going (an external CLI agent answering, opened after a
   * refresh or from another tab): show its reply as it streams, from the start.
   */
  async function reattach(threadId: string, seq: number): Promise<void> {
    const runs = await chatApi.getActiveRuns(threadId);
    if (seq !== selectSeq || threadId !== activeThreadId.value || isStreaming.value) return;
    const run = runs.succeeded ? runs.data?.[runs.data.length - 1] : undefined;
    if (!run) return;

    const assistantId = newId();
    messages.value = [
      ...messages.value,
      { id: assistantId, role: 'assistant', content: '', reasoning: '', createdAt: new Date().toISOString(), isStreaming: true, status: 'streaming' },
    ];
    activeRunId.value = run.runId;
    await runTurn(
      { url: chatApi.getRunStreamUrl(run.runId, 0), method: 'GET' },
      { userId: run.userMessageId ?? undefined, assistantId, titleHint: '', rewinds: false },
    );
  }

  /** Reload the open thread's newest page (after a rewind that failed: the server's thread is the truth). */
  async function reloadTranscript(): Promise<void> {
    const threadId = activeThreadId.value;
    if (!threadId) return;
    const seq = selectSeq;
    const result = await threadApi.getMessages(threadId, { limit: messageLimit });
    if (seq !== selectSeq || threadId !== activeThreadId.value || isStreaming.value) return;
    if (result.succeeded && result.data) showPage(result.data.messages ?? [], result.data.hasMore ?? false);
  }

  function newChat(): void {
    abort();
    supersedeSelection();
    releaseAttachmentUrls();
    activeThreadId.value = undefined;
    messages.value = [];
    oldestOrder = undefined;
    hasOlderMessages.value = false;
  }

  async function deleteThread(id: string): Promise<void> {
    if (isPendingId(id)) return;
    const result = await threadApi.delete(id);
    if (!result.succeeded) {
      fail('delete-thread', 'Could not delete the conversation', result);
      return;
    }
    threads.value = threads.value.filter((t) => t.id !== id);
    recentPageThreads.value = recentPageThreads.value.filter((t) => t.id !== id);
    if (activeThreadId.value === id) {
      newChat();
    } else if (openingThreadId.value === id) {
      // Clicked but not switched to yet (the switch waits for the transcript):
      // cancel it, or the load would open a conversation that no longer exists.
      supersedeSelection();
    }
  }

  // ---------------------------------------------------------------- recents

  async function setRecentsPreferences(patch: Partial<RecentsPreferences>): Promise<void> {
    const previous = recentsPreferences.value;
    const next = normalizeRecentsPreferences({ ...previous, ...patch });
    if (
      next.status === previous.status
      && next.lastActivity === previous.lastActivity
      && next.sort === previous.sort
      && next.groupBy === previous.groupBy
    ) return;
    recentsPreferences.value = next;
    writeRecentsPreferences(recentsKey, next);
    if (changesRecentsQuery(previous, next)) await loadThreads();
  }

  /* The full Recents page: its own list (Active / Archived, a search, newest
     activity first), loaded when the page opens. */
  const recentPageThreads = shallowRef<ThreadItem[]>([]);
  const recentPageStatus = ref<Exclude<RecentsStatus, 'all'>>('active');
  const recentPageKeyword = ref('');
  const recentPageHasMore = ref(false);
  const recentPageLoading = ref(false);
  const recentPageLoadingMore = ref(false);
  const recentPageLoaded = ref(false);
  let recentPageNumber = 1;
  let recentPageSeq = 0;

  function recentPageQuery(page: number) {
    const query = {
      ...createPagedQuery(page, pageSize),
      ...recentsQuery({ status: recentPageStatus.value, lastActivity: 'all', sort: 'activity' }),
    };
    const keyword = recentPageKeyword.value.trim();
    return withAgentScope(keyword ? { ...query, keyword } : query);
  }

  async function loadRecentPage(): Promise<void> {
    const seq = ++recentPageSeq;
    recentPageLoading.value = true;
    try {
      const result = await threadApi.getList(recentPageQuery(1));
      if (seq !== recentPageSeq) return;
      if (result.succeeded && result.data) {
        const items = toThreadItems(result.data.items ?? [], untitled);
        recentPageThreads.value = items;
        recentPageNumber = 1;
        recentPageHasMore.value = (result.data.totalCount ?? 0) > items.length;
      } else {
        fail('load-threads', 'Could not load conversations', result);
      }
    } finally {
      if (seq === recentPageSeq) {
        recentPageLoading.value = false;
        recentPageLoaded.value = true;
      }
    }
  }

  async function loadMoreRecentPage(): Promise<void> {
    if (!recentPageHasMore.value || recentPageLoadingMore.value || recentPageLoading.value) return;
    recentPageLoadingMore.value = true;
    const seq = recentPageSeq;
    try {
      const result = await threadApi.getList(recentPageQuery(recentPageNumber + 1));
      if (seq !== recentPageSeq) return;
      if (!result.succeeded || !result.data) {
        fail('load-more-threads', 'Could not load more conversations', result);
        return;
      }
      const known = new Set(recentPageThreads.value.map((t) => t.id));
      const next = toThreadItems(result.data.items ?? [], untitled).filter((t) => !known.has(t.id));
      recentPageThreads.value = [...recentPageThreads.value, ...next];
      recentPageNumber += 1;
      const total = result.data.totalCount ?? 0;
      recentPageHasMore.value = recentPageNumber * pageSize < total && (result.data.items?.length ?? 0) > 0;
    } finally {
      recentPageLoadingMore.value = false;
    }
  }

  async function setRecentPageStatus(status: Exclude<RecentsStatus, 'all'>): Promise<void> {
    if (recentPageStatus.value === status && recentPageLoaded.value) return;
    recentPageStatus.value = status;
    await loadRecentPage();
  }

  async function searchRecentPage(keyword: string): Promise<void> {
    recentPageKeyword.value = keyword;
    await loadRecentPage();
  }

  async function renameThread(id: string, title: string): Promise<boolean> {
    const trimmed = title.trim();
    if (!trimmed || isPendingId(id)) return false;
    const result = await threadApi.updateTitle(id, { title: trimmed });
    if (!result.succeeded) {
      fail('rename-thread', 'Could not rename the conversation', result);
      return false;
    }
    const rename = (t: ThreadItem): ThreadItem => (t.id === id ? { ...t, title: trimmed } : t);
    threads.value = threads.value.map(rename);
    recentPageThreads.value = recentPageThreads.value.map(rename);
    return true;
  }

  /**
   * Archive or unarchive, then put each list in step with its own filter: a row
   * that no longer belongs leaves it, one that still does is updated, and a list
   * that should now show a row it does not have is reloaded (it cannot know
   * where the row goes in its order). The conversation on screen stays open.
   */
  async function setThreadArchived(id: string, archived: boolean): Promise<boolean> {
    if (isPendingId(id)) return false;
    const result = archived ? await threadApi.archive(id) : await threadApi.unarchive(id);
    if (!result.succeeded) {
      fail(
        archived ? 'archive-thread' : 'unarchive-thread',
        archived ? 'Could not archive the conversation' : 'Could not unarchive the conversation',
        result,
      );
      return false;
    }
    const update = (list: ThreadItem[], status: RecentsStatus): { list: ThreadItem[]; reload: boolean } => {
      if (!matchesRecentsStatus(status, archived)) return { list: list.filter((t) => t.id !== id), reload: false };
      if (!list.some((t) => t.id === id)) return { list, reload: true };
      return { list: list.map((t) => (t.id === id ? { ...t, archived } : t)), reload: false };
    };
    const sidebar = update(threads.value, recentsPreferences.value.status);
    threads.value = sidebar.list;
    if (sidebar.reload) void loadThreads();
    if (recentPageLoaded.value) {
      const page = update(recentPageThreads.value, recentPageStatus.value);
      recentPageThreads.value = page.list;
      if (page.reload) void loadRecentPage();
    }
    return true;
  }

  // A scope read from reactive state follows it: the lists reload when the agents change.
  if (options.listAgentIds) {
    watch(
      () => scopedAgentIds().join(','),
      () => {
        void loadThreads();
        if (recentPageLoaded.value) void loadRecentPage();
      },
    );
  }

  const recents: RecentThreadsController = {
    preferences: readonly(recentsPreferences),
    setPreferences: setRecentsPreferences,
    rename: renameThread,
    archive: (id) => setThreadArchived(id, true),
    unarchive: (id) => setThreadArchived(id, false),
    page: {
      threads: recentPageThreads,
      status: readonly(recentPageStatus),
      keyword: readonly(recentPageKeyword),
      hasMore: readonly(recentPageHasMore),
      loading: readonly(recentPageLoading),
      loadingMore: readonly(recentPageLoadingMore),
      loaded: readonly(recentPageLoaded),
      load: loadRecentPage,
      loadMore: loadMoreRecentPage,
      setStatus: setRecentPageStatus,
      search: searchRecentPage,
    },
  };

  async function send(content: string, files: readonly File[] = [], turnOptions: ChatTurnOptions = {}): Promise<void> {
    if ((!content.trim() && files.length === 0) || isStreaming.value) return;

    // A thread still loading from a sidebar click is not switched to: the user
    // carried on in the conversation on screen, which is where this turn goes.
    supersedeSelection();
    const turn = ++turnSeq;

    // Attachments are resolved BEFORE anything on screen moves: a refusal (a
    // file the transport cannot carry, an oversized one, a failed upload)
    // leaves no optimistic row, no user bubble and the draft intact. The
    // composer has already cleared its chips, so the refusal is reported.
    // A composer bound with `v-model` (TChatApp's) also clears the text when
    // it emits, so the draft is put back rather than merely left alone.
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
        if (turn === turnSeq) {
          isStreaming.value = false;
          // Only into an empty box: text typed while the upload ran is newer.
          if (!inputText.value) inputText.value = content;
        }
        fail('attachment', 'The attachment could not be sent', err);
        return;
      }
      // Stopped (or superseded) while the upload was in flight.
      if (turn !== turnSeq) return;
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
        ...(turnOptions.metadata ? { metadata: turnOptions.metadata } : {}),
      },
      { id: assistantId, role: 'assistant', content: '', reasoning: '', createdAt: now, isStreaming: true, status: 'streaming' },
    ];
    inputText.value = '';

    const agentId = options.agentId?.() || undefined;
    const model = options.model?.() || undefined;
    // `message` stays alongside `content`: the backend builds the model
    // input from the parts when present, but adds and persists the user
    // turn only from a non-blank `message`. A files-only turn sends the
    // attachment names as its message for that reason.
    await runTurn(
      {
        url: chatApi.getChatStreamUrl(),
        method: 'POST',
        body: {
          message: parts.message,
          threadId: activeThreadId.value ?? null,
          ...(agentId ? { agentId } : {}),
          ...(model ? { model } : {}),
          ...(parts.parts ? { content: parts.parts } : {}),
          ...(turnOptions.metadata ? { metadata: turnOptions.metadata } : {}),
        },
      },
      { userId, assistantId, titleHint: content, rewinds: false, draft: content },
      turn,
    );
  }

  async function regenerate(turnOptions: ChatTurnOptions = {}): Promise<void> {
    if (isStreaming.value) return;
    // Retry on a reply whose stream broke: the server may have answered it, or still be on it.
    // Ask first, and only re-run it once the server says it has no reply and no run for it;
    // otherwise Retry would run a turn the server already has a second time.
    const pending = unresolved.value;
    const lastReply = findLastIndex(messages.value, (m) => m.role === 'assistant');
    if (pending && messages.value[lastReply]?.id === pending.progress.assistantId) {
      if ((await resumeUnresolved()) !== 'unanswered') return;
    }

    const threadId = activeThreadId.value;
    if (isStreaming.value || !threadId) return;
    const lastUser = findLastIndex(messages.value, (m) => m.role === 'user');
    if (lastUser < 0) return;

    supersedeSelection();
    const userRow = messages.value[lastUser];
    if (!userRow) return;
    const assistantId = newId();
    // The question stays on screen (the server stores it again as a new message); the
    // answers after it go, on the server and in the agent's session too.
    messages.value = [
      ...messages.value.slice(0, lastUser + 1),
      { id: assistantId, role: 'assistant', content: '', reasoning: '', createdAt: new Date().toISOString(), isStreaming: true, status: 'streaming' },
    ];

    const agentId = options.agentId?.() || undefined;
    const model = options.model?.() || undefined;
    await runTurn(
      {
        url: chatApi.getChatStreamUrl(),
        method: 'POST',
        body: {
          threadId,
          regenerate: true,
          ...(agentId ? { agentId } : {}),
          ...(model ? { model } : {}),
          ...(turnOptions.metadata ? { metadata: turnOptions.metadata } : {}),
        },
      },
      { userId: userRow.id, assistantId, titleHint: userRow.content, rewinds: true },
    );
  }

  async function editMessage(messageId: string, content: string, turnOptions: ChatTurnOptions = {}): Promise<void> {
    const threadId = activeThreadId.value;
    if (isStreaming.value || !threadId || !content.trim()) return;
    const index = messages.value.findIndex((m) => m.id === messageId && m.role === 'user');
    if (index < 0) return;
    if (messageId.startsWith(LOCAL_MESSAGE_PREFIX)) {
      // Never stored (its turn ended before the server reported the message): nothing to replace.
      fail('edit', 'This message cannot be edited.');
      return;
    }

    supersedeSelection();
    const edited = messages.value[index];
    if (!edited) return;
    const assistantId = newId();
    const userId = newId();
    messages.value = [
      ...messages.value.slice(0, index),
      {
        id: userId,
        role: 'user',
        content,
        createdAt: new Date().toISOString(),
        ...((turnOptions.metadata ?? edited.metadata) ? { metadata: turnOptions.metadata ?? edited.metadata } : {}),
      },
      { id: assistantId, role: 'assistant', content: '', reasoning: '', createdAt: new Date().toISOString(), isStreaming: true, status: 'streaming' },
    ];

    const agentId = options.agentId?.() || undefined;
    const model = options.model?.() || undefined;
    await runTurn(
      {
        url: chatApi.getChatStreamUrl(),
        method: 'POST',
        body: {
          threadId,
          editMessageId: messageId,
          message: content,
          ...(agentId ? { agentId } : {}),
          ...(model ? { model } : {}),
          ...(turnOptions.metadata ? { metadata: turnOptions.metadata } : {}),
        },
      },
      { userId, assistantId, titleHint: content, rewinds: true },
    );
  }

  /**
   * Stream one turn into its rows: a new turn (POST), an edit or regenerate (POST with the
   * rewind fields), or a run picked up again (GET on the run's stream). With `resume` the
   * stream continues a turn whose earlier stream broke, into the same reply.
   *
   * A stream that breaks without the server saying why does not fail the turn: it goes to
   * `recoverTurn`, which asks the server how the turn stands.
   */
  async function runTurn(
    request: { url: string; method: 'POST' | 'GET'; body?: ChatRequestDto },
    rows: TurnRows,
    turn: number = ++turnSeq,
    resume?: TurnResume,
  ): Promise<TurnOutcome> {
    /** Whether this turn is still the one the hook is running. */
    const isCurrent = () => turn === turnSeq;

    isStreaming.value = true;
    // A new turn is a new attempt: the notice of the last refusal goes, and comes back if this one is refused too.
    usageLimit.value = null;
    // A new stream supersedes a turn left unresolved (a resumed one took it over already).
    unresolved.value = null;
    abortController = new AbortController();
    const signal = abortController.signal;

    let bufferedText = resume?.progress.text ?? '';
    let bufferedReasoning = resume?.progress.reasoning ?? '';
    /** The run answering this turn, once an event named it. */
    let runId = resume?.progress.runId;
    /** The thread the server put the turn in, once an event named it. */
    let streamThreadId = resume?.progress.threadId;
    /** The last run event seen, where a reattach continues. */
    let sequence = resume?.progress.sequence ?? 0;
    /** The stream broke without the server saying why (see `isConnectionFailure`). */
    let connectionLost: Error | null = null;
    /** The terminal event arrived: whatever happens to the connection afterwards, the turn has its outcome. */
    let sawDone = false;
    let streamFailed = false;
    /** What went wrong, for the row's error block. Set together with `streamFailed`. */
    let failure: ChatThreadsError | null = null;
    /**
     * Set when the reply stopped at a usage limit: the external CLI's own account
     * (`AI_CLI_USAGE_LIMIT`), or the user's limit reached once the turn was under
     * way (`AI_USAGE_LIMIT_REACHED` in the stream, e.g. a queued run that got to
     * the front after the user's window filled up).
     */
    let limitStop: { code: string; resetsAt: string | null; window: UsageLimitNotice['window'] } | null = null;
    /**
     * Set when the stream was refused with a 401. The raw fetch cannot go
     * through the client's refresh path, so the turn handles it below:
     * refresh once, retry once, and only then treat it as a failure.
     */
    let unauthorized = false;
    // The turn's ids change under us once the backend reports the persisted
    // ones, so finalisation has to write to the LIVE ids, not the initial ones.
    let liveUserId = resume?.progress.userId ?? rows.userId;
    let liveAssistantId = resume?.progress.assistantId ?? rows.assistantId;
    /**
     * Tool activity as the stream reports it: a name-only entry the moment a
     * tool starts (`isToolCall` + `toolCallNames`), filled in with duration and
     * outcome when the detail list arrives after it ran. The same tool called
     * twice is two entries; a detail fills the FIRST still-pending entry of
     * that name. A call with an id (external CLI agents) is settled by id.
     */
    let toolCalls: ToolCallInfo[] = resume?.progress.toolCalls ?? [];
    const isPendingCall = (c: ToolCallInfo) =>
      c.status === 'running' || (c.status == null && c.isSuccess == null && c.durationMs == null);
    /**
     * Set when the event that announces a tool start already carried its
     * detail (external CLI agents send both on one event, with an id). The
     * name-only entry `onToolCall` would add for that same event is then a
     * duplicate. `onEvent` runs before `onToolCall` for one event.
     */
    let startAlreadyDetailed = false;

    const adoptUserMessageId = (id: string | null | undefined) => {
      if (!id || !liveUserId || id === liveUserId) return;
      replaceMessageId(liveUserId, id);
      liveUserId = id;
    };

    const noteFailure = (event: ChatStreamEvent) => {
      if (event.errorCode === CLI_USAGE_LIMIT_ERROR_CODE || event.errorCode === USAGE_LIMIT_REACHED_ERROR_CODE) {
        limitStop = {
          code: event.errorCode,
          resetsAt: event.usageLimitResetsAt ?? limitStop?.resetsAt ?? null,
          window: event.usageLimitWindow ?? limitStop?.window ?? null,
        };
      }
    };

    const streamOnce = (token: string | null) => streamChat({
      url: request.url,
      method: request.method,
      ...(request.body ? { body: request.body } : {}),
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
        if (startAlreadyDetailed) {
          startAlreadyDetailed = false;
          return;
        }
        toolCalls = [...toolCalls, ...names.map((name) => ({ name }))];
        updateMessage(liveAssistantId, { toolCalls });
      },
      onEvent: (event) => {
        startAlreadyDetailed = false;
        if (!isCurrent()) return;
        // Where to pick the turn up again if this stream breaks.
        if (typeof event.sequence === 'number' && event.sequence > sequence) sequence = event.sequence;
        if (event.threadId) streamThreadId = event.threadId;
        if (event.eventType === 'run_started') {
          // The run outlives this request: keep its id so Stop can cancel it on the server.
          if (event.runId) {
            runId = event.runId;
            activeRunId.value = event.runId;
          }
          adoptUserMessageId(event.userMessageId);
          return;
        }
        if (!event.toolCalls?.length) return;
        startAlreadyDetailed = !!event.isToolCall;
        let next = toolCalls;
        for (const detail of event.toolCalls) {
          const byId = detail.id ? next.findIndex((c) => c.id === detail.id) : -1;
          const idx = byId !== -1
            ? byId
            : next.findIndex((c) => c.name === detail.name && !c.id && isPendingCall(c));
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
        sawDone = true;
        if (event.isError) noteFailure(event);
        // The backend may have created the thread for this turn.
        if (event.threadId) adoptThread(event.threadId, rows.titleHint);
        // Swap local ids for persisted ones so feedback / regenerate address
        // real rows.
        adoptUserMessageId(event.userMessageId);
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
        if (isConnectionFailure(err)) {
          // Not a failure yet either: the server may have the turn. Decided by `recoverTurn`.
          connectionLost = err;
          return;
        }
        streamFailed = true;
        if (!(err instanceof Error)) noteFailure(err);
        failure = toChatThreadsError('turn', err, 'Stream failed');
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
      failure = { operation: 'turn', message: 'Session expired, please login again', code: null, details: null, status: 401 };
    }

    // Superseded turn (aborted, or the user already started another one): its
    // tail must not touch shared state. `isStreaming = false` here would clear
    // the flag of the turn that is currently running.
    if (!isCurrent()) return 'superseded';

    const limit = limitStop as { code: string; resetsAt: string | null; window: UsageLimitNotice['window'] } | null;
    const failed = failure as ChatThreadsError | null;
    const lost = connectionLost as Error | null;
    const progress = (): TurnProgress => ({
      text: bufferedText,
      reasoning: bufferedReasoning,
      toolCalls,
      runId,
      threadId: streamThreadId,
      sequence,
      userId: liveUserId,
      assistantId: liveAssistantId,
    });

    // Closed on purpose because the page came back (`onPageReturned`): pick it up again at once.
    const reconnecting = reconnectTurn === turn;
    if (reconnecting) reconnectTurn = null;

    // The stream broke before the turn's outcome arrived: the server may have finished the
    // turn or still be on it, so ask it rather than calling the turn failed. A run's stream
    // that ends without a terminal event broke too (the server always ends one with it).
    if (!sawDone && !streamFailed && (lost || reconnecting || runId)) {
      const cause = toChatThreadsError('turn', lost, 'The connection to the server was lost');
      const attempt = !resume || reconnecting ? 0 : resume.attempt + 1;
      return recoverTurn(rows, progress(), turn, resume && !lost && !reconnecting ? resume.failure : cause, attempt);
    }

    // Picking a broken turn up again was refused (the run is gone, say): the server is
    // reachable, so its thread says how the turn ended. Counted as an attempt, so a run the
    // thread still lists but whose stream keeps being refused cannot go round for ever.
    if (resume && failed && failed.status != null && failed.status !== 401 && !limit) {
      return reconcileWithThread(rows, { ...progress(), runId: undefined }, turn, resume.failure, resume.attempt + 1);
    }

    // The server refused the turn because the user is at a usage limit. It
    // refuses BEFORE writing anything (no thread, no user message, no run), so
    // the turn leaves the screen too and its text goes back into the composer:
    // a notice with the reset time, not a failed reply to retry into the same wall.
    if (failed && failed.status != null && failed.code === USAGE_LIMIT_REACHED_ERROR_CODE) {
      refuseTurn(rows, liveUserId, liveAssistantId, readUsageLimitDenial(failed.details));
      return 'failed';
    }

    // On failure the row keeps whatever text was buffered and carries the error
    // in `status` / `error`, which is what the renderers' error block reads.
    // Writing the (empty) stream result over the content would turn the
    // failure into an assistant that simply had nothing to say. A usage limit is
    // its own outcome: the renderers say whose limit was reached, and when it resets.
    /* The code travels with the row, so a renderer (or `describeError`) can word it. */
    const errorFields = failed
      ? { error: wordingOf(failed), errorCode: failed.code, errorDetails: failed.details }
      : { error: null, errorCode: limit ? limit.code : null, errorDetails: null };
    if (limit?.code === USAGE_LIMIT_REACHED_ERROR_CODE) noteUsageLimit({ window: limit.window, resetsAt: limit.resetsAt });
    updateMessage(
      liveAssistantId,
      limit
        ? {
            isStreaming: false,
            status: 'usage_limit',
            usageLimitResetsAt: limit.resetsAt,
            usageLimitWindow: limit.window,
            ...errorFields,
            errorCode: limit.code,
          }
        : streamFailed
          ? { isStreaming: false, status: 'error', ...errorFields }
          : {
              // The buffer holds the whole reply; a stream that picked the turn up again
              // only returns what it carried itself.
              content: bufferedText || result.text,
              reasoning: bufferedReasoning || result.reasoning || null,
              isStreaming: false,
              status: 'done',
            },
    );
    endTurn();

    // Roll the optimistic row back if the stream died before the backend
    // committed a thread. A turn that ended cleanly but never reported one
    // (persistence skipped, say) has nothing to keep the row for either; the
    // list is re-fetched so the sidebar shows what the server actually has.
    dropPendingRow(!streamFailed);

    // An edit or regenerate that was refused (a reply still running, a message that
    // cannot be edited) left the server's thread as it was: show that, not the local guess.
    if (rows.rewinds && streamFailed && !limit) void reloadTranscript();
    return streamFailed || limit ? 'failed' : 'answered';
  }

  /** The turn is over: clear what it held while in flight. */
  function endTurn(): void {
    isStreaming.value = false;
    abortController = null;
    activeRunId.value = undefined;
  }

  /**
   * The server put a new conversation's turn in a thread: open it, in place of the optimistic
   * sidebar row. No-op when a thread is already open.
   */
  function adoptThread(threadId: string, titleHint: string): void {
    if (activeThreadId.value) return;
    activeThreadId.value = threadId;
    if (pendingThreadId) {
      threads.value = threads.value.map((t) =>
        t.id === pendingThreadId
          ? toThreadItem({ id: threadId, title: previewTitle(titleHint), lastActivityTime: new Date().toISOString() }, untitled)
          : t,
      );
      pendingThreadId = null;
    }
    // Refresh in the background so server-side state (an auto-generated
    // title, for one) replaces the local guess.
    void loadThreads();
  }

  /** Show a reply as failed, worded through `describeError`. */
  function markReplyFailed(assistantId: string, failure: ChatThreadsError): void {
    updateMessage(assistantId, {
      isStreaming: false,
      status: 'error',
      error: wordingOf(failure),
      errorCode: failure.code,
      errorDetails: failure.details,
    });
  }

  /**
   * A turn's stream broke: find out from the server how the turn stands. A run is picked up
   * after the last event seen (it replays what was missed and ends with the run's outcome,
   * whether it is still going or long finished); without one, the thread is read back.
   * Waits for a hidden page to be shown first, and gives a server that cannot be reached a
   * second chance after a pause before leaving the turn unresolved.
   */
  async function recoverTurn(
    rows: TurnRows,
    progress: TurnProgress,
    turn: number,
    failure: ChatThreadsError,
    attempt: number,
  ): Promise<TurnOutcome> {
    const isCurrent = () => turn === turnSeq;
    if (attempt >= MAX_RECOVERY_ATTEMPTS) return leaveUnresolved(rows, progress, failure);

    await whenPageVisible();
    if (!isCurrent()) return 'superseded';
    if (attempt > 0) {
      await delay(RECOVERY_RETRY_DELAY_MS);
      if (!isCurrent()) return 'superseded';
    }

    if (progress.runId) {
      activeRunId.value = progress.runId;
      return runTurn(
        { url: chatApi.getRunStreamUrl(progress.runId, progress.sequence), method: 'GET' },
        rows,
        turn,
        { progress, attempt, failure },
      );
    }
    return reconcileWithThread(rows, progress, turn, failure, attempt);
  }

  /**
   * Read a broken turn's outcome from its thread: a run still answering it is picked up, a
   * stored reply is shown, and a thread with neither means the turn really failed.
   */
  async function reconcileWithThread(
    rows: TurnRows,
    progress: TurnProgress,
    turn: number,
    failure: ChatThreadsError,
    attempt: number,
  ): Promise<TurnOutcome> {
    const isCurrent = () => turn === turnSeq;
    const threadId = progress.threadId ?? activeThreadId.value;
    if (!threadId || isPendingId(threadId)) {
      // No event named a thread: there is nothing to ask about. The list is re-fetched so
      // the sidebar shows whatever the server did keep.
      markReplyFailed(progress.assistantId, failure);
      endTurn();
      dropPendingRow(true);
      return 'failed';
    }

    let runs: Awaited<ReturnType<typeof chatApi.getActiveRuns>>;
    let transcript: Awaited<ReturnType<typeof threadApi.getMessages>>;
    try {
      // Runs first: a run that finishes between the two reads has written its reply by then
      // (the reply is stored before the run's outcome), so the transcript read after it shows it.
      runs = await chatApi.getActiveRuns(threadId);
      if (!isCurrent()) return 'superseded';
      if (!runs.succeeded) return recoverTurn(rows, progress, turn, failure, attempt + 1);
      const active = runs.data ?? [];
      const run = active.find((r) => r.userMessageId && r.userMessageId === progress.userId) ?? active[active.length - 1];
      if (run) {
        // A run whose events this page never saw: follow it from the start, into the same reply.
        return recoverTurn(
          rows,
          { ...progress, threadId, runId: run.runId, sequence: 0, text: '', reasoning: '', toolCalls: [] },
          turn,
          failure,
          attempt,
        );
      }
      transcript = await threadApi.getMessages(threadId, { limit: messageLimit });
      if (!isCurrent()) return 'superseded';
      if (!transcript.succeeded || !transcript.data) return recoverTurn(rows, progress, turn, failure, attempt + 1);
    } catch {
      if (!isCurrent()) return 'superseded';
      return recoverTurn(rows, progress, turn, failure, attempt + 1);
    }

    const page = transcript.data.messages ?? [];
    const at = locateTurn(page, progress.userId, rows.titleHint);
    if (at >= 0 && page.slice(at + 1).some((m) => toMessageRole(m.role) === 'assistant')) {
      // The server answered it: show the thread as it has it.
      endTurn();
      adoptThread(threadId, rows.titleHint);
      showPage(page, transcript.data.hasMore ?? false);
      return 'answered';
    }

    // Reachable, and neither a reply nor a run: the turn did fail. The question the server
    // kept (if it kept it) is the row Retry asks again.
    const stored = at >= 0 ? page[at]?.id : undefined;
    if (stored && progress.userId) replaceMessageId(progress.userId, stored);
    markReplyFailed(progress.assistantId, failure);
    endTurn();
    adoptThread(threadId, rows.titleHint);
    return 'unanswered';
  }

  /**
   * Where the turn's question sits in the thread's newest page, or -1. By its stored id when
   * the server reported one; otherwise it is the thread's last question, provided it comes
   * after the message shown just before it (or opens the conversation) and reads the same.
   */
  function locateTurn(page: Parameters<typeof toChatMessages>[0], userId: string | undefined, content: string): number {
    if (userId && !userId.startsWith(LOCAL_MESSAGE_PREFIX)) return page.findIndex((m) => m.id === userId);
    const lastQuestion = findLastIndex(page, (m) => toMessageRole(m.role) === 'user');
    if (lastQuestion < 0) return -1;
    const index = userId ? messages.value.findIndex((m) => m.id === userId) : -1;
    if (index < 0) return -1;
    const before = messages.value[index - 1];
    if (!before) return lastQuestion;
    if (before.id.startsWith(LOCAL_MESSAGE_PREFIX)) return page[lastQuestion]?.content === content ? lastQuestion : -1;
    const anchor = page.findIndex((m) => m.id === before.id);
    return anchor >= 0 && lastQuestion > anchor ? lastQuestion : -1;
  }

  /**
   * The server could not be reached about a broken turn: show the reply as failed, and keep
   * what is needed to ask again when the page comes back, it goes online, or Retry is pressed.
   */
  function leaveUnresolved(rows: TurnRows, progress: TurnProgress, failure: ChatThreadsError): TurnOutcome {
    markReplyFailed(progress.assistantId, failure);
    endTurn();
    if (progress.threadId) adoptThread(progress.threadId, rows.titleHint);
    else dropPendingRow(true);
    unresolved.value = { rows, progress, failure };
    return 'unresolved';
  }

  /** Ask the server again about the unresolved turn, showing it in progress meanwhile. */
  function resumeUnresolved(): Promise<TurnOutcome> {
    const pending = unresolved.value;
    if (!pending || isStreaming.value) return Promise.resolve('superseded');
    unresolved.value = null;
    if (!messages.value.some((m) => m.id === pending.progress.assistantId)) return Promise.resolve('superseded');
    const turn = ++turnSeq;
    isStreaming.value = true;
    activeRunId.value = pending.progress.runId;
    updateMessage(pending.progress.assistantId, {
      isStreaming: true,
      status: 'streaming',
      error: null,
      errorCode: null,
      errorDetails: null,
    });
    return recoverTurn(pending.rows, pending.progress, turn, pending.failure, 0);
  }

  /** Resolves at once on a visible page (or outside a browser), otherwise when the page is shown again. */
  function whenPageVisible(): Promise<void> {
    if (typeof document === 'undefined' || document.visibilityState !== 'hidden') return Promise.resolve();
    return new Promise((resolve) => {
      visibleWaiters = [...visibleWaiters, resolve];
    });
  }

  /** Let waiting recoveries go on (the page is back) or see that they were superseded (the turn ended). */
  function releaseVisibleWaiters(): void {
    const waiters = visibleWaiters;
    visibleWaiters = [];
    for (const resolve of waiters) resolve();
  }

  /**
   * The page is back (shown again, online, focused): an unresolved turn is asked about again,
   * and a run's stream that sat in a page hidden long enough to have been frozen is picked up
   * again after the last event seen (a stream that is in fact alive loses nothing by it).
   */
  function onPageReturned(hiddenForMs: number): void {
    releaseVisibleWaiters();
    if (unresolved.value && !isStreaming.value) {
      void resumeUnresolved();
      return;
    }
    if (isStreaming.value && activeRunId.value && abortController && hiddenForMs >= RECONNECT_AFTER_HIDDEN_MS) {
      reconnectTurn = turnSeq;
      abortController.abort();
    }
  }

  /** Listen for the page coming back; returns the function that stops listening. */
  function listenForPageReturn(): () => void {
    if (typeof document === 'undefined' || typeof window === 'undefined') return () => {};
    let hiddenAt: number | null = document.visibilityState === 'hidden' ? Date.now() : null;
    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') {
        hiddenAt ??= Date.now();
        return;
      }
      const hiddenFor = hiddenAt == null ? 0 : Date.now() - hiddenAt;
      hiddenAt = null;
      onPageReturned(hiddenFor);
    };
    // Restored from the back-forward cache: every connection it had is gone.
    const onPageShow = (event: PageTransitionEvent) => onPageReturned(event.persisted ? Number.POSITIVE_INFINITY : 0);
    const onBack = () => onPageReturned(0);
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('pageshow', onPageShow);
    window.addEventListener('online', onBack);
    window.addEventListener('focus', onBack);
    return () => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('pageshow', onPageShow);
      window.removeEventListener('online', onBack);
      window.removeEventListener('focus', onBack);
    };
  }

  // The page is only listened to while it matters: a turn in flight or unresolved. Idle hooks
  // (and hooks used outside a component, which no scope will clean up) hold no listeners.
  watch(
    () => isStreaming.value || unresolved.value !== null,
    (needed, _previous, onCleanup) => {
      if (needed) onCleanup(listenForPageReturn());
    },
    { immediate: true, flush: 'sync' },
  );

  /** Record a usage limit the user ran into and tell the product. */
  function noteUsageLimit(notice: UsageLimitNotice): void {
    usageLimit.value = notice;
    options.onUsageLimit?.(notice);
  }

  /**
   * Take a refused turn back off the screen. A new turn's rows go (and the
   * optimistic sidebar row of a new conversation), and its text returns to an
   * empty composer; an edit or regenerate reloads the transcript, which the
   * server never rewound.
   */
  function refuseTurn(rows: TurnRows, userId: string | undefined, assistantId: string, notice: UsageLimitNotice): void {
    const gone = new Set([assistantId]);
    if (!rows.rewinds && userId) gone.add(userId);
    messages.value = messages.value.filter((m) => !gone.has(m.id));
    isStreaming.value = false;
    abortController = null;
    activeRunId.value = undefined;
    dropPendingRow(false);
    // Only into an empty box: text typed while the request was in flight is newer.
    if (rows.draft != null && !inputText.value) inputText.value = rows.draft;
    noteUsageLimit(notice);
    if (rows.rewinds) void reloadTranscript();
  }

  async function resolveAttachmentUrl(attachmentId: string): Promise<string | null> {
    const threadId = activeThreadId.value;
    if (!threadId || !attachmentId) return null;
    let pending = attachmentUrls.get(attachmentId);
    if (!pending) {
      const map = attachmentUrls;
      pending = threadApi.getAttachment(threadId, attachmentId).then((result) => {
        if (!result.succeeded || !result.data) return null;
        const url = URL.createObjectURL(result.data);
        // The thread changed while this was in flight: nobody will release it otherwise.
        if (map !== attachmentUrls) {
          URL.revokeObjectURL(url);
          return null;
        }
        return url;
      }, () => null);
      attachmentUrls.set(attachmentId, pending);
    }
    return pending;
  }

  // Guarded: the hook is usable outside a component (a store, a test), where an
  // unguarded `onScopeDispose` only emits a Vue warning and registers nothing.
  if (getCurrentScope()) {
    onScopeDispose(() => {
      // Leaving the page is not stopping the reply: the run carries on and its reply is
      // written to the thread. Only the stream is closed here, never the run cancelled.
      activeRunId.value = undefined;
      abort(false);
      releaseAttachmentUrls();
    });
  }

  // The visitor's first message, typed before signing in (`defineChatApp({ guestWelcome })`).
  if (options.sendWelcomeMessage !== false) {
    const welcome = takeWelcomeMessage();
    if (welcome) sendWelcomeWhenReady(welcome);
  }

  /**
   * Send the welcome message once it can go the way the visitor meant it: to the
   * pinned agent (an agent resolved after start-up is waited for), and not on top of
   * a turn already running (`send` would drop it). It was taken from storage up front,
   * so it is sent at most once whatever happens here.
   */
  function sendWelcomeWhenReady(welcome: string): void {
    const readAgent = options.agentId;
    if (!readAgent) {
      queueMicrotask(() => void send(welcome));
      return;
    }
    let sent = false;
    const stop = watch(
      () => (readAgent() || undefined) !== undefined && !isStreaming.value,
      (ready) => {
        if (!ready || sent) return;
        sent = true;
        // A later tick: with `immediate` this first runs inside the caller's own setup.
        queueMicrotask(() => {
          stop();
          void send(welcome);
        });
      },
      { immediate: true },
    );
  }

  return {
    threads,
    activeThreadId,
    messages,
    isStreaming,
    inputText,
    usageLimit,
    hasMoreThreads,
    threadKeyword,
    hasOlderMessages,
    loadingOlderMessages,
    activeRunId,
    openingThreadId: readonly(openingThreadId),
    loadThreads,
    loadMoreThreads,
    searchThreads,
    selectThread,
    loadOlderMessages,
    newChat,
    deleteThread,
    recents,
    send,
    regenerate,
    editMessage,
    abort: () => abort(),
    updateMessage,
    resolveAttachmentUrl,
  };
}

function findLastIndex<T>(items: readonly T[], predicate: (item: T) => boolean): number {
  for (let i = items.length - 1; i >= 0; i -= 1) {
    if (predicate(items[i] as T)) return i;
  }
  return -1;
}
