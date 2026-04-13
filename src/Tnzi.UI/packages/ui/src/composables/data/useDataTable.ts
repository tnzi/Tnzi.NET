import { ref, computed, type Ref } from 'vue'

/**
 * Query object passed to the fetcher.
 */
export interface DataTableQuery {
  pageIndex: number
  pageSize: number
  sortField?: string
  sortOrder?: 'asc' | 'desc'
  searchText?: string
  filters?: Record<string, unknown>
}

export interface DataTableResult<T> {
  items: T[]
  totalCount: number
}

export interface UseDataTableOptions<T> {
  fetcher: (query: DataTableQuery) => Promise<DataTableResult<T>>
  initialPageSize?: number
  initialSortField?: string
  initialSortOrder?: 'asc' | 'desc'
  rowKey?: (row: T) => string | number
  onError?: (err: Error) => void
  onSuccess?: (result: DataTableResult<T>) => void
  /** Auto-reload on mount. Defaults to true. */
  immediate?: boolean
}

/**
 * Headless data table composable.
 *
 * Composes over pagination, sorting, selection, and async loading state.
 * In a future refactor, this should delegate to @tnzi/core's
 * PaginationController / SortController / SelectionController when those APIs
 * are stable. For now we implement locally but keep the public API identical
 * to what the core-composed version would expose, so consumers don't need
 * to change code when the internals swap.
 */
export function useDataTable<T extends { id: string | number } = any>(
  options: UseDataTableOptions<T>,
) {
  const pageIndex = ref(1)
  const pageSize = ref(options.initialPageSize ?? 10)
  const sortField = ref<string | undefined>(options.initialSortField)
  const sortOrder = ref<'asc' | 'desc' | undefined>(options.initialSortOrder)
  const searchText = ref('')
  const filters = ref<Record<string, unknown>>({})

  const items: Ref<T[]> = ref([]) as Ref<T[]>
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref<Error | null>(null)

  const selectedIds = ref<Array<string | number>>([])

  const totalPages = computed(() =>
    pageSize.value > 0 ? Math.max(1, Math.ceil(totalCount.value / pageSize.value)) : 1,
  )

  const hasPrev = computed(() => pageIndex.value > 1)
  const hasNext = computed(() => pageIndex.value < totalPages.value)
  const isEmpty = computed(() => !loading.value && items.value.length === 0)

  function rowId(row: T): string | number {
    if (options.rowKey) return options.rowKey(row)
    return row.id
  }

  async function reload(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const result = await options.fetcher({
        pageIndex: pageIndex.value,
        pageSize: pageSize.value,
        sortField: sortField.value,
        sortOrder: sortOrder.value,
        searchText: searchText.value,
        filters: filters.value,
      })
      items.value = result.items
      totalCount.value = result.totalCount
      options.onSuccess?.(result)
    } catch (err) {
      const e = err instanceof Error ? err : new Error(String(err))
      error.value = e
      options.onError?.(e)
    } finally {
      loading.value = false
    }
  }

  async function goToPage(n: number): Promise<void> {
    const target = Math.max(1, Math.min(n, totalPages.value || 1))
    pageIndex.value = target
    await reload()
  }

  async function setPageSize(size: number): Promise<void> {
    pageSize.value = size
    pageIndex.value = 1
    await reload()
  }

  async function sortBy(field: string, order: 'asc' | 'desc'): Promise<void> {
    sortField.value = field
    sortOrder.value = order
    await reload()
  }

  async function search(text: string): Promise<void> {
    searchText.value = text
    pageIndex.value = 1
    await reload()
  }

  async function applyFilters(f: Record<string, unknown>): Promise<void> {
    filters.value = { ...f }
    pageIndex.value = 1
    await reload()
  }

  function toggleSelection(id: string | number): void {
    const idx = selectedIds.value.indexOf(id)
    if (idx >= 0) {
      selectedIds.value.splice(idx, 1)
    } else {
      selectedIds.value.push(id)
    }
  }

  function selectAll(): void {
    selectedIds.value = items.value.map(rowId)
  }

  function clearSelection(): void {
    selectedIds.value = []
  }

  function isSelected(id: string | number): boolean {
    return selectedIds.value.includes(id)
  }

  const selectedItems = computed<T[]>(() =>
    items.value.filter(row => selectedIds.value.includes(rowId(row))),
  )

  // Auto-load on creation unless immediate=false
  if (options.immediate !== false) {
    void reload()
  }

  return {
    // Pagination state
    pageIndex,
    pageSize,
    totalCount,
    totalPages,
    hasPrev,
    hasNext,

    // Data state
    items,
    loading,
    error,
    isEmpty,

    // Sort state
    sortField,
    sortOrder,

    // Search/filter state
    searchText,
    filters,

    // Selection state
    selectedIds,
    selectedItems,

    // Actions
    reload,
    goToPage,
    setPageSize,
    sortBy,
    search,
    applyFilters,
    toggleSelection,
    selectAll,
    clearSelection,
    isSelected,
  }
}
