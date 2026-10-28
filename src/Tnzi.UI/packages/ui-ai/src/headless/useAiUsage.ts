/**
 * The signed-in user's own usage limits: how much of each window (the 5-hour
 * session window and the weekly one) is used, and when each resets.
 *
 * Backed by `GET /usage-limits/me` (`Tnzi.AI`'s `DefaultUsageLimitController`),
 * a user-facing route: an ordinary user reads their own figures, as shares of
 * each window and never as amounts, so this can be a built-in settings page.
 *
 * Read-only by design: a user cannot raise their own limit, so there is no
 * write path to get wrong.
 */
import { ref, computed, getCurrentScope, onScopeDispose, watch, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { useUsageLimitApi } from '@tnzi/core/services/ai';
import type { MyUsageLimitsDto, MyUsageWindowDto, UsageLimitWindow } from '@tnzi/core/services/ai';
import { isResetAhead, type UsageLimitNotice } from './usage-limits';

export interface UseAiUsageOptions {
  client?: HttpClient | null;
  /** Clock, for tests. Default `() => new Date()`. */
  now?: () => Date;
  /** How often the clock re-reads while a window is at or near its limit. Default 30 s. */
  tickMs?: number;
}

/** A window past the warning threshold (and not at its limit). */
export interface UsageLimitWarning {
  window: UsageLimitWindow;
  /** 0-100. */
  usedPercent: number;
  resetsAt: string | null;
}

export interface UseAiUsageReturn {
  /** The last reading, or null before one arrived (or when the deployment has no such endpoint). */
  readonly status: Ref<MyUsageLimitsDto | null>;
  readonly loading: Ref<boolean>;
  /** The last read failed (other than the endpoint not existing). */
  readonly failed: Ref<boolean>;
  /** A client was supplied. */
  readonly available: ComputedRef<boolean>;
  /** At least one window has a limit. False also reads "no limits apply", which is not an error. */
  readonly enabled: ComputedRef<boolean>;
  /** Every window, limited or not, in the server's order (session first). */
  readonly windows: ComputedRef<MyUsageWindowDto[]>;
  /**
   * A window at its limit whose reset is still ahead: sending is refused until then.
   * When both are, the one that resets later (the user has to wait for it).
   */
  readonly exceeded: ComputedRef<UsageLimitNotice | null>;
  /** The most-used window past the warning threshold, while none is at its limit. */
  readonly warning: ComputedRef<UsageLimitWarning | null>;
  /** When the last reading was taken. */
  readonly checkedAt: ComputedRef<Date | null>;
  /** Read the figures again. Never throws. */
  load: () => Promise<void>;
  /** `exceeded` evaluated against the clock right now (the computed only re-reads on its tick). */
  currentLimit: () => UsageLimitNotice | null;
}

/** HTTP codes that mean "this deployment has no such endpoint", not a failure. */
const ENDPOINT_ABSENT = new Set([404, 501]);

export function useAiUsage(options: UseAiUsageOptions = {}): UseAiUsageReturn {
  const client = options.client ?? null;
  const api = client ? useUsageLimitApi(client) : null;
  const readNow = options.now ?? (() => new Date());
  const tickMs = options.tickMs ?? 30_000;

  const status = ref<MyUsageLimitsDto | null>(null);
  const loading = ref(false);
  const failed = ref(false);
  /* The clock the computeds read. A plain `new Date()` inside a computed tracks
     nothing, so a reset that passes while the page sits open would never clear
     the "at its limit" state; this ref ticks while it matters. */
  const now = ref(readNow());
  let seq = 0;

  async function load(): Promise<void> {
    if (!api) return;
    const mine = ++seq;
    loading.value = true;
    try {
      const result = await api.getMine();
      if (mine !== seq) return;
      if (result?.succeeded && result.data) {
        status.value = result.data;
        failed.value = false;
      } else if (result && ENDPOINT_ABSENT.has(result.code ?? 0)) {
        status.value = null;
        failed.value = false;
      } else {
        failed.value = true;
      }
    } catch {
      if (mine === seq) failed.value = true;
    } finally {
      if (mine === seq) {
        now.value = readNow();
        loading.value = false;
      }
    }
  }

  const windows = computed(() => status.value?.windows ?? []);

  function limitAt(at: Date): UsageLimitNotice | null {
    const blocking = windows.value.filter((w) => w.exceeded && isResetAhead(w.resetsAt, at));
    if (blocking.length === 0) return null;
    // A window with no reset (a limit of zero) outlasts any dated one.
    const latest = blocking.reduce((a, b) => {
      if (!a.resetsAt) return a;
      if (!b.resetsAt) return b;
      return new Date(b.resetsAt).getTime() > new Date(a.resetsAt).getTime() ? b : a;
    });
    return { window: latest.window, resetsAt: latest.resetsAt ?? null };
  }

  const exceeded = computed(() => limitAt(now.value));

  const warning = computed<UsageLimitWarning | null>(() => {
    if (exceeded.value) return null;
    const near = windows.value
      .filter((w) => w.limited && w.warningReached && isResetAhead(w.resetsAt, now.value))
      .sort((a, b) => (b.usedPercent ?? 0) - (a.usedPercent ?? 0))[0];
    return near ? { window: near.window, usedPercent: near.usedPercent ?? 0, resetsAt: near.resetsAt ?? null } : null;
  });

  /* Tick only while something on screen depends on the clock. */
  let timer: ReturnType<typeof setInterval> | null = null;
  const stopTimer = () => {
    if (timer) clearInterval(timer);
    timer = null;
  };
  watch(
    () => windows.value.some((w) => w.exceeded || w.warningReached),
    (watching) => {
      stopTimer();
      if (watching && tickMs > 0) timer = setInterval(() => { now.value = readNow(); }, tickMs);
    },
    { immediate: true },
  );
  if (getCurrentScope()) onScopeDispose(stopTimer);

  return {
    status,
    loading,
    failed,
    available: computed(() => api !== null),
    enabled: computed(() => windows.value.some((w) => w.limited)),
    windows,
    exceeded,
    warning,
    checkedAt: computed(() => (status.value?.checkedAt ? new Date(status.value.checkedAt) : null)),
    load,
    currentLimit: () => limitAt(readNow()),
  };
}

/**
 * Percentage clamped to 0-100 for a meter width.
 *
 * Usage can exceed a limit (a turn that overshoots finishes rather than
 * truncating mid-sentence), and a bar wider than its track escapes the card.
 */
export function usageBarPercent(percentage: number | undefined | null): number {
  if (typeof percentage !== 'number' || !Number.isFinite(percentage)) return 0;
  return Math.max(0, Math.min(100, percentage));
}
