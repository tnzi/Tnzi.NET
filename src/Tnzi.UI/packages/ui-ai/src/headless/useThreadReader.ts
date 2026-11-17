/**
 * `useThreadReader` - one conversation, read only, followed while its turns run.
 *
 * For a page that shows the record of a conversation nobody writes in here (an unattended run's
 * thread, often one kept out of the conversation list): it opens the thread by id on its newest
 * messages and pages back on demand, attaches to the turn running in it (from the start of that
 * turn, as Chat does after a reload), and notices a turn that starts later by asking the server
 * for the thread's running turns every `followPollMs` while nothing streams and the page is in
 * view. Once a turn ended, the rows are the conversation as the server keeps it.
 *
 * It is the conversation loop of `useChatThreads` in a reading mode, so a turn streams, breaks and
 * recovers exactly as it does in Chat. What it leaves out is everything a reader must not do:
 * nothing sends, regenerates, edits or stops a turn; the thread list, titles, attention and read
 * marks, approvals, the message queue and the welcome message are never read or written; and no
 * context is provided for `TChatApp` or a composer to pick up. The requests it makes are the
 * thread's messages (`threadApi.getMessages`), its attachments when shown
 * (`resolveAttachmentUrl`), its running turns (`chatApi.getActiveRuns`) and their streams.
 *
 * Only turns the server runs as a run (external CLI agents) can be followed live; a built-in
 * agent answers on the request that started the turn, so its answer shows with the next
 * `refresh()`.
 */
import { computed, getCurrentScope, onScopeDispose, readonly, ref, toValue, watch, type ComputedRef, type MaybeRefOrGetter, type Ref } from 'vue';
import type { ChatMessage } from './useChat';
import { createChatThreads, type UseChatThreadsOptions } from './useChatThreads';

/** How often a followed conversation is asked whether a turn started in it, while none streams. */
export const THREAD_FOLLOW_POLL_MS = 3_000;

export interface UseThreadReaderOptions
  extends Pick<UseChatThreadsOptions, 'http' | 'threadApi' | 'chatApi' | 'messageLimit' | 'onError' | 'describeError'> {
  /**
   * The conversation to show, read reactively: a new id opens that conversation (letting the
   * previous one's turn go, never cancelling it); null or undefined shows none. Any thread the
   * user may read can be opened, listed or not.
   */
  threadId: MaybeRefOrGetter<string | null | undefined>;
  /** Start following (default true). Off, the conversation is read once and `follow()` starts. */
  follow?: boolean;
  /** How often to ask whether a turn started while none streams. Default `THREAD_FOLLOW_POLL_MS`; 0 = only on opening, `follow()` and the page coming back. */
  followPollMs?: number;
}

export interface UseThreadReaderReturn {
  /** The conversation on screen (set when its messages arrived). */
  threadId: Readonly<Ref<string | undefined>>;
  /** Its messages, oldest first, a running turn's answer included as it streams. */
  messages: Readonly<Ref<readonly ChatMessage[]>>;
  /** The conversation is being opened (its first page is on its way). */
  loading: ComputedRef<boolean>;
  /** A turn of it is streaming on screen. */
  isStreaming: Readonly<Ref<boolean>>;
  /** The run streaming on screen, if it is one. */
  runId: Readonly<Ref<string | undefined>>;
  /** Older messages exist than those shown; `loadOlderMessages` reads the next page back. */
  hasOlderMessages: Readonly<Ref<boolean>>;
  loadingOlderMessages: Readonly<Ref<boolean>>;
  loadOlderMessages: () => Promise<void>;
  /** Whether running turns are followed. */
  following: Readonly<Ref<boolean>>;
  /** Follow again: read the conversation and attach to its running turn, then watch for new ones. */
  follow: () => Promise<void>;
  /**
   * Stop following: the stream on screen closes (the turn goes on on the server), the answer keeps
   * what had arrived, and no new turn is looked for. Unmounting does the same.
   */
  stopFollowing: () => void;
  /** Read the conversation again as the server has it (and, while following, attach to its running turn). */
  refresh: () => Promise<void>;
  /** An object URL for one of its attachments (`TChatApp`'s `resolveAttachmentUrl`), revoked when the conversation changes. */
  resolveAttachmentUrl: (attachmentId: string) => Promise<string | null>;
}

