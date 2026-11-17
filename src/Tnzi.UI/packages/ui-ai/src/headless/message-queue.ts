/**
 * A conversation's queued messages: what the user writes while a turn of an external agent runs.
 * The queue lives on the server (`chat/threads/{threadId}/queue`): an agent whose turn can take
 * messages while it works gets each one at once and takes it in between two steps (`HandedOver`,
 * then `Delivered`); otherwise the server sends everything waiting as one turn when the turn ends.
 * A reload shows it again and it goes with the page closed. This lists it over the composer, queues
 * (with attachments), edits and removes its messages, and sends one now (handed to the running
 * agent, or, where the turn cannot take it, `sendNowStopsTurn`, the server stops the turn for it).
 * A message the agent took in (`Delivered`) is a user message of the conversation from then on: the
 * server keeps its row until the turn that took it ends, but it is not listed here, and its row
 * going away is not a turn that went out.
 *
 * The turn the server starts is not one this page started, so it does not stream here by itself:
 * when a listed message goes without the page taking it away, `onDelivered` says so and the caller
 * follows the conversation again (`useChatThreads().refreshThread`). A message seen gone while a
 * turn is still on screen (the server can start the next turn before this page's stream of the last
 * one ends) is owed: `onDelivered` fires when that turn ends here, even though the list is empty by then.
 * Right after a turn ends the queue is read a few times in quick succession, so what the server did
 * with the waiting messages (sent them, or held them because the turn was stopped) shows at once.
 *
 * Only a turn with a server run can be queued behind (`available`): a built-in agent's turn lives
 * in the request that started it, and the server cannot see it running.
 */
import { computed, readonly, ref, shallowRef, type ComputedRef, type DeepReadonly, type Ref } from 'vue';
import { AiChatErrorCodes, type QueuedMessageDto, type QueueMessageRequestDto, type useChatApi } from '@tnzi/core/services/ai';
import type { ChatThreadsOperation } from './chat-errors';

/** The endpoints the queue uses: `useChatApi(http)` has them. */
export type MessageQueueApi = Pick<
  ReturnType<typeof useChatApi>,
  'getQueue' | 'queueMessage' | 'updateQueuedMessage' | 'removeQueuedMessage' | 'sendQueuedMessageNow'
>;

/** What the queue was doing when it failed. */
export type MessageQueueOperation = Extract<
  ChatThreadsOperation,
  'load-queue' | 'queue' | 'edit-queued' | 'remove-queued' | 'send-queued-now'
>;

/** How often the queue is read while something in it waits to go: the turn's end is the server's to hear. */
export const QUEUE_POLL_MS = 2000;

/** How soon the queue is read again right after a turn ends, and how many times, before the usual interval. */
export const QUEUE_AFTER_TURN_MS = 400;
const QUEUE_AFTER_TURN_READS = 3;

export interface MessageQueueOptions {
  api: MessageQueueApi;
  /** How often the queue is read while a message in it waits to go. Default `QUEUE_POLL_MS`; 0 reads only when following. */
  intervalMs?: number;
  /**
   * A listed message went out as a turn this page did not start (and no turn streams here): follow
   * the conversation to show it.
   */
  onDelivered?: () => void;
  /** A request failed; `source` is the refused `ApiResult` or the thrown error. */
  onError?: (operation: MessageQueueOperation, source: unknown) => void;
}

export interface MessageQueue {
  /**
   * The queued messages, in the order they go: the one to send now first, then as written. A message
   * the agent took in is not among them: it is in the conversation.
   */
  items: ComputedRef<readonly QueuedMessageDto[]>;
  /** A turn with a server run is on screen: a message written now waits on the server for it. */
  available: ComputedRef<boolean>;
  /** A turn of the conversation is on screen (what `follow` was last told). */
  running: ComputedRef<boolean>;
  /** Ids whose edit, removal or send-now is on its way. */
  busy: DeepReadonly<Ref<ReadonlySet<string>>>;
  /**
   * The conversation on screen, whether a turn of it runs, and whether that turn has a server run
   * (an external agent's). A new conversation is read at once; a turn that ends makes it read again.
   */
  follow: (threadId: string | null | undefined, running: boolean, serverRun: boolean) => void;
  /** Queues a message in the conversation on screen; null when there is none or the server refused it (said through `onError`). */
  add: (input: QueueMessageRequestDto) => Promise<QueuedMessageDto | null>;
  /** New words for a queued message; false when refused (said through `onError`). */
  edit: (id: string, text: string) => Promise<boolean>;
  /** Removes a queued message; false when refused (said through `onError`). */
  remove: (id: string) => Promise<boolean>;
  /**
   * Sends a queued message now: handed to the running agent without stopping anything, or, when the
   * turn cannot take it (`QueuedMessageDto.sendNowStopsTurn`), the server stops the turn and sends it alone.
   * False when refused.
   */
  sendNow: (id: string) => Promise<boolean>;
  /** Reads the queue now. */
  refresh: () => Promise<void>;
  dispose: () => void;
}

