import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface UseDataListOptions<T = unknown> {
  pageSize?: number
  onLoad?: (page: number, pageSize: number) => Promise<T[]> | T[]
  onRefresh?: () => Promise<T[]> | T[]
}

export interface UseDataListReturn<T = unknown> {
  items: Ref<T[]>
  isLoading: Ref<boolean>
  isRefreshing: Ref<boolean>
  hasMore: Ref<boolean>
  page: Ref<number>
  isEmpty: ComputedRef<boolean>
  loadMore: () => Promise<void>
  refresh: () => Promise<void>
  reset: () => void
}

export function useDataList<T = unknown>(options: UseDataListOptions<T> = {}): UseDataListReturn<T> {
  const pageSize = options.pageSize ?? 20

  const items = ref<T[]>([]) as Ref<T[]>
  const isLoading = ref(false)
  const isRefreshing = ref(false)
  const hasMore = ref(true)
  const page = ref(1)

  const isEmpty = computed(() => items.value.length === 0 && !isLoading.value && !isRefreshing.value)

  /**
   * Bumped by refresh() and reset(), which replace the list. A request that
   * started before the bump answers for a list that no longer exists: its rows
   * are dropped, and it leaves the loading flags to whatever runs now.
   */
  let generation = 0

  async function loadMore(): Promise<void> {
    // During a refresh the page cursor is about to be reset; a page requested
    // now would be the wrong one.
    if (isLoading.value || isRefreshing.value || !hasMore.value) return
    const started = generation
    isLoading.value = true
    try {
      const result = await options.onLoad?.(page.value, pageSize)
      if (started !== generation) return
      if (result) {
        const newItems = result as T[]
        items.value = [...items.value, ...newItems]
        page.value += 1
        if (newItems.length < pageSize) {
          hasMore.value = false
        }
      } else {
        hasMore.value = false
      }
    } finally {
      if (started === generation) isLoading.value = false
    }
  }

  async function refresh(): Promise<void> {
    if (isRefreshing.value) return
    const started = ++generation
    // A page load still in flight is abandoned (its answer is dropped above).
    isLoading.value = false
    isRefreshing.value = true
    try {
      const result = await options.onRefresh?.() ?? await options.onLoad?.(1, pageSize)
      if (started !== generation) return
      if (result) {
        const newItems = result as T[]
        items.value = newItems
        page.value = 2
        hasMore.value = newItems.length >= pageSize
      } else {
        items.value = []
        page.value = 1
        hasMore.value = false
      }
    } finally {
      if (started === generation) isRefreshing.value = false
    }
  }

  function reset(): void {
    generation += 1
    items.value = []
    isLoading.value = false
    isRefreshing.value = false
    hasMore.value = true
    page.value = 1
  }

  return {
    items,
    isLoading,
    isRefreshing,
    hasMore,
    page,
    isEmpty,
    loadMore,
    refresh,
    reset,
  }
}
