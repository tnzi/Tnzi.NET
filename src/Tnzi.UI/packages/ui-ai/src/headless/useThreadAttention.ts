/**
 * `useThreadAttention` - which of the user's conversations need them: a turn blocked waiting for them
 * (`awaitingOwner`: a permission request, a question, an application's confirmation card), a turn going
 * on on the server (`running`) and a reply they have not seen (`unread`), with the counts. It reads the backend's
 * summary (`threadApi.getAttention`, `POST threads/attention`), which counts over all the user's
 * threads rather than the page of the list a sidebar holds.
 *
 * `useChatThreads` runs one for its conversations (`attention`) and lays its flags on the list rows.
 * A product that shows the counts somewhere the conversations are not on screen (a mode switch, a
 * nav badge) runs its own with the same agent filter.
 *
 * ## How fresh it is
 *
 * The summary is read on start, then every `runningPollMs` while something runs or awaits the user and every
 * `idlePollMs` otherwise, and at once when the page is shown, focused or back online. A hidden page
 * is not polled. `refresh()` reads it now (the caller knows a turn started or ended); reads asked for
 * while one is in flight are folded into one more read after it.
 *
 * `markRead` takes the conversation off the unread list at once and tells the server; a summary
 * read before the server had it does not bring the conversation back.
 *
 * A backend without the endpoint (404, 405 or 501) is asked once: the counts stay at zero and
 * nothing more is read. Other failures keep the last answer and are tried again on the next tick.
 */
import { computed, getCurrentScope, onScopeDispose, readonly, ref, shallowRef, type ComputedRef, type Ref } from 'vue';
import type { ThreadAttentionDto, useThreadApi } from '@tnzi/core/services/ai';

/** The endpoints the attention summary uses: `useThreadApi(http)` has them. */
export type ThreadAttentionApi = Pick<ReturnType<typeof useThreadApi>, 'getAttention' | 'markRead'>;

/** How often the summary is read while a conversation has a turn going on or awaiting the user. */
export const ATTENTION_RUNNING_POLL_MS = 5_000;
/** How often the summary is read while nothing runs (a turn started elsewhere, an automation's reply). */
export const ATTENTION_IDLE_POLL_MS = 30_000;

/** Statuses that mean the backend has no attention endpoint: not asked again. */
const UNSUPPORTED_STATUSES = new Set([404, 405, 501]);

export interface UseThreadAttentionOptions {
  /** `useThreadApi(http)`. */
  threadApi: ThreadAttentionApi;
  /**
   * Count only these agents' conversations (an id, several, or none/empty for all), read on every
   * request. Pass the same filter as the list it goes with (`useChatThreads`' `listAgentIds`).
   */
  agentIds?: () => string | readonly string[] | undefined | null;
  /** Read interval while a conversation runs. Default `ATTENTION_RUNNING_POLL_MS`; 0 = no polling then. */
  runningPollMs?: number;
  /** Read interval while nothing runs. Default `ATTENTION_IDLE_POLL_MS`; 0 = no polling then. */
  idlePollMs?: number;
  /** Read at once and start polling. Default true; false waits for `start()` (or a `refresh()`). */
  immediate?: boolean;
}

export interface ThreadAttention {
  /** Conversations with a turn blocked waiting for the user. */
  awaitingOwnerCount: Readonly<Ref<number>>;
  /** The conversations awaiting the user (at most 200 of them; `awaitingOwnerCount` is exact). */
  awaitingOwnerIds: Readonly<Ref<ReadonlySet<string>>>;
  /** Conversations with a turn going on on the server. */
  runningCount: Readonly<Ref<number>>;
  /** Conversations with a reply the user has not seen. */
  unreadCount: Readonly<Ref<number>>;
  /** The running conversations' ids (at most 200 of them; `runningCount` is exact). */
  runningIds: Readonly<Ref<ReadonlySet<string>>>;
  /** The unread conversations' ids (at most 200 of them; `unreadCount` is exact). */
  unreadIds: Readonly<Ref<ReadonlySet<string>>>;
  /** The summary has been read at least once (until then the counts are zero because nothing is known). */
  loaded: Readonly<Ref<boolean>>;
  /** Something needs the user: a conversation awaits them, runs or has an unread reply. */
  active: ComputedRef<boolean>;
  /** Read the summary now. */
  refresh: () => Promise<void>;
  /** The user saw this conversation: off the unread list at once, and told to the server. Failures are ignored. */
  markRead: (threadId: string) => Promise<void>;
  /** Start reading and polling (with `immediate: false`). */
  start: () => void;
  /** Stop polling and listening. Done by itself when the calling scope ends. */
  dispose: () => void;
}

/** The agent filter as the request takes it. */
function toAgentIds(value: string | readonly string[] | undefined | null): string[] | undefined {
  if (!value) return undefined;
  const ids = (typeof value === 'string' ? [value] : [...value]).filter(Boolean);
  return ids.length > 0 ? ids : undefined;
}

