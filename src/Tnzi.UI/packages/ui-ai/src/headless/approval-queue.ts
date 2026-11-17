/**
 * The cards a conversation shows: what the agent (or the application) waits on its user for, during
 * a turn or after it (a call an unattended turn left for its owner's approval). Items come from one
 * or more sources: each is asked when the conversation is followed, running or not (a page opened
 * later must find the ones still waiting), every `intervalMs` while its turn runs, and once more when
 * the turn ends (what still waits stays, what went with the turn leaves); an idle conversation is not
 * asked on an interval. A stream can push an item the moment it appears. The cards clear when another
 * conversation opens. A card the user answered, or whose time ran out, leaves at once rather than
 * at the next ask, and stays out until its source stops listing it.
 *
 * Independent of what the cards are: an item has a `kind`, and whoever renders the queue decides
 * which card shows each kind (`TPendingApprovals`).
 */
import { computed, readonly, ref, shallowRef, type ComputedRef, type DeepReadonly, type Ref } from 'vue';

/** One thing the conversation waits on its user for. */
export interface PendingInteraction<TData = unknown> {
  /** Unique among the cards of all sources (a source prefixes its own ids). */
  key: string;
  /**
   * Which card shows it: `permission`, `question` and `plan` are an external agent's requests;
   * any other kind is the application's own.
   */
  kind: string;
  /** ISO time after which it can no longer be answered. */
  expiresAt: string;
  /** When it appeared: the cards are in this order, oldest first. */
  createdAt?: string | null;
  /** What the card is built from. */
  data: TData;
}

/** An answer to a card. */
export interface ApprovalAnswer {
  /** One of the item's options (an external agent's request), or the application's own id. */
  optionId: string;
  /** A note handed back with a refusal (a plan's feedback). */
  message?: string | null;
  /** A question card's answers: question text to its answers. */
  answers?: Record<string, string[]> | null;
  /** The code typed on a card that asks for one. */
  code?: string;
}

/**
 * `answered`: the answer went through. `closed`: the item no longer waited (answered elsewhere,
 * timed out, its run over); the card leaves all the same.
 */
export type ApprovalAnswerOutcome = 'answered' | 'closed';

/** Where pending items come from, and where their answers go. */
export interface ApprovalSource {
  /** Names the source; every item it lists or that is pushed for it is answered through it. */
  id: string;
  /**
   * What the conversation waits for from this source now. Asked of any conversation followed,
   * including one with no running turn: a source with nothing for it answers an empty list.
   */
  load: (threadId: string) => Promise<readonly PendingInteraction[]>;
  /**
   * Sends an answer. Resolves `closed` when the item no longer waits; rejects on any other
   * failure, with an `Error` whose message can be shown (the card stays).
   */
  answer: (item: PendingInteraction, answer: ApprovalAnswer) => Promise<ApprovalAnswerOutcome | void>;
}

/** An item on screen, with the source it came from. */
export interface QueuedInteraction<TData = unknown> extends PendingInteraction<TData> {
  source: string;
}

export interface ApprovalQueueOptions {
  sources: readonly ApprovalSource[];
  /** How often the sources are asked while a turn runs; 0 asks only when following and when a turn ends. Default `APPROVAL_POLL_MS`. */
  intervalMs?: number;
  now?: () => number;
  /** A source that failed to load; the next ask tries again. */
  onError?: (error: unknown, source: string) => void;
}

export const APPROVAL_POLL_MS = 2000;

export interface ApprovalQueue {
  /** The cards, oldest first. */
  items: ComputedRef<readonly QueuedInteraction[]>;
  /** Keys whose answer is on its way. */
  answering: DeepReadonly<Ref<ReadonlySet<string>>>;
  /**
   * The conversation on screen, and whether a turn of it runs: a conversation newly followed is asked
   * at once, a running one again every interval, and one whose turn just ended once more.
   */
  follow: (threadId: string | null | undefined, running: boolean) => void;
  /** Asks every source now. */
  refresh: () => Promise<void>;
  /** An item the stream announced: shown at once, unless it was already answered or has expired. */
  push: (source: string, item: PendingInteraction) => void;
  /** Takes a card off at once: answered, resolved elsewhere, or its time ran out. */
  remove: (key: string) => void;
  /**
   * Answers the card `key` through its source: the card is busy meanwhile and goes when the answer
   * went through or the item no longer waited. Rejects (the card stays) on any other failure.
   */
  answer: (key: string, answer: ApprovalAnswer) => Promise<ApprovalAnswerOutcome>;
  dispose: () => void;
}

function byAge(a: QueuedInteraction, b: QueuedInteraction): number {
  const at = (item: QueuedInteraction) => (item.createdAt ? Date.parse(item.createdAt) : Number.NaN);
  const left = at(a);
  const right = at(b);
  if (Number.isNaN(left) || Number.isNaN(right)) return 0;
  return left - right;
}

