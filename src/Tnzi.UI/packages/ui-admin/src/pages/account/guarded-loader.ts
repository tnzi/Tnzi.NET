import type { Ref } from 'vue'

/**
 * Every read-only load in the User Center goes through this guard so a loading
 * flag can NEVER be stranded (the "spinner keeps rotating forever" class of bug):
 *
 * - `try/finally` always releases the flag - even when `apply` throws on a
 *   malformed/failed-envelope payload;
 * - a generation token makes the **latest** call the sole owner of the flag and
 *   the data, so concurrent invocations (mount + Refresh clicks) can neither
 *   clear the spinner early nor overwrite fresh results with stale late ones;
 * - a timeout race ends the wait when the HTTP layer never settles (hung
 *   connection / token-refresh deadlock - the request promise itself may stay
 *   pending forever, which no try/finally can recover from), degrading the
 *   infinite spinner into an error toast + a retryable Refresh button.
 *
 * Extracted from the monolithic UserCenter so each self-loading section
 * component reuses the identical hardening.
 */
export const USER_CENTER_LOAD_TIMEOUT_MS = 15_000

export function createGuardedLoader<T>(options: {
  flag: Ref<boolean>
  fetch: () => Promise<T>
  apply: (result: T) => void
  onError: (e: unknown) => void
  /** Timeout message (usually the page translator's `loadTimeout`). */
  timeoutMessage: string
  timeoutMs?: number
}): () => Promise<void> {
  let generation = 0
  const timeoutMs = options.timeoutMs ?? USER_CENTER_LOAD_TIMEOUT_MS
  return async () => {
    const token = ++generation
    options.flag.value = true
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      const timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(options.timeoutMessage)), timeoutMs)
      })
      const result = await Promise.race([options.fetch(), timeout])
      if (token === generation) options.apply(result)
    } catch (e) {
      if (token === generation) options.onError(e)
    } finally {
      if (timer !== undefined) clearTimeout(timer)
      if (token === generation) options.flag.value = false
    }
  }
}
