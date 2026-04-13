import { ref, watch, type Ref, type WatchSource } from 'vue'

export interface UseDataQueryOptions<TParams, TResult> {
  /** Reactive source of parameters. When it changes, the query re-runs (debounced). */
  params: WatchSource<TParams>
  /** The actual query function. */
  queryFn: (params: TParams) => Promise<TResult>
  /** Debounce delay in ms. Defaults to 300. */
  debounce?: number
  /** Run immediately on creation. Defaults to true. */
  immediate?: boolean
  /** Error handler. */
  onError?: (err: Error) => void
}

/**
 * Generic debounced query composable. Use for any async fetch that should
 * automatically re-run when its parameters change (e.g., search input).
 *
 * @example
 * const query = ref('')
 * const { data, loading } = useDataQuery({
 *   params: query,
 *   queryFn: (q) => api.search(q),
 *   debounce: 500,
 * })
 */
export function useDataQuery<TParams, TResult>(
  options: UseDataQueryOptions<TParams, TResult>,
) {
  const data: Ref<TResult | null> = ref(null) as Ref<TResult | null>
  const loading = ref(false)
  const error = ref<Error | null>(null)

  let timer: ReturnType<typeof setTimeout> | null = null
  let latestRequestId = 0

  async function runQuery(params: TParams): Promise<void> {
    const requestId = ++latestRequestId
    loading.value = true
    error.value = null
    try {
      const result = await options.queryFn(params)
      // Discard stale responses
      if (requestId !== latestRequestId) return
      data.value = result
    } catch (err) {
      if (requestId !== latestRequestId) return
      const e = err instanceof Error ? err : new Error(String(err))
      error.value = e
      options.onError?.(e)
    } finally {
      if (requestId === latestRequestId) {
        loading.value = false
      }
    }
  }

  function schedule(params: TParams): void {
    if (timer) clearTimeout(timer)
    const delay = options.debounce ?? 300
    timer = setTimeout(() => {
      void runQuery(params)
    }, delay)
  }

  watch(
    options.params,
    (params) => {
      schedule(params as TParams)
    },
    { immediate: options.immediate !== false, deep: true },
  )

  return {
    data,
    loading,
    error,
  }
}