/**
 * The messages that went out as a turn: listed before (waiting or going), gone now, and not taken
 * away by this page. A held one does not go by itself, so its going is the page's own doing; one the
 * agent took in is already in the conversation, and its row goes when that turn ends, not as a turn.
 */
export function wentOut(
  before: readonly QueuedMessageDto[],
  after: readonly QueuedMessageDto[],
  removedHere: ReadonlySet<string>,
): string[] {
  const still = new Set(after.map((m) => m.id));
  return before
    .filter((m) => m.state !== 'Held' && !takenIn(m) && !still.has(m.id) && !removedHere.has(m.id))
    .map((m) => m.id);
}

/** The agent took it in: it is a user message of the conversation, no longer a queued one. */
export function takenIn(item: QueuedMessageDto): boolean {
  return item.state === 'Delivered';
}

/** Whether the user may still edit, remove or send it now: it waits, or is held (not going, not with the agent). */
export function isChangeable(item: QueuedMessageDto): boolean {
  return item.state === 'Queued' || item.state === 'Held';
}

/** Whether something waits to go by itself, so the queue keeps being read to see it go (held and taken-in ones do not). */
export function waitsToGo(items: readonly QueuedMessageDto[]): boolean {
  return items.some((m) => m.state !== 'Held' && !takenIn(m));
}

/**
 * What the line under a queued message says about where it stands: `sending` (going out now),
 * `next` (goes next, by itself), `handed` (written to the running agent, which reads it after its
 * current step; it is not in the conversation until the agent takes it in), `stopped` (held: the
 * turn it waited for was stopped), `failed` (held: its turn was refused, its sending interrupted, or
 * the agent did not take it), `waiting` (nothing to say).
 */
export type QueuedLine = 'waiting' | 'next' | 'sending' | 'handed' | 'stopped' | 'failed';

export function queuedLine(item: QueuedMessageDto): QueuedLine {
  if (item.state === 'Delivering') return 'sending';
  if (item.state === 'HandedOver') return 'handed';
  if (item.state === 'Held') return heldBecauseStopped(item) ? 'stopped' : 'failed';
  return item.sendNow ? 'next' : 'waiting';
}

/**
 * A held message whose turn was stopped: the server says so with `AI_QUEUED_TURN_STOPPED`. A held row
 * without any reason is one an older server held for the same cause (it wrote none then).
 */
function heldBecauseStopped(item: QueuedMessageDto): boolean {
  return item.failureCode === AiChatErrorCodes.QueuedTurnStopped || (!item.failure && !item.failureCode);
}

/** The words in `text` as a queued message holds them; null when there are none. */
export function queuedText(text: string): string | null {
  const trimmed = text.trim();
  return trimmed.length > 0 ? trimmed : null;
}

/** The order the server sends them in: the one to send now first, then as written. */
function inSendingOrder(items: readonly QueuedMessageDto[]): QueuedMessageDto[] {
  return [...items].sort((a, b) => Number(b.sendNow) - Number(a.sendNow));
}