export function useThreadReader(options: UseThreadReaderOptions): UseThreadReaderReturn {
  const following = ref(options.follow !== false);
  const core = createChatThreads(
    {
      http: options.http,
      threadApi: options.threadApi,
      chatApi: options.chatApi,
      messageLimit: options.messageLimit,
      onError: options.onError,
      describeError: options.describeError,
      // Belt and braces: the reading mode already leaves these out.
      recentsStorageKey: null,
      attention: false,
      sendWelcomeMessage: false,
      approvalPollMs: 0,
      messageQueuePollMs: 0,
    },
    'reader',
    { attach: () => following.value },
  );
  const pollMs = options.followPollMs ?? THREAD_FOLLOW_POLL_MS;
  /** Runs this page attached to already: one still listed just after its stream ended is not a new turn. */
  const followed = new Set<string>();
  let timer: ReturnType<typeof setTimeout> | null = null;
  let checking = false;
  let disposed = false;

  const pageHidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';
  /** Nothing streams and nothing opens: the moment to look for a turn that started. */
  const idle = () =>
    following.value && !disposed && !!core.activeThreadId.value
    && core.openingThreadId.value === undefined && !core.isStreaming.value;

  function clearTimer(): void {
    if (timer) clearTimeout(timer);
    timer = null;
  }

  function schedule(): void {
    clearTimer();
    if (pollMs <= 0 || !idle() || pageHidden()) return;
    timer = setTimeout(() => void check(), pollMs);
  }

  /** Ask the server for the conversation's running turns; one this page has not followed yet is shown. */
  async function check(): Promise<void> {
    clearTimer();
    if (checking || !idle()) return;
    const threadId = core.activeThreadId.value as string;
    checking = true;
    let runs: Awaited<ReturnType<typeof options.chatApi.getActiveRuns>> | null = null;
    try {
      runs = await options.chatApi.getActiveRuns(threadId);
    } catch {
      // Asked again on the next tick: a dropped connection is not worth a report every few seconds
    } finally {
      checking = false;
    }
    if (!idle() || threadId !== core.activeThreadId.value) return;
    const fresh = runs?.succeeded ? (runs.data ?? []).some((run) => !followed.has(run.runId)) : false;
    // The transcript first (the turn's question is in it), then its run from the start.
    if (fresh) await core.refreshThread();
    schedule();
  }

  /** The stream on screen goes; the answer keeps what had arrived and stops showing as streaming. */
  function letGo(): void {
    core.leave();
    for (const message of core.messages.value) {
      if (message.isStreaming) core.updateMessage(message.id, { isStreaming: false });
    }
  }

  function stopFollowing(): void {
    following.value = false;
    clearTimer();
    letGo();
  }

  async function follow(): Promise<void> {
    following.value = true;
    followed.clear();
    const wanted = toValue(options.threadId) || undefined;
    if (wanted && wanted !== core.activeThreadId.value) await core.selectThread(wanted);
    else if (core.activeThreadId.value) await core.refreshThread();
  }

  async function refresh(): Promise<void> {
    await core.refreshThread();
  }

  // Every run attached to here is remembered (opening, a refresh, a turn noticed by `check`).
  watch(core.activeRunId, (runId) => {
    if (runId) followed.add(runId);
  }, { flush: 'sync' });

  // Looking for a new turn only while nothing streams or opens.
  watch(
    [core.activeThreadId, core.isStreaming, core.openingThreadId, following],
    () => schedule(),
  );

  watch(
    () => toValue(options.threadId) || undefined,
    (threadId) => {
      followed.clear();
      if (!threadId) {
        core.newChat();
        return;
      }
      void core.selectThread(threadId);
    },
    { immediate: true },
  );

  // A hidden page asks nothing; shown again, it asks at once.
  const onVisibility = () => {
    if (pageHidden()) clearTimer();
    else void check();
  };
  // Only with a scope to remove it again; outside one (a store, a test) the timer alone follows.
  if (getCurrentScope()) {
    if (typeof document !== 'undefined') document.addEventListener('visibilitychange', onVisibility);
    onScopeDispose(() => {
      disposed = true;
      clearTimer();
      if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', onVisibility);
    });
  }

  return {
    threadId: readonly(core.activeThreadId),
    messages: computed<readonly ChatMessage[]>(() => core.messages.value),
    loading: computed(() => core.openingThreadId.value !== undefined),
    isStreaming: readonly(core.isStreaming),
    runId: readonly(core.activeRunId),
    hasOlderMessages: readonly(core.hasOlderMessages),
    loadingOlderMessages: readonly(core.loadingOlderMessages),
    loadOlderMessages: core.loadOlderMessages,
    following: readonly(following),
    follow,
    stopFollowing,
    refresh,
    resolveAttachmentUrl: core.resolveAttachmentUrl,
  };
}
