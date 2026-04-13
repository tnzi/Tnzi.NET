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

  async function loadMore(): Promise<void> {
    if (isLoading.value || !hasMore.value) return
    isLoading.value = true
    try {
      const result = await options.onLoad?.(page.value, pageSize)
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
      isLoading.value = false
    }
  }

  async function refresh(): Promise<void> {
    if (isRefreshing.value) return
    isRefreshing.value = true
    try {
      const result = await options.onRefresh?.() ?? await options.onLoad?.(1, pageSize)
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
      isRefreshing.value = false
    }
  }

  function reset(): void {
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
