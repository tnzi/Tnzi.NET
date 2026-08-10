/**
 * The signed-in user's own token quota and usage.
 *
 * Backed by `GET /quotas/me` (`Tnzi.AI`'s `DefaultQuotaController`), a
 * user-facing route - an ordinary user reads their own quota, no admin
 * permission involved, which is what lets this be a built-in settings page.
 *
 * Read-only by design: a user cannot raise their own limit, so there is no
 * write path to get wrong.
 */
import { ref, computed, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { useQuotaApi } from '@tnzi/core/services/ai';
import type { UserQuotaDto } from '@tnzi/core/services/ai';

export interface UseAiUsageOptions {
  client?: HttpClient | null;
}

export interface UseAiUsageReturn {
  readonly quota: Ref<UserQuotaDto | null>;
  readonly loading: Ref<boolean>;
  readonly available: ComputedRef<boolean>;
  /** False once a load has completed and found nothing - a deployment with
   *  quotas switched off, which is not an error and must not read as one. */
  readonly enabled: ComputedRef<boolean>;
  load: () => Promise<void>;
}

export function useAiUsage(options: UseAiUsageOptions = {}): UseAiUsageReturn {
  const client = options.client ?? null;
  const api = client ? useQuotaApi(client) : null;

  const quota = ref<UserQuotaDto | null>(null);
  const loading = ref(false);

  async function load(): Promise<void> {
    if (!api) return;
    loading.value = true;
    try {
      const result = await api.getMyQuota();
      quota.value = result?.data ?? null;
    } catch {
      /* Fail-safe like every other settings read: a deployment without the
         endpoint shows "no limit in force", not an error banner on a page the
         user merely opened. */
      quota.value = null;
    } finally {
      loading.value = false;
    }
  }

  return {
    quota,
    loading,
    available: computed(() => api !== null),
    enabled: computed(() => quota.value?.isEnabled === true),
    load,
  };
}

/**
 * Whether a limit means "no limit".
 *
 * The backend stores an unlimited quota as a sentinel near `long.MaxValue / 2`
 * (4611686018427387903). Two things go wrong if that reaches the screen: it
 * renders as "4,611,686,018,427,388,000 tokens", which no one can read as
 * "unlimited"; and it is past `Number.MAX_SAFE_INTEGER`, so JSON parsing has
 * already rounded it - the digits shown are not even the stored value.
 *
 * Testing against MAX_SAFE_INTEGER rather than the exact sentinel covers both:
 * any limit that large is unlimited in practice AND is a number JavaScript can
 * no longer be trusted to have received intact.
 */
export function isUnlimited(limit: number | undefined | null): boolean {
  return typeof limit !== 'number' || !Number.isFinite(limit) || limit >= Number.MAX_SAFE_INTEGER;
}

/**
 * Percentage clamped to 0-100 for a meter width.
 *
 * The backend already sends a percentage, but usage can exceed a limit (a turn
 * that overshoots finishes rather than truncating mid-sentence), and a bar
 * wider than its track escapes the card.
 */
export function usageBarPercent(percentage: number | undefined | null): number {
  if (typeof percentage !== 'number' || !Number.isFinite(percentage)) return 0;
  return Math.max(0, Math.min(100, percentage));
}

/** Thousands separators. Token counts are the whole point of the page and
 *  `1284730` is unreadable at a glance. */
export function formatTokens(value: number | undefined | null): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '-';
  return value.toLocaleString();
}
