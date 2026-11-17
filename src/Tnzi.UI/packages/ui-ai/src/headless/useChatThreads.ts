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
 *   - **Moving away is not stopping**: opening another conversation, New chat
 *     or leaving the page closes the stream on screen and never cancels
 *     anything. A run goes on and is picked up again on coming back; a turn
 *     whose run is not named yet keeps its request open until the run is named
 *     (then closes it) or, on a built-in agent, which answers on the request
 *     itself, until the server has finished the turn. Only Stop (`abort()`)
 *     cancels, and a Stop pressed before the run was named cancels the run as
 *     soon as the stream names it.
 *   - **A run waiting on background tasks** (the CLI answered, and answers again
 *     when its background tasks finish, e.g. a dev server it started) is not a
 *     turn in progress: its answer is done, `isStreaming` is false and the
 *     composer works. A quiet row with status `'waiting'` (not streaming) names
 *     the tasks the server reported, the same after a reload. Its stream stays
 *     open: when the CLI answers by itself that answer streams in as a reply of
 *     its own (the turn is busy again while it does). A message sent meanwhile
 *     lets that stream go without cancelling anything; the server hands the
 *     still-running process, tasks included, to the new turn.
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
 *   - **Awaiting, running and unread**: the list rows carry `awaitingOwner` (a turn waits for the
 *     user: a permission request, a question, a confirmation card), `running` (a turn of the
 *     conversation is going on on the server) and `unread` (a reply landed that the user has not
 *     seen), from the backend's attention summary (`attention`, polled; see `useThreadAttention`),
 *     the turn on screen and the approvals it shows. The conversation on screen is marked read when
 *     it opens, when its turn ends with the page in view, and when the page comes back to it.
 *   - **Order**: the rows are kept in the server's order (`compareThreads`: awaiting the user, then
 *     running, then the rest, each by when the user last wrote), applied here too, so a row moves
 *     the moment the user sends, edits, regenerates or queues a message, or the conversation enters
 *     or leaves the first two tiers; a reply, a turn ending or opening a conversation does not move it.
 *   - **Recents**: the sidebar list's filter (archive status, the user's last message),
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
  type ChatTurnCompletedEventData,
  type ChatUserMessageEventData,
  type ChatCompactedEventData,
  type ChatPermissionRequestEventData,
  type ChatPermissionResolvedEventData,
  type useChatApi,
  type useThreadApi,
} from '@tnzi/core/services/ai';
import { createPagedQuery } from '@tnzi/core/types';
import type { ChatCompaction, ChatMessage, ToolCallInfo } from './useChat';
import { filesToContentParts, type UploadedAttachment } from './attachment-parts';
import { takeWelcomeMessage } from './welcome-message';
import { createApprovalQueue, type ApprovalQueue, type ApprovalSource } from './approval-queue';
import { provideChatThreads } from './chat-approvals-key';
import { createMessageQueue, type MessageQueue, type MessageQueueOperation } from './message-queue';
import { useThreadAttention, type ThreadAttention } from './useThreadAttention';
import { createRunRequestSource, runRequestFromEvent, runRequestKey } from './run-requests';
import { toChatThreadsError, type ChatThreadsError, type ChatThreadsOperation } from './chat-errors';
import {
  CLI_USAGE_LIMIT_ERROR_CODE,
  USAGE_LIMIT_REACHED_ERROR_CODE,
  readUsageLimitDenial,
  type UsageLimitNotice,
} from './usage-limits';
import type { ThreadItem } from '../components/chat/TThreadList.vue';
import { toChatMessages, toMessageRole, toThreadItem, toThreadItems } from '../adapters/index';
import { findToolCallOwner, mergeTaskCalls, mergeToolCall, upsertToolCalls } from './cli-tasks';
import {
  RECENTS_STORAGE_KEY,
  changesRecentsQuery,
  sortThreads,
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
   * The reasoning effort the user picked for the next turn, read per turn (typically
   * `useChatModels().effort`). Omitted or null: the turn carries none and the agent's own setting
   * applies. The server checks it against the turn's model and refuses others
   * (`AI_EFFORT_NOT_ALLOWED`). Queued messages carry it too.
   */
  effort?: () => string | undefined | null;
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
   * it (or `inlineFiles`) any other file is REFUSED through `onError` and the turn is not sent:
   * the endpoint has no inline form for such a file, and sending the text
   * alone would answer "I don't see a document" to a user who attached one.
   * The framework's default backend does not load file contents and answers a
   * `file` part with 501, so pass this only to a backend that resolves them.
   */
  uploadFile?: (file: File) => Promise<UploadedAttachment>;
  /**
   * Send files that are not images or PDFs inline (`document` parts with the
   * file's name and type) instead of refusing them or uploading them. For an
   * agent that runs as an external CLI on a backend whose turns accept files
   * (`ICliFileAttachmentPolicy`): the agent opens them with its own tools. A
   * turn that does not take files is refused by the backend (415) through
   * `onError`. Takes precedence over `uploadFile`. Default false.
   */
  inlineFiles?: boolean;
  /** Per-attachment ceiling in bytes for images and PDFs. Default 10 MB. */
  maxAttachmentBytes?: number;
  /** Per-file ceiling in bytes for files sent with `inlineFiles`. Default `maxAttachmentBytes`. */
  maxFileAttachmentBytes?: number;
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
  /**
   * The application's own things a conversation waits on its user for (a high-risk call to
   * confirm, say), beside the external agent's permission requests, questions and plans, which are
   * always followed: each source is asked when a conversation opens, running or not (a card can
   * outlive the turn that left it), every `approvalPollMs` while a turn runs and once more when it
   * ends, and answers its own items. See `approvals`.
   */
  approvalSources?: readonly ApprovalSource[];
  /** How often the approval sources are asked while a turn runs. Default `APPROVAL_POLL_MS` (2 s); 0 asks only on opening and when a turn ends. */
  approvalPollMs?: number;
  /**
   * How often the conversation's queued messages are read while one of them waits to go. Default
   * `QUEUE_POLL_MS` (2 s); 0 reads only when a conversation opens and when its turn ends. See `messageQueue`.
   */
  messageQueuePollMs?: number;
  /**
   * The running / unread summary (`attention`): how often it is read while a conversation runs and while
   * none does (defaults `ATTENTION_RUNNING_POLL_MS` and `ATTENTION_IDLE_POLL_MS`; 0 = only on the hook's
   * own events and when the page comes back). `false` turns it off: the rows keep only what the list
   * read said, and nothing is marked read.
   */
  attention?: false | { runningPollMs?: number; idlePollMs?: number };
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
  /**
   * Where the conversation came from (`ChatRequestDto.origin`), for a `send` that starts a new
   * one: stored on the thread, and shown at once on its optimistic sidebar row. Only values the
   * application allows clients to declare are accepted by the server (otherwise the turn fails
   * with `AI_THREAD_ORIGIN_NOT_ALLOWED`). Ignored when a conversation is open, and by
   * `regenerate` / `editMessage`.
   */
  origin?: string | null;
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
   * The turn in progress in the conversation being left is not stopped: its
   * stream on screen closes and its run goes on (coming back reattaches to it).
   *
   * Selecting the conversation already on screen (the active thread, or the
   * optimistic row of a new one) cancels an open still loading, so that click
   * means "stay here" instead of being overtaken when the other transcript
   * lands; with nothing loading it does nothing. Selecting the thread already
   * being opened returns that open's promise rather than loading it again.
   */
  selectThread: (id: string) => Promise<void>;
  /**
   * Read the open thread again and follow a run that is going on in it, for a turn started
   * somewhere else than this hook (the app's server sending a queued message once the last turn
   * ended, another tab or device). The newest page replaces the messages when it arrives, with no
   * empty state in between, and the latest active run is followed from its start as when the
   * thread is opened. A turn that only waits on background tasks is let go first (nothing is
   * cancelled; the reattach picks its run up again while it still goes). Does nothing without a
   * saved open thread. Asked while a thread is being opened or while a turn streams here, it is
   * kept and done once that is over (if the same thread is still open), so a turn the server
   * started meanwhile is not missed. Resolves once the transcript is shown, as `selectThread` does
   * (at once when it was kept for later). A failed read leaves the screen as it was and reports
   * through `onError`.
   */
  refreshThread: () => Promise<void>;
  /** Prepend the page of messages before the oldest one shown. */
  loadOlderMessages: () => Promise<void>;
  /**
   * Drop back to the empty state without creating anything server-side. A turn
   * in progress is left running, as with `selectThread`; it is not stopped.
   */
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
   *
   * While a turn of an external agent runs (`messageQueue.available`) the message is queued on the
   * server instead, with its attachments, the model and the turn's metadata: an agent whose turn
   * can take messages while it works gets it at once and takes it in between two steps; otherwise it
   * goes when the turn ends. Attachments follow the rules of a normal send (`inlineFiles`, the size
   * limits; an upload by reference cannot wait in the queue). A refused message is reported through
   * `onError` (operation `queue`, or `attachment`) and its text goes back into an empty composer.
   * While a built-in agent's turn runs, `send` does nothing, as before.
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
  /**
   * Stop the turn in progress: what the Stop button does. Its run is cancelled on
   * the server, including a run not named yet when this is called (it is
   * cancelled as soon as the stream names it). Moving away does not call this;
   * call it only for a stop the person asked for.
   */
  abort: () => void;
  /**
   * Stop one task of the open conversation's external agent (a sub-agent or a background command; the `taskId` of
   * its `ToolCallInfo.task`) without stopping the turn. Resolves true once the server took the request; the task's
   * stopped state arrives on the stream. A refusal (no run going on, a CLI that cannot stop a single task) goes to
   * `onError` with operation `stop-task` and resolves false.
   */
  stopTask: (taskId: string) => Promise<boolean>;
  /** Patch one rendered message (feedback, edits). */
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
  /**
   * An object URL for a stored attachment of the open thread (an uploaded image or PDF, or
   * an image a tool returned), fetched with the signed-in client. Null when it cannot be read.
   * URLs are released when the thread changes and when the hook's scope ends.
   */
  resolveAttachmentUrl: (attachmentId: string) => Promise<string | null>;
  /**
   * What the open conversation waits on its user for, as cards: the external agent's requests
   * (from the stream as they appear and resolve, and from `chatApi.getPendingPermissions` for the
   * ones a page opened later must find) and the items of `approvalSources`. `TChatApp` shows them
   * above the composer by itself when the hook runs in the component that renders it (it is
   * provided to descendants); elsewhere pass it as `TChatApp`'s `approvals`.
   */
  approvals: ApprovalQueue;
  /**
   * The open conversation's queued messages (on the server, `chat/threads/{threadId}/queue`). While a
   * turn of an external agent runs (`messageQueue.available`), `send` queues the message instead of
   * dropping it: it waits on the server, which hands it to the running agent when its turn can take
   * messages, or sends it with the others as one turn when the turn ends, also with the page closed. When a queued message goes out as a turn this page did not start, the hook
   * follows it (`refreshThread`). `TChatApp` lists the queue above the composer and queues behind a
   * running turn by itself when the hook runs in the component that renders it (it registers with
   * the approval queue and is picked by the same `messages`); elsewhere pass it as `TChatApp`'s
   * `messageQueue`.
   */
  messageQueue: MessageQueue;
  /**
   * Which of the listed agents' conversations need the user (`listAgentIds` applies): `runningCount`
   * and `unreadCount` over all of them, for a badge outside the list (a mode switch, a nav item), and
   * `markRead`. The list rows already carry the same flags. Idle when the `attention` option is false.
   */
  attention: ThreadAttention;
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

/**
 * The request a turn streams on, as stopping and leaving see it from outside the turn.
 */
interface LiveStream {
  turn: number;
  controller: AbortController;
  /** The run answering the turn, once an event named it. */
  runId?: string;
  /** The thread the server put the turn in, once an event named it. */
  threadId?: string;
  /** The request is still open (`streamChat` has not returned). */
  open: boolean;
  /**
   * The turn was let go while its request was open and before any run was named:
   * `'stop'` (Stop) or `'leave'` (the person moved to another conversation). The
   * request then stays open until its next sign says what it is (`settleReleased`).
   */
  released: 'stop' | 'leave' | null;
}

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

/** Waits between reads of a conversation whose AI title is still coming (about 30 s in all). */
export const TITLE_FOLLOW_DELAYS_MS: readonly number[] = [1500, 3000, 5000, 8000, 12000];

export function useChatThreads(options: UseChatThreadsOptions): UseChatThreadsReturn {
  return createChatThreads(options, 'chat');
}

/**
 * What `createChatThreads` gives a reader (`useThreadReader`) on top of the chat surface:
 * letting the turn on screen go without cancelling it, for "stop following".
 */
export interface ChatThreadsInternals extends UseChatThreadsReturn {
  /** Close the stream on screen; nothing is cancelled and the rows are left as they are. */
  leave: () => void;
}

/**
 * The conversation loop behind `useChatThreads` and `useThreadReader` (internal: not exported
 * from the package). `'chat'` is the whole loop. `'reader'` keeps what reading one conversation
 * and following its turns needs (open by id, older pages, attach to a running turn, recovery of a
 * broken stream) and leaves out everything else: the thread list and titles, attention and read
 * marks, the approval and queue follow-ups, the welcome message and the context `TChatApp` reads.
 * A reader never calls `send` / `regenerate` / `editMessage` / `abort` (`useThreadReader` does not
 * expose them), so the turn paths that assume a list never run in it. `internal.attach`, when
 * given, is asked before attaching to a thread's running turn (opening it, `refreshThread`).
 */
export function createChatThreads(
  options: UseChatThreadsOptions,
  mode: 'chat' | 'reader',
  internal: { attach?: () => boolean } = {},
): ChatThreadsInternals {
  const reader = mode === 'reader';
  const { http, threadApi, chatApi } = options;
  const pageSize = options.pageSize ?? 50;
  const recentsKey = options.recentsStorageKey === undefined ? RECENTS_STORAGE_KEY : options.recentsStorageKey;
  const messageLimit = options.messageLimit ?? 100;
  const untitled = options.untitledLabel ?? 'New chat';

  const threads = shallowRef<ThreadItem[]>([]);
  const attentionEnabled = !reader && options.attention !== false;
  const attention = useThreadAttention({
    threadApi,
    agentIds: options.listAgentIds,
    immediate: attentionEnabled,
    ...(options.attention ? options.attention : {}),
  });
  /** Conversations whose AI title is being waited for, with the pending re-read (see `followTitle`). */
  const titleFollows = new Map<string, ReturnType<typeof setTimeout>>();
  let disposed = false;
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
  const approvals = createApprovalQueue({
    sources: [createRunRequestSource(chatApi), ...(options.approvalSources ?? [])],
    intervalMs: options.approvalPollMs,
  });
  const messageQueue = createMessageQueue({
    api: chatApi,
    intervalMs: options.messageQueuePollMs,
    // A queued message went out as a turn the server started: follow the conversation to stream it.
    onDelivered: () => void refreshThread(),
    onError: (operation, source) => fail(operation, QUEUE_FAILURES[operation], source),
  });
  // A reader sends nothing: it must not be the conversation `TChatApp` (or a composer) finds.
  if (!reader) provideChatThreads({ messages, approvals, messageQueue, stopTask });

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
  /** The request of the turn in progress (null between turns). */
  let liveStream: LiveStream | null = null;
  /** Requests let go before their run was named, still open (see `settleReleased`); closed with the hook's scope. */
  const releasedStreams = new Set<LiveStream>();
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
  /**
   * The turn whose run only waits on background tasks (`turnSeq` of it): its answer is done and the
   * conversation is free, but its stream stays open for the answer the CLI gives by itself later.
   */
  let parkedTurn: number | null = null;

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

  /**
   * What stopping and leaving both do first: the turn stops being the hook's (its tail sees it
   * is superseded), any recovery of it ends, and its request is handed back to the caller.
   */
  function releaseTurn(): LiveStream | null {
    turnSeq += 1;
    parkedTurn = null;
    // A recovery in flight or waiting is the turn being let go too; it sees it is superseded.
    unresolved.value = null;
    reconnectTurn = null;
    releaseVisibleWaiters();
    const live = liveStream;
    liveStream = null;
    abortController = null;
    activeRunId.value = undefined;
    return live;
  }

  async function stopTask(taskId: string): Promise<boolean> {
    const threadId = activeThreadId.value;
    if (!threadId || isPendingId(threadId) || !taskId) return false;
    let result: Awaited<ReturnType<typeof chatApi.stopTask>>;
    try {
      result = await chatApi.stopTask(threadId, taskId);
    } catch (err) {
      fail('stop-task', 'Could not stop the task', err);
      return false;
    }
    if (!result.succeeded) {
      fail('stop-task', 'Could not stop the task', result);
      return false;
    }
    return true;
  }

  function cancelRunOnServer(runId: string): void {
    void chatApi.cancelRun(runId).then((result) => {
      if (!result.succeeded) fail('stop-run', 'Could not stop the reply on the server', result);
    });
  }

  /**
   * A new turn while the last one only waits on background tasks: that run's stream is let go
   * (nothing is cancelled: the run, its process and the tasks go on, and the server hands the
   * process to the new turn) and its quiet waiting row goes; an answer it gives later is in the
   * thread like any other. Without a waiting turn this does nothing.
   */
  function releaseParkedTurn(): void {
    if (parkedTurn === null || parkedTurn !== turnSeq) return;
    const live = releaseTurn();
    live?.controller.abort();
    messages.value = messages.value.filter((m) => m.status !== 'waiting');
  }

  /**
   * Stop: the person ended the turn. The run goes on without a client unless it is cancelled
   * where it runs (its reply would still be written to the thread), so a named run is cancelled.
   * Before a run is named the request stays open for its first sign, because closing it then
   * could lose the stop: an external CLI agent's run may already be queued with its id on the
   * way. What the reply had written stays as a stopped reply.
   */
  function stopTurn(): void {
    const runId = activeRunId.value ?? liveStream?.runId;
    const hadStream = abortController !== null;
    // Stopping a run that only waits on background tasks ends the tasks too: nothing is left to wait for
    const wasParked = parkedTurn !== null && parkedTurn === turnSeq;
    const live = releaseTurn();
    if (wasParked) messages.value = messages.value.filter((m) => m.status !== 'waiting');
    if (runId) {
      cancelRunOnServer(runId);
      live?.controller.abort();
    } else if (live?.open) {
      live.released = 'stop';
      releasedStreams.add(live);
    } else {
      live?.controller.abort();
    }
    // The turn's tail returns early once superseded, so its own rollback never runs; the
    // optimistic row would otherwise stay as a zombie that the next send does not replace.
    if (hadStream) dropPendingRow(true);
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

  /**
   * Leave: the person moved to another conversation (or a new one). Nothing is cancelled: a
   * named run goes on and its stream is closed (coming back reattaches to it); a request with no
   * run named yet stays open until it says what it is (`settleReleased`). The rows are not
   * marked, the caller replaces them. A new conversation's optimistic row becomes the thread the
   * server named, so it can be opened again; without one it goes and the list is re-fetched.
   */
  function leaveTurn(): void {
    const live = releaseTurn();
    if (live?.open && !live.runId) {
      live.released = 'leave';
      releasedStreams.add(live);
    } else {
      live?.controller.abort();
    }
    if (pendingThreadId) {
      const threadId = live?.threadId;
      if (threadId) {
        const pendingId = pendingThreadId;
        threads.value = threads.value.map((t) =>
          t.id === pendingId
            ? toThreadItem({
                id: threadId,
                title: t.title,
                lastActivityTime: t.updatedAt ?? new Date().toISOString(),
                lastUserMessageTime: t.userMessageAt,
                creationTime: t.createdAt,
                origin: t.origin,
              }, untitled)
            : t,
        );
        pendingThreadId = null;
        void loadThreads();
      } else {
        dropPendingRow(true);
      }
    }
    isStreaming.value = false;
  }

  /**
   * A request let go before its run was named shows a sign. A run named (an external CLI
   * agent): it goes on without this request, so the request is closed, and after a Stop the run
   * is cancelled. Any other sign means the agent answers on the request itself (a built-in
   * agent): after a Stop it is closed, which is what ends the turn on the server; after leaving
   * it stays open so the server finishes the turn, and nothing of it reaches the screen.
   */
  function settleReleased(live: LiveStream, runId: string | undefined): void {
    if (runId) {
      if (live.released === 'stop') cancelRunOnServer(runId);
      // The server has the conversation by now (a new one included): the list shows it.
      void loadThreads();
      releasedStreams.delete(live);
      live.controller.abort();
    } else if (live.released === 'stop') {
      releasedStreams.delete(live);
      live.controller.abort();
    }
  }

  /**
   * A request let go before its run was named has ended. After leaving, that is the server
   * finishing a built-in agent's turn: the list shows the conversation as it is now, and when
   * the person is back in that conversation with nothing running its reply is shown.
   */
  function endReleased(live: LiveStream): void {
    if (!releasedStreams.delete(live) || live.released !== 'leave') return;
    void loadThreads();
    if (live.threadId && live.threadId === activeThreadId.value && !isStreaming.value) void reloadTranscript();
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
    // A reader has no list: the paths that refresh it (a stream let go, a new thread named) read nothing.
    if (reader) return;
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
      for (const item of items) if (item.titlePending) followTitle(item.id);
    } else {
      fail('load-threads', 'Could not load conversations', result);
    }
  }

  /**
   * A conversation whose AI title is still being generated (`titlePending`): read it again a
   * few times, backing off, and put the title in the list as soon as it changes. The server
   * starts the title when the first message is accepted, so it usually lands before the reply
   * ends; this covers the slower case, without a reload. Gives up after about 30 seconds (a
   * title that never comes keeps the fallback the list already shows).
   */
  function followTitle(threadId: string, attempt = 0): void {
    if (disposed || (attempt === 0 && titleFollows.has(threadId))) return;
    const wait = TITLE_FOLLOW_DELAYS_MS[attempt];
    if (wait === undefined) {
      titleFollows.delete(threadId);
      return;
    }
    titleFollows.set(threadId, setTimeout(() => void readTitle(threadId, attempt), wait));
  }

  async function readTitle(threadId: string, attempt: number): Promise<void> {
    const result = await threadApi.getDetail(threadId, 1);
    if (disposed) return;
    const detail = result.succeeded ? result.data : undefined;
    if (detail) {
      const title = detail.title || untitled;
      const stillPending = !!detail.titlePending;
      threads.value = threads.value.map((t) =>
        t.id === threadId ? { ...t, title, titlePending: stillPending || undefined } : t,
      );
      if (!stillPending) {
        titleFollows.delete(threadId);
        return;
      }
    }
    followTitle(threadId, attempt + 1);
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
    // Moving to another conversation is not stopping the one on screen (see `leaveTurn`).
    leaveTurn();
    releaseAttachmentUrls();
    activeThreadId.value = id;
    showPage(result.data.messages ?? [], result.data.hasMore ?? false);
    if (attentionEnabled) void attention.markRead(id);
    void reattach(id, seq);
  }

  /** Show a thread's newest page. */
  function showPage(page: Parameters<typeof toChatMessages>[0], hasMore: boolean): void {
    messages.value = mergeTaskCalls(toChatMessages(page));
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
      messages.value = mergeTaskCalls([...toChatMessages(page).filter((m) => !known.has(m.id)), ...messages.value]);
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
    // A reader that stopped following reads the transcript only.
    if (internal.attach && !internal.attach()) return;
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

  /**
   * A refresh asked for while the page could not do it (a turn streaming here, a conversation
   * opening): the conversation it was for, refreshed as soon as the page is free, so a turn the
   * server started meanwhile is not missed.
   */
  let refreshOwed: string | null = null;

  async function refreshThread(): Promise<void> {
    const threadId = activeThreadId.value;
    if (!threadId || isPendingId(threadId)) return;
    if (openingThreadId.value !== undefined || isStreaming.value) {
      refreshOwed = threadId;
      return;
    }
    refreshOwed = null;
    // A refresh is a new selection of the same thread: an older page or reattach still in flight
    // must not land on top of it.
    supersedeSelection();
    const seq = selectSeq;
    let result: Awaited<ReturnType<typeof threadApi.getMessages>>;
    try {
      result = await threadApi.getMessages(threadId, { limit: messageLimit });
    } catch (err) {
      if (seq === selectSeq) fail('load-thread', 'Could not refresh this conversation', err);
      return;
    }
    if (seq !== selectSeq || threadId !== activeThreadId.value) return;
    if (isStreaming.value) {
      // A turn began here while the page read: refresh again once it ends.
      refreshOwed = threadId;
      return;
    }
    if (!result.succeeded || !result.data) {
      fail('load-thread', 'Could not refresh this conversation', result);
      return;
    }
    // The transcript is the truth now: a parked turn's stream would write into a row it replaces.
    // The thread stays the same, so its attachment URLs are kept.
    leaveTurn();
    showPage(result.data.messages ?? [], result.data.hasMore ?? false);
    void reattach(threadId, seq);
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
    leaveTurn();
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

  /**
   * A message written while an external agent's turn runs: it waits on the server, which hands it to
   * the running agent or sends it when the turn ends. Its files are read under the rules of a normal
   * send and travel inline (an upload by reference cannot wait in the queue).
   * Refused (an attachment, a full queue, the server's rules): reported, and the words go back into an empty composer.
   */
  async function queueTurn(content: string, files: readonly File[], turnOptions: ChatTurnOptions): Promise<void> {
    const giveBack = () => {
      if (!inputText.value) inputText.value = content;
    };
    let parts: Awaited<ReturnType<typeof filesToContentParts>>['parts'] = null;
    if (files.length > 0) {
      try {
        ({ parts } = await filesToContentParts(content, files, {
          inlineFiles: options.inlineFiles,
          maxBytes: options.maxAttachmentBytes,
          maxFileBytes: options.maxFileAttachmentBytes,
        }));
      } catch (err) {
        giveBack();
        fail('attachment', 'The attachment could not be queued', err);
        return;
      }
    }
    const queued = await messageQueue.add({
      text: content.trim(),
      ...(parts ? { content: parts.filter((part) => part.type !== 'text') } : {}),
      model: options.model?.() ?? null,
      ...(options.effort?.() ? { effort: options.effort() } : {}),
      metadata: turnOptions.metadata ?? null,
    });
    if (!queued) giveBack();
    // Queued is sent, as far as the list goes: the server moved the conversation when it took the message
    else if (activeThreadId.value && !isPendingId(activeThreadId.value)) noteUserMessage(activeThreadId.value);
  }

  /**
   * The user just wrote in this conversation (a message, an edit, a regenerate, a queued message): its
   * rows take the time now, which is what the server records, so the list moves it where its next read will.
   */
  function noteUserMessage(threadId: string): void {
    const at = new Date().toISOString();
    const touch = (items: ThreadItem[]): ThreadItem[] =>
      items.some((t) => t.id === threadId) ? items.map((t) => (t.id === threadId ? { ...t, userMessageAt: at } : t)) : items;
    threads.value = touch(threads.value);
    recentPageThreads.value = touch(recentPageThreads.value);
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
      ...recentsQuery({ status: recentPageStatus.value, lastActivity: 'all', sort: 'lastMessage' }),
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
  if (options.listAgentIds && !reader) {
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
    if (!content.trim() && files.length === 0) return;
    if (isStreaming.value) {
      if (messageQueue.available.value) await queueTurn(content, files, turnOptions);
      return;
    }
    releaseParkedTurn();

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
          inlineFiles: options.inlineFiles,
          maxBytes: options.maxAttachmentBytes,
          maxFileBytes: options.maxFileAttachmentBytes,
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
      const sentAt = new Date().toISOString();
      threads.value = [
        {
          id: pendingThreadId,
          title: previewTitle(content),
          updatedAt: sentAt,
          userMessageAt: sentAt,
          createdAt: sentAt,
          pending: true,
          ...(turnOptions.origin ? { origin: turnOptions.origin } : {}),
        },
        ...threads.value,
      ];
    } else {
      noteUserMessage(activeThreadId.value);
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
    const effort = options.effort?.() || undefined;
    // The origin belongs to the thread, so only a turn that starts one carries it.
    const origin = !activeThreadId.value && turnOptions.origin ? turnOptions.origin : undefined;
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
          ...(effort ? { effort } : {}),
          ...(parts.parts ? { content: parts.parts } : {}),
          ...(turnOptions.metadata ? { metadata: turnOptions.metadata } : {}),
          ...(origin ? { origin } : {}),
        },
      },
      { userId, assistantId, titleHint: content, rewinds: false, draft: content },
      turn,
    );
  }

  async function regenerate(turnOptions: ChatTurnOptions = {}): Promise<void> {
    if (isStreaming.value) return;
    releaseParkedTurn();
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
    // The server stores the question again (regenerate) or the edited one: the user wrote just now
    noteUserMessage(threadId);

    const agentId = options.agentId?.() || undefined;
    const model = options.model?.() || undefined;
    const effort = options.effort?.() || undefined;
    await runTurn(
      {
        url: chatApi.getChatStreamUrl(),
        method: 'POST',
        body: {
          threadId,
          regenerate: true,
          ...(agentId ? { agentId } : {}),
          ...(model ? { model } : {}),
          ...(effort ? { effort } : {}),
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
    releaseParkedTurn();
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
    // The server stores the question again (regenerate) or the edited one: the user wrote just now
    noteUserMessage(threadId);

    const agentId = options.agentId?.() || undefined;
    const model = options.model?.() || undefined;
    const effort = options.effort?.() || undefined;
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
          ...(effort ? { effort } : {}),
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

    /**
     * The stream picks up a run that only waits on background tasks (its row says `'waiting'`):
     * the conversation stays free until the run answers again.
     */
    let parked = !!resume && messages.value.some((m) => m.id === resume.progress.assistantId && m.status === 'waiting');
    isStreaming.value = !parked;
    if (parked) {
      parkedTurn = turn;
      activeRunId.value = undefined;
    }
    // A new turn is a new attempt: the notice of the last refusal goes, and comes back if this one is refused too.
    usageLimit.value = null;
    // A new stream supersedes a turn left unresolved (a resumed one took it over already).
    unresolved.value = null;
    abortController = new AbortController();
    const signal = abortController.signal;
    const live: LiveStream = {
      turn,
      controller: abortController,
      runId: resume?.progress.runId,
      threadId: resume?.progress.threadId,
      open: true,
      released: null,
    };
    liveStream = live;
    /**
     * The turn was let go before its run was named (`stopTurn` / `leaveTurn`): its signs go to
     * `settleReleased`, none of them to the screen. `runId` is the run an event named.
     */
    const isReleased = (runIdNamed?: string | null): boolean => {
      if (!live.released) return false;
      if (releasedStreams.has(live)) settleReleased(live, runIdNamed ?? undefined);
      return true;
    };

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
    /**
     * Answers this run already finished inside it (`turn_completed`: the CLI went on after a
     * background task). Once there was one, the stream's own text total covers every answer,
     * so the last row is written from its own buffer only.
     */
    let finishedTurns = 0;
    /** The context compaction this run did (`compacted`), if any. */
    let compaction: ChatCompaction | null = null;
    /**
     * The row waits for the run's background tasks (status `'waiting'`, after `turn_completed`)
     * until the next answer begins. A stream picking the turn up again starts from the row.
     */
    let waiting = messages.value.some((m) => m.id === liveAssistantId && m.status === 'waiting');
    /**
     * A patch to the reply that is the next answer beginning: the wait is over, and the turn is
     * busy again while the run answers (Stop shows, a send waits for it).
     */
    const answering = (patch: Partial<ChatMessage>): Partial<ChatMessage> => {
      if (!waiting) return patch;
      waiting = false;
      if (parked) {
        parked = false;
        parkedTurn = null;
        isStreaming.value = true;
        if (runId) activeRunId.value = runId;
      }
      return { ...patch, isStreaming: true, status: 'streaming', backgroundTasks: null };
    };
    /** Another row than the live one has this id: the answer is already on screen. */
    const shownElsewhere = (id: string) => messages.value.some((m) => m.id === id && m.id !== liveAssistantId);

    /**
     * One answer ended and the run goes on (the CLI starts another turn when its background
     * tasks finish): the row is done and takes the stored message's id; what follows streams
     * into a new row after it.
     */
    const startNextAnswer = (event: ChatStreamEvent) => {
      const data = (event.eventData ?? {}) as Partial<ChatTurnCompletedEventData>;
      const stored = data.assistantMessageId;
      const liveRow = messages.value.find((m) => m.id === liveAssistantId);
      if (!stored && liveRow && !bufferedText && !bufferedReasoning && toolCalls.length === 0 && !liveRow.compaction) {
        // The turn said nothing and stored nothing (the CLI woke for a background task and stayed
        // quiet): the row goes on waiting for the next answer, rather than staying behind empty.
        updateMessage(liveAssistantId, {
          isStreaming: false,
          status: 'waiting',
          backgroundTasks: (data.backgroundTasks ?? []).map((task) => ({ ...task })),
        });
        finishedTurns += 1;
        waiting = true;
        parked = true;
        parkedTurn = turn;
        isStreaming.value = false;
        activeRunId.value = undefined;
        return;
      }
      if (stored && shownElsewhere(stored)) {
        // The thread's transcript already shows this answer: the stream was picked up from the
        // start (a reload, coming back to the conversation) and replayed it. The copy goes.
        messages.value = messages.value.filter((m) => m.id !== liveAssistantId);
      } else {
        updateMessage(liveAssistantId, {
          content: bufferedText,
          reasoning: bufferedReasoning || null,
          isStreaming: false,
          status: 'done',
          backgroundTasks: null,
        });
        if (stored) replaceMessageId(liveAssistantId, stored);
      }
      const previousId = stored ?? liveAssistantId;
      const nextId = newId();
      const index = messages.value.findIndex((m) => m.id === previousId);
      // Until the next answer begins, the run only waits on its background tasks: the row says
      // so quietly, with the tasks the server reported, and is not streaming. The conversation is
      // free meanwhile (the composer works); the stream stays open for the answer the run gives
      // when its tasks finish.
      const row: ChatMessage = {
        id: nextId,
        role: 'assistant',
        content: '',
        reasoning: '',
        createdAt: new Date().toISOString(),
        isStreaming: false,
        status: 'waiting',
        backgroundTasks: (data.backgroundTasks ?? []).map((task) => ({ ...task })),
      };
      messages.value = index === -1
        ? [...messages.value, row]
        : [...messages.value.slice(0, index + 1), row, ...messages.value.slice(index + 1)];
      liveAssistantId = nextId;
      bufferedText = '';
      bufferedReasoning = '';
      toolCalls = [];
      finishedTurns += 1;
      waiting = true;
      parked = true;
      parkedTurn = turn;
      isStreaming.value = false;
      activeRunId.value = undefined;
    };

    /**
     * A message the user queued reached the agent mid-turn (`user_message`). Taken in: the answer
     * so far is done (it takes the stored message's id; nothing was said yet, it goes), the user's
     * message follows it, and what the agent says next streams into a new row after that: the same
     * order the thread has after a reload. Declined: nothing here changes (the queue shows it held).
     */
    const takeUserMessage = (event: ChatStreamEvent) => {
      const data = (event.eventData ?? {}) as Partial<ChatUserMessageEventData>;
      if (!data.taken) return;
      const stored = data.assistantMessageId;
      const index = messages.value.findIndex((m) => m.id === liveAssistantId);
      const silent = !stored && !bufferedText && !bufferedReasoning && toolCalls.length === 0;
      // Already in the transcript (the stream was picked up from the start), or nothing said yet: the live row goes.
      const dropLive = (!!stored && shownElsewhere(stored)) || silent;
      const before = dropLive
        ? messages.value.filter((m) => m.id !== liveAssistantId)
        : messages.value.map((m) => (m.id === liveAssistantId
          ? { ...m, id: stored ?? m.id, content: bufferedText, reasoning: bufferedReasoning || null, isStreaming: false, status: 'done' as const }
          : m));
      const userId = data.threadMessageId;
      // The user's message already on screen (the stream was picked up from the start) is where the
      // answer to it goes: right after it, not where the replayed live row happened to be.
      const shownUser = userId ? before.findIndex((m) => m.id === userId) : -1;
      const at = shownUser !== -1 ? shownUser + 1 : index === -1 ? before.length : dropLive ? index : index + 1;
      const now = new Date().toISOString();
      const rows: ChatMessage[] = [];
      if (!userId || !shownElsewhere(userId)) {
        rows.push({ id: userId ?? newId(), role: 'user', content: data.text ?? '', createdAt: now });
      }
      const nextId = newId();
      rows.push({ id: nextId, role: 'assistant', content: '', reasoning: '', createdAt: now, isStreaming: true, status: 'streaming' });
      messages.value = [...before.slice(0, at), ...rows, ...before.slice(at)];
      liveAssistantId = nextId;
      bufferedText = '';
      bufferedReasoning = '';
      toolCalls = [];
      finishedTurns += 1;
    };

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
        if (isReleased() || !isCurrent()) return;
        bufferedText += text;
        updateMessage(liveAssistantId, answering({ content: bufferedText }));
      },
      onReasoningDelta: (text) => {
        if (isReleased() || !isCurrent()) return;
        bufferedReasoning += text;
        updateMessage(liveAssistantId, answering({ reasoning: bufferedReasoning }));
      },
      onToolCall: (names) => {
        if (isReleased() || !isCurrent()) return;
        if (startAlreadyDetailed) {
          startAlreadyDetailed = false;
          return;
        }
        toolCalls = [...toolCalls, ...names.map((name) => ({ name }))];
        updateMessage(liveAssistantId, answering({ toolCalls }));
      },
      onEvent: (event) => {
        startAlreadyDetailed = false;
        if (event.threadId && (live.released || isCurrent())) live.threadId = event.threadId;
        if (isReleased(event.eventType === 'run_started' ? event.runId : undefined)) return;
        if (!isCurrent()) return;
        // Where to pick the turn up again if this stream breaks.
        if (typeof event.sequence === 'number' && event.sequence > sequence) sequence = event.sequence;
        if (event.threadId) {
          streamThreadId = event.threadId;
          // A new conversation is that thread from the moment the server names it, not from the
          // end of its first turn: what is keyed on the conversation (the URL, a message queued
          // while the turn runs) works during that turn too. Nothing is reloaded; the stream goes on.
          adoptThread(event.threadId, rows.titleHint);
        }
        if (event.eventType === 'run_started') {
          // The run outlives this request: keep its id so Stop can cancel it on the server.
          if (event.runId) {
            runId = event.runId;
            live.runId = event.runId;
            if (!parked) activeRunId.value = event.runId;
          }
          adoptUserMessageId(event.userMessageId);
          return;
        }
        if (event.eventType === 'turn_completed') {
          startNextAnswer(event);
          return;
        }
        if (event.eventType === 'user_message') {
          takeUserMessage(event);
          return;
        }
        // A request the run waits on is a card at once; its outcome (answered here or elsewhere,
        // timed out, the run over) takes the card off
        if (event.eventType === 'permission_request' && runId && event.eventData) {
          approvals.push('run', runRequestFromEvent(runId, event.eventData as unknown as ChatPermissionRequestEventData));
          return;
        }
        if (event.eventType === 'permission_resolved' && event.eventData) {
          const requestId = (event.eventData as unknown as ChatPermissionResolvedEventData).requestId;
          if (requestId) approvals.remove(runRequestKey(requestId));
          return;
        }
        if (event.eventType === 'compacted') {
          compaction = { ...((event.eventData ?? {}) as ChatCompactedEventData) } as ChatCompaction;
          updateMessage(liveAssistantId, answering({ compaction }));
          return;
        }
        if (!event.toolCalls?.length) return;
        startAlreadyDetailed = !!event.isToolCall;
        let next = toolCalls;
        let liveChanged = false;
        for (const detail of event.toolCalls) {
          // A background task outlives the answer that started it: its progress updates that answer's call
          if (!next.some((c) => c.id && c.id === detail.id)) {
            const owner = findToolCallOwner(messages.value, detail, liveAssistantId);
            if (owner) {
              updateMessage(owner.id, { toolCalls: upsertToolCalls(owner.toolCalls ?? [], [detail]) });
              continue;
            }
          }
          const byId = detail.id ? next.findIndex((c) => c.id === detail.id) : -1;
          const idx = byId !== -1
            ? byId
            : next.findIndex((c) => c.name === detail.name && !c.id && isPendingCall(c));
          next = idx === -1
            ? [...next, { ...detail }]
            : next.map((c, i) => (i === idx ? mergeToolCall(c, detail) : c));
          liveChanged = true;
        }
        if (!liveChanged) return;
        toolCalls = next;
        updateMessage(liveAssistantId, answering({ toolCalls }));
      },
      onAgentSwitch: (name) => {
        if (isReleased() || !isCurrent()) return;
        updateMessage(liveAssistantId, { agentName: name });
      },
      onDone: (event) => {
        // Defence in depth: aborting should stop the stream before this fires,
        // but a late `done` frame writing `activeThreadId` would drag the user
        // out of the conversation they just opened.
        if (live.released || !isCurrent()) return;
        sawDone = true;
        if (event.isError) noteFailure(event);
        // The backend may have created the thread for this turn.
        if (event.threadId) adoptThread(event.threadId, rows.titleHint);
        // Swap local ids for persisted ones so feedback / regenerate address
        // real rows.
        adoptUserMessageId(event.userMessageId);
        // After a turn boundary the run's stored answer can be one already on screen (the last
        // turn said nothing of its own): the live row keeps its local id, and goes below.
        if (event.assistantMessageId && !shownElsewhere(event.assistantMessageId)) {
          replaceMessageId(liveAssistantId, event.assistantMessageId);
          liveAssistantId = event.assistantMessageId;
        }
        if (event.usage) updateMessage(liveAssistantId, { usage: event.usage });
      },
      onError: (err) => {
        if (live.released) return;
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
    live.open = false;
    if (unauthorized && isCurrent()) {
      streamFailed = true;
      failure = { operation: 'turn', message: 'Session expired, please login again', code: null, details: null, status: 401 };
    }

    // Superseded turn (stopped, left, or the user already started another one): its
    // tail must not touch shared state. `isStreaming = false` here would clear
    // the flag of the turn that is currently running.
    if (!isCurrent()) {
      endReleased(live);
      return 'superseded';
    }

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
      ? { error: wordingOf(failed), errorCode: failed.code, errorDetails: failed.details, backgroundTasks: null }
      : { error: null, errorCode: limit ? limit.code : null, errorDetails: null, backgroundTasks: null };
    if (limit?.code === USAGE_LIMIT_REACHED_ERROR_CODE) noteUsageLimit({ window: limit.window, resetsAt: limit.resetsAt });
    // The run answered before a turn boundary and nothing after it (its background tasks ended,
    // or the wait for them ran out): no answer is stored for that, so the row goes, as a reload
    // would show it.
    const noAnswerAfterBoundary = finishedTurns > 0 && !streamFailed && !limit && !bufferedText
      && !bufferedReasoning && toolCalls.length === 0 && !compaction;
    if (noAnswerAfterBoundary) {
      messages.value = messages.value.filter((m) => m.id !== liveAssistantId);
    } else updateMessage(
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
              // only returns what it carried itself. After a turn boundary the stream's total
              // also holds the earlier answers, which already have rows of their own.
              content: bufferedText || (finishedTurns > 0 ? '' : result.text),
              reasoning: bufferedReasoning || (finishedTurns > 0 ? null : result.reasoning || null),
              isStreaming: false,
              backgroundTasks: null,
              // A turn that only compacted the conversation has no answer: say so instead of an empty reply
              status: compaction && !bufferedText && !(finishedTurns === 0 && result.text) && toolCalls.length === 0 ? 'compacted' : 'done',
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
    parkedTurn = null;
    isStreaming.value = false;
    abortController = null;
    liveStream = null;
    activeRunId.value = undefined;
  }

  /**
   * The server put a new conversation's turn in a thread: open it, in place of the optimistic
   * sidebar row. No-op when a thread is already open.
   */
  function adoptThread(threadId: string, titleHint: string): void {
    if (activeThreadId.value) return;
    // The row first: whoever watches `activeThreadId` finds the list already naming it.
    if (pendingThreadId) {
      threads.value = threads.value.map((t) =>
        t.id === pendingThreadId
          ? toThreadItem({
              id: threadId,
              title: previewTitle(titleHint),
              lastActivityTime: new Date().toISOString(),
              lastUserMessageTime: t.userMessageAt,
              creationTime: t.createdAt,
              origin: t.origin,
            }, untitled)
          : t,
      );
      pendingThreadId = null;
    }
    activeThreadId.value = threadId;
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
    liveStream = null;
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

  // The cards follow the conversation on screen, asked again on an interval while a turn of it runs. Synchronous: a request
  // the stream announces right after naming a new conversation must not be cleared by a late follow.
  if (!reader) {
    watch([activeThreadId, isStreaming], ([threadId, running]) => approvals.follow(threadId && !isPendingId(threadId) ? threadId : null, running), {
      immediate: true,
      flush: 'sync',
    });
  }

  // A refresh the page could not do when asked runs once it is free, on the conversation it was for.
  watch([activeThreadId, isStreaming, openingThreadId], ([threadId, running, opening]) => {
    if (refreshOwed === null) return;
    if (refreshOwed !== threadId) refreshOwed = null;
    else if (!running && opening === undefined) void refreshThread();
  });

  // The queued messages follow the conversation on screen; only a turn with a server run can be queued behind.
  if (!reader) {
    watch(
      [activeThreadId, isStreaming, activeRunId],
      ([threadId, running, runId]) => messageQueue.follow(threadId && !isPendingId(threadId) ? threadId : null, running, !!runId),
      { immediate: true, flush: 'sync' },
    );
  }

  // Awaiting, running and unread on the rows: the server's summary, plus the turn streaming here (a
  // built-in agent's turn is not reported by the server) and the approvals it shows (an application's
  // own cards included). Before the first summary the rows keep what the list read said; a summary that
  // lists fewer unread ids than it counts only adds to them.
  function withAttention(items: ThreadItem[]): ThreadItem[] {
    const known = attention.loaded.value;
    const unreadComplete = attention.unreadIds.value.size >= attention.unreadCount.value;
    const onScreen = activeThreadId.value;
    const seen = onScreen && !pageHidden();
    const approvalsHere = approvals.items.value.length > 0;
    let changed = false;
    const next = items.map((item) => {
      // A new conversation's optimistic row is the turn streaming on screen
      const streamingHere = (item.id === onScreen || !!item.pending) && isStreaming.value;
      const running = known ? attention.runningIds.value.has(item.id) || streamingHere : !!item.running || streamingHere;
      const awaitingHere = item.id === onScreen && approvalsHere;
      const awaitingOwner = known
        ? attention.awaitingOwnerIds.value.has(item.id) || awaitingHere
        : !!item.awaitingOwner || awaitingHere;
      const listedUnread = attention.unreadIds.value.has(item.id);
      const unread = (seen && item.id === onScreen)
        ? false
        : known ? listedUnread || (!unreadComplete && !!item.unread) : !!item.unread;
      if (!!item.running === running && !!item.awaitingOwner === awaitingOwner && !!item.unread === unread) return item;
      changed = true;
      const { running: _running, awaitingOwner: _awaitingOwner, unread: _unread, ...rest } = item;
      return {
        ...rest,
        ...(awaitingOwner ? { awaitingOwner: true } : {}),
        ...(running ? { running: true } : {}),
        ...(unread ? { unread: true } : {}),
      };
    });
    return changed ? next : items;
  }

  function pageHidden(): boolean {
    return typeof document !== 'undefined' && document.visibilityState === 'hidden';
  }

  // The flags, then the list's order (the sidebar in its chosen sort, the Recents page in the default one).
  watch(
    [
      threads,
      recentPageThreads,
      attention.awaitingOwnerIds,
      attention.runningIds,
      attention.unreadIds,
      attention.loaded,
      activeThreadId,
      isStreaming,
      approvals.items,
      () => recentsPreferences.value.sort,
    ],
    () => {
      const sidebar = sortThreads(withAttention(threads.value), recentsPreferences.value.sort) as ThreadItem[];
      if (sidebar !== threads.value) threads.value = sidebar;
      const page = sortThreads(withAttention(recentPageThreads.value)) as ThreadItem[];
      if (page !== recentPageThreads.value) recentPageThreads.value = page;
    },
    { immediate: true, flush: 'sync' },
  );

  if (attentionEnabled) {
    // The conversation on screen is seen: a reply the summary still calls unread is marked read
    // while the page is in view (the page coming back reads the summary again, which lands here).
    watch(attention.unreadIds, (unread) => {
      const threadId = activeThreadId.value;
      if (threadId && !isPendingId(threadId) && unread.has(threadId) && !pageHidden()) void attention.markRead(threadId);
    });
    // An approval came or went on screen (answered here, or by someone else): the summary says how the
    // conversation stands now, sooner than the next tick.
    watch(
      () => approvals.items.value.length,
      (count, was) => {
        if (count !== was) void attention.refresh();
      },
    );
    // A turn named its run (it is running on the server now) or ended: read the summary again.
    // A turn that ends on screen with the page in view has been seen.
    watch(activeRunId, (runId) => {
      if (runId) void attention.refresh();
    });
    watch(isStreaming, (streaming, was) => {
      if (streaming || !was) return;
      const threadId = activeThreadId.value;
      if (threadId && !isPendingId(threadId) && !pageHidden()) void attention.markRead(threadId);
      void attention.refresh();
    });
  }

  // Guarded: the hook is usable outside a component (a store, a test), where an
  // unguarded `onScopeDispose` only emits a Vue warning and registers nothing.
  if (getCurrentScope()) {
    onScopeDispose(() => {
      // Leaving the page is not stopping the reply: the run carries on and its reply is
      // written to the thread. Only the streams are closed here, never a run cancelled.
      releaseTurn()?.controller.abort();
      for (const released of releasedStreams) released.controller.abort();
      releasedStreams.clear();
      isStreaming.value = false;
      releaseAttachmentUrls();
      approvals.dispose();
      messageQueue.dispose();
      disposed = true;
      for (const timer of titleFollows.values()) clearTimeout(timer);
      titleFollows.clear();
    });
  }

  // The visitor's first message, typed before signing in (`defineChatApp({ guestWelcome })`).
  if (!reader && options.sendWelcomeMessage !== false) {
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
    refreshThread,
    loadOlderMessages,
    newChat,
    deleteThread,
    recents,
    send,
    regenerate,
    editMessage,
    abort: stopTurn,
    stopTask,
    updateMessage,
    resolveAttachmentUrl,
    approvals,
    messageQueue,
    attention,
    leave: leaveTurn,
  };
}

/** What each queue failure says when the server gives no message. */
const QUEUE_FAILURES: Record<MessageQueueOperation, string> = {
  'load-queue': 'Could not read the queued messages',
  queue: 'The message could not be queued',
  'edit-queued': 'The queued message could not be changed',
  'remove-queued': 'The queued message could not be removed',
  'send-queued-now': 'The queued message could not be sent now',
};

function findLastIndex<T>(items: readonly T[], predicate: (item: T) => boolean): number {
  for (let i = items.length - 1; i >= 0; i -= 1) {
    if (predicate(items[i] as T)) return i;
  }
  return -1;
}