export function useThreadAttention(options: UseThreadAttentionOptions): ThreadAttention {
  const { threadApi } = options;
  const runningPollMs = options.runningPollMs ?? ATTENTION_RUNNING_POLL_MS;
  const idlePollMs = options.idlePollMs ?? ATTENTION_IDLE_POLL_MS;

  const awaitingOwnerCount = ref(0);
  const runningCount = ref(0);
  const unreadCount = ref(0);
  const awaitingOwnerIds = shallowRef<ReadonlySet<string>>(new Set());
  const runningIds = shallowRef<ReadonlySet<string>>(new Set());
  const unreadIds = shallowRef<ReadonlySet<string>>(new Set());
  const loaded = ref(false);
  const active = computed(() => awaitingOwnerCount.value > 0 || runningCount.value > 0 || unreadCount.value > 0);

  let started = false;
  let disposed = false;
  let unsupported = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let inFlight: Promise<void> | null = null;
  let again = false;
  let stopListening: (() => void) | null = null;

  /**
   * Conversations marked read here that a summary may still report unread: one read before the
   * server took the mark. Bumped on every mark, so a read knows whether a mark happened under it.
   */
  const marked = new Set<string>();
  let markSeq = 0;
  let marksInFlight = 0;

  const isHidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';

  function schedule(): void {
    if (timer) clearTimeout(timer);
    timer = null;
    if (!started || disposed || unsupported || isHidden()) return;
    /* A conversation awaiting the user is a turn in progress too: the answer may come from another tab or device. */
    const interval = runningCount.value > 0 || awaitingOwnerCount.value > 0 ? runningPollMs : idlePollMs;
    if (interval > 0) timer = setTimeout(() => void refresh(), interval);
  }

  function apply(summary: ThreadAttentionDto, clean: boolean): void {
    let unread = summary.unreadThreadIds ?? [];
    let unreadTotal = summary.unreadCount ?? unread.length;
    if (clean) {
      marked.clear();
    } else if (marked.size > 0) {
      const before = unread.length;
      unread = unread.filter((id) => !marked.has(id));
      unreadTotal = Math.max(0, unreadTotal - (before - unread.length));
    }
    awaitingOwnerCount.value = summary.awaitingOwnerCount ?? summary.awaitingOwnerThreadIds?.length ?? 0;
    awaitingOwnerIds.value = new Set(summary.awaitingOwnerThreadIds ?? []);
    runningCount.value = summary.runningCount ?? summary.runningThreadIds?.length ?? 0;
    runningIds.value = new Set(summary.runningThreadIds ?? []);
    unreadCount.value = unreadTotal;
    unreadIds.value = new Set(unread);
    loaded.value = true;
  }

  async function read(): Promise<void> {
    const seqAtStart = markSeq;
    const marksPendingAtStart = marksInFlight > 0;
    try {
      const result = await threadApi.getAttention({ agentIds: toAgentIds(options.agentIds?.()) ?? null });
      if (disposed) return;
      if (result.succeeded && result.data) {
        apply(result.data, !marksPendingAtStart && seqAtStart === markSeq);
      } else if (result.code != null && UNSUPPORTED_STATUSES.has(result.code)) {
        unsupported = true;
      }
    } catch (error) {
      const status = (error as { status?: unknown } | null)?.status;
      if (typeof status === 'number' && UNSUPPORTED_STATUSES.has(status)) unsupported = true;
      // Otherwise keep the last answer: a dropped connection is tried again on the next tick
    }
  }

  function refresh(): Promise<void> {
    if (disposed || unsupported) return Promise.resolve();
    if (inFlight) {
      again = true;
      return inFlight;
    }
    inFlight = (async () => {
      do {
        again = false;
        await read();
      } while (again && !disposed && !unsupported);
    })().finally(() => {
      inFlight = null;
      schedule();
    });
    return inFlight;
  }

  async function markRead(threadId: string): Promise<void> {
    if (!threadId || disposed) return;
    markSeq += 1;
    marked.add(threadId);
    if (unreadIds.value.has(threadId)) {
      const next = new Set(unreadIds.value);
      next.delete(threadId);
      unreadIds.value = next;
      unreadCount.value = Math.max(0, unreadCount.value - 1);
    }
    marksInFlight += 1;
    try {
      await threadApi.markRead(threadId);
    } catch {
      // The mark is a courtesy: the next open marks it again
    } finally {
      marksInFlight -= 1;
    }
  }

  function listen(): () => void {
    if (typeof document === 'undefined' || typeof window === 'undefined') return () => {};
    const onVisibility = () => {
      if (isHidden()) schedule();
      else void refresh();
    };
    const onBack = () => void refresh();
    document.addEventListener('visibilitychange', onVisibility);
    window.addEventListener('focus', onBack);
    window.addEventListener('online', onBack);
    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      window.removeEventListener('focus', onBack);
      window.removeEventListener('online', onBack);
    };
  }

  function start(): void {
    if (started || disposed) return;
    started = true;
    stopListening = listen();
    void refresh();
  }

  function dispose(): void {
    disposed = true;
    if (timer) clearTimeout(timer);
    timer = null;
    stopListening?.();
    stopListening = null;
  }

  if (getCurrentScope()) onScopeDispose(dispose);
  if (options.immediate !== false) start();

  return {
    awaitingOwnerCount: readonly(awaitingOwnerCount),
    awaitingOwnerIds: computed(() => awaitingOwnerIds.value),
    runningCount: readonly(runningCount),
    unreadCount: readonly(unreadCount),
    runningIds: computed(() => runningIds.value),
    unreadIds: computed(() => unreadIds.value),
    loaded: readonly(loaded),
    active,
    refresh,
    markRead,
    start,
    dispose,
  };
}