export function createMessageQueue(options: MessageQueueOptions): MessageQueue {
  const intervalMs = options.intervalMs ?? QUEUE_POLL_MS;
  /** The rows as the server has them, taken-in ones too: their going must not read as a turn that went out. */
  const rows = shallowRef<readonly QueuedMessageDto[]>([]);
  const busy = ref<ReadonlySet<string>>(new Set());
  const threadId = ref<string | null>(null);
  const isRunning = ref(false);
  const serverRun = ref(false);
  /** The messages this page took away, which did not go out. */
  let removedHere: ReadonlySet<string> = new Set();
  /** A listed message went out while a turn was on screen: follow the conversation once that turn ends. */
  let followOwed = false;
  /** Quick reads left after a turn ended. */
  let quickReads = 0;
  /** Bumped by every read and change, so a slow read cannot land over a newer state. */
  let ticket = 0;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let disposed = false;

  function stopReading(): void {
    if (timer !== null) clearTimeout(timer);
    timer = null;
  }

  function schedule(): void {
    stopReading();
    if (disposed || intervalMs <= 0 || !threadId.value || !waitsToGo(rows.value)) return;
    const delay = quickReads > 0 ? Math.min(intervalMs, QUEUE_AFTER_TURN_MS) : intervalMs;
    if (quickReads > 0) quickReads--;
    timer = setTimeout(() => {
      timer = null;
      void refresh();
    }, delay);
  }

  function settle(next: readonly QueuedMessageDto[]): void {
    const gone = wentOut(rows.value, next, removedHere);
    rows.value = inSendingOrder(next);
    schedule();
    if (gone.length === 0) return;
    // A turn is still on screen: the one the server started shows when this page follows the
    // conversation, which it can only do once that turn ends here.
    if (isRunning.value) followOwed = true;
    else deliver();
  }

  /** A turn this page did not start began: follow the conversation (once). */
  function deliver(): void {
    followOwed = false;
    options.onDelivered?.();
  }

  async function refresh(): Promise<void> {
    const id = threadId.value;
    const mine = ++ticket;
    if (!id) {
      stopReading();
      rows.value = [];
      return;
    }
    try {
      const result = await options.api.getQueue(id);
      if (mine !== ticket || id !== threadId.value || disposed) return;
      if (!result.succeeded) {
        options.onError?.('load-queue', result);
        schedule();
        return;
      }
      settle(result.data ?? []);
    } catch (error) {
      if (mine !== ticket || disposed) return;
      options.onError?.('load-queue', error);
      schedule();
    }
  }

  function markBusy(id: string, on: boolean): void {
    const next = new Set(busy.value);
    if (on) next.add(id);
    else next.delete(id);
    busy.value = next;
  }

  /** Replaces one message as the server now has it (or takes it off when it went at once). */
  function place(id: string, changed: QueuedMessageDto | null): void {
    ticket++;
    const others = rows.value.filter((m) => m.id !== id);
    rows.value = inSendingOrder(changed ? [...others, changed] : others);
    schedule();
  }

  async function add(input: QueueMessageRequestDto): Promise<QueuedMessageDto | null> {
    const id = threadId.value;
    if (!id) return null;
    try {
      const result = await options.api.queueMessage(id, input);
      if (!result.succeeded || !result.data) {
        options.onError?.('queue', result);
        return null;
      }
      const queued = result.data;
      if (id === threadId.value && !disposed) {
        // Went at once (nothing ran): a turn the page did not start, so the conversation is followed.
        if (queued.state === 'Delivering' && !queued.afterRunId) {
          place(queued.id, null);
          if (isRunning.value) followOwed = true;
          else deliver();
        } else {
          place(queued.id, queued);
        }
      }
      return queued;
    } catch (error) {
      options.onError?.('queue', error);
      return null;
    }
  }

  async function edit(messageId: string, text: string): Promise<boolean> {
    const id = threadId.value;
    const words = queuedText(text);
    if (!id || !words) return false;
    markBusy(messageId, true);
    try {
      const result = await options.api.updateQueuedMessage(id, messageId, words);
      if (!result.succeeded || !result.data) {
        options.onError?.('edit-queued', result);
        void refresh();
        return false;
      }
      if (id === threadId.value) place(messageId, result.data);
      return true;
    } catch (error) {
      options.onError?.('edit-queued', error);
      void refresh();
      return false;
    } finally {
      markBusy(messageId, false);
    }
  }

  async function remove(messageId: string): Promise<boolean> {
    const id = threadId.value;
    if (!id) return false;
    removedHere = new Set([...removedHere, messageId]);
    markBusy(messageId, true);
    try {
      const result = await options.api.removeQueuedMessage(id, messageId);
      if (!result.succeeded) {
        removedHere = new Set([...removedHere].filter((m) => m !== messageId));
        options.onError?.('remove-queued', result);
        void refresh();
        return false;
      }
      if (id === threadId.value) place(messageId, null);
      return true;
    } catch (error) {
      removedHere = new Set([...removedHere].filter((m) => m !== messageId));
      options.onError?.('remove-queued', error);
      void refresh();
      return false;
    } finally {
      markBusy(messageId, false);
    }
  }

  async function sendNow(messageId: string): Promise<boolean> {
    const id = threadId.value;
    if (!id) return false;
    markBusy(messageId, true);
    try {
      const result = await options.api.sendQueuedMessageNow(id, messageId);
      if (!result.succeeded || !result.data) {
        options.onError?.('send-queued-now', result);
        void refresh();
        return false;
      }
      if (id === threadId.value) {
        const marked = result.data;
        if (marked.state === 'Delivering') {
          // Went at once (nothing ran): follow the conversation to show it.
          place(messageId, null);
          if (isRunning.value) followOwed = true;
          else deliver();
        } else {
          place(messageId, marked);
        }
      }
      return true;
    } catch (error) {
      options.onError?.('send-queued-now', error);
      void refresh();
      return false;
    } finally {
      markBusy(messageId, false);
    }
  }

  function follow(next: string | null | undefined, running: boolean, run: boolean): void {
    const id = next ?? null;
    const wasRunning = isRunning.value;
    isRunning.value = !!id && running;
    serverRun.value = !!id && running && run;
    if (id !== threadId.value) {
      threadId.value = id;
      ticket++;
      stopReading();
      removedHere = new Set();
      followOwed = false;
      quickReads = 0;
      rows.value = [];
      void refresh();
      return;
    }
    if (!wasRunning || isRunning.value) return;
    // The turn on screen ended: what waited for it goes now, on the server. A message seen gone
    // while it ran started a turn of its own, which the page follows now.
    quickReads = QUEUE_AFTER_TURN_READS;
    if (followOwed) deliver();
    void refresh();
  }

  function dispose(): void {
    disposed = true;
    ticket++;
    stopReading();
  }

  return {
    items: computed(() => rows.value.filter((m) => !takenIn(m))),
    available: computed(() => serverRun.value && !!threadId.value),
    running: computed(() => isRunning.value),
    busy: readonly(busy),
    follow,
    add,
    edit,
    remove,
    sendNow,
    refresh,
    dispose,
  };
}
