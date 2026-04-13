import { ref, computed, type Ref } from 'vue'

export interface DataListQuery {
  pageIndex: number
  pageSize: number
  searchText?: string
}

export interface DataListResult<T> {
  items: T[]
  totalCount: number
}

export interface UseDataListOptions<T> {
  fetcher: (query: DataListQuery) => Promise<DataListResult<T>>
  initialPageSize?: number
  /** Append mode: when true, new pages are appended to items (infinite scroll) instead of replacing. */
  appendMode?: boolean
  onError?: (err: Error) => void
  immediate?: boolean
}

/**
 * Headless data list composable for list-style (non-tabular) UIs.
 * Supports "load more" append mode for infinite scroll.
 */
export function useDataList<T>(options: UseDataListOptions<T>) {
  const pageIndex = ref(1)
  const pageSize = ref(options.initialPageSize ?? 20)
  const searchText = ref('')

  const items: Ref<T[]> = ref([]) as Ref<T[]>
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref<Error | null>(null)

  const hasMore = computed(() => items.value.length < totalCount.value)

  async function load(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const result = await options.fetcher({
        pageIndex: pageIndex.value,
        pageSize: pageSize.value,
        searchText: searchText.value,
      })
      if (options.appendMode && pageIndex.value > 1) {
        items.value = [...items.value, ...result.items]
      } else {
        items.value = result.items
      }
      totalCount.value = result.totalCount
    } catch (err) {
      const e = err instanceof Error ? err : new Error(String(err))
      error.value = e
      options.onError?.(e)
    } finally {
      loading.value = false
    }
  }

  async function loadMore(): Promise<void> {
    if (!hasMore.value || loading.value) return
    pageIndex.value += 1
    await load()
  }

  async function refresh(): Promise<void> {
    pageIndex.value = 1
    await load()
  }

  async function search(text: string): Promise<void> {
    searchText.value = text
    pageIndex.value = 1
    items.value = []
    await load()
  }

  if (options.immediate !== false) {
    void load()
  }

  return {
    pageIndex,
    pageSize,
    items,
    totalCount,
    loading,
    error,
    hasMore,
    searchText,
    load,
    loadMore,
    refresh,
    search,
  }
}