export function createApprovalQueue(options: ApprovalQueueOptions): ApprovalQueue {
  const now = options.now ?? (() => Date.now());
  const intervalMs = options.intervalMs ?? APPROVAL_POLL_MS;
  const sources = new Map(options.sources.map((source) => [source.id, source] as const));
  const items = shallowRef<readonly QueuedInteraction[]>([]);
  const answering = ref<ReadonlySet<string>>(new Set());
  let threadId: string | null = null;
  let running = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  /** The ask that is out, and the conversation it is for. */
  let inFlight: { threadId: string | null; promise: Promise<void> } | null = null;
  /** Keys answered or expired here, kept out until no source lists them. */
  let gone: ReadonlySet<string> = new Set();
  /** Keys pushed while an ask was out: that ask may have been answered before they existed. */
  let pushedDuringAsk: ReadonlySet<string> = new Set();

  const live = (item: PendingInteraction) => !gone.has(item.key) && Date.parse(item.expiresAt) > now();

  async function ask(): Promise<void> {
    const asked = threadId;
    if (!asked || sources.size === 0) return;
    pushedDuringAsk = new Set();
    const results = await Promise.allSettled(
      [...sources.values()].map(async (source) => ({ source: source.id, list: await source.load(asked) })),
    );
    if (asked !== threadId) return;

    const loaded: QueuedInteraction[] = [];
    const failed = new Set<string>();
    results.forEach((result, index) => {
      const sourceId = [...sources.keys()][index]!;
      if (result.status === 'rejected') {
        failed.add(sourceId);
        options.onError?.(result.reason, sourceId);
        return;
      }
      for (const item of result.value.list) loaded.push({ ...item, source: sourceId });
    });

    const listed = new Set(loaded.map((item) => item.key));
    gone = new Set([...gone].filter((key) => listed.has(key)));
    // A failed source keeps what it showed; a card pushed while the ask was out stays until a later ask
    const kept = items.value.filter((item) => !listed.has(item.key) && (failed.has(item.source) || pushedDuringAsk.has(item.key)));
    items.value = [...loaded.filter(live), ...kept.filter(live)].sort(byAge);
  }

  function refresh(): Promise<void> {
    // An ask out for another conversation is dropped when it lands: this one needs its own
    if (inFlight && inFlight.threadId === threadId) return inFlight.promise;
    const entry = { threadId, promise: Promise.resolve() };
    entry.promise = ask().finally(() => {
      if (inFlight === entry) inFlight = null;
    });
    inFlight = entry;
    return entry.promise;
  }

  /** Asks after whatever ask is out now, which may have been answered before the change to see. */
  function refreshAfterOut(): Promise<void> {
    const out = inFlight && inFlight.threadId === threadId ? inFlight.promise : null;
    return out ? out.then(refresh) : refresh();
  }

  function schedule(): void {
    if (timer !== null || intervalMs <= 0) return;
    timer = setTimeout(() => {
      timer = null;
      if (!running || !threadId) return;
      void refresh().finally(schedule);
    }, intervalMs);
  }

  function stopAsking(): void {
    if (timer !== null) clearTimeout(timer);
    timer = null;
  }

  function follow(next: string | null | undefined, isRunning: boolean): void {
    const id = next ?? null;
    const opened = id !== threadId;
    const wasRunning = running;
    if (opened) {
      threadId = id;
      items.value = [];
      gone = new Set();
    }
    running = !!id && isRunning;
    if (!id) {
      stopAsking();
      return;
    }
    if (running) {
      void refresh();
      schedule();
      return;
    }
    stopAsking();
    if (opened) void refresh();
    // The turn ended: ask again rather than clear, since a card can outlive the turn that left it
    else if (wasRunning) void refreshAfterOut();
  }

  function push(source: string, item: PendingInteraction): void {
    if (!sources.has(source) || !live(item)) return;
    pushedDuringAsk = new Set([...pushedDuringAsk, item.key]);
    const others = items.value.filter((existing) => existing.key !== item.key);
    items.value = [...others, { ...item, source }].sort(byAge);
  }

  function remove(key: string): void {
    gone = new Set([...gone, key]);
    items.value = items.value.filter((item) => item.key !== key);
  }

  async function answer(key: string, reply: ApprovalAnswer): Promise<ApprovalAnswerOutcome> {
    const item = items.value.find((i) => i.key === key);
    const source = item ? sources.get(item.source) : undefined;
    if (!item || !source) return 'closed';
    if (answering.value.has(key)) return 'answered';
    answering.value = new Set([...answering.value, key]);
    try {
      const outcome = (await source.answer(item, reply)) ?? 'answered';
      remove(key);
      return outcome;
    } finally {
      answering.value = new Set([...answering.value].filter((k) => k !== key));
    }
  }

  function dispose(): void {
    stopAsking();
    threadId = null;
    running = false;
  }

  return {
    items: computed(() => items.value),
    answering: readonly(answering),
    follow,
    refresh,
    push,
    remove,
    answer,
    dispose,
  };
}
