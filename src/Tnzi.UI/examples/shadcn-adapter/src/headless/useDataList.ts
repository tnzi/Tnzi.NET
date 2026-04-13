import { ref, computed, type Ref, type ComputedRef } from 'vue'
import type { IDataQuery, IDataLoadState, IWebPagerConfig } from '@tnzi/core/types/shared-ui'
import { calculateTotalPages, clampPageIndex, updatePageQuery } from '@tnzi/core/utils'

export interface UseDataListOptions<T = unknown> {
  items?: T[]
  query?: IDataQuery
  loadState?: IDataLoadState
  itemKey?: string | ((item: T, index: number) => string)
  emptyText?: string
  pager?: IWebPagerConfig | false
  onPageChange?: (pageIndex: number, pageSize: number) => void
  onUpdateQuery?: (query: IDataQuery) => void
  onRefresh?: () => void
}

export interface UseDataListReturn<T = unknown> {
  currentPage: Ref<number>
  pageSize: Ref<number>
  pagerConfig: ComputedRef<IWebPagerConfig | undefined>
  isLoading: ComputedRef<boolean>
  isEmpty: ComputedRef<boolean>
  total: ComputedRef<number>
  totalPages: ComputedRef<number>
  hasPager: ComputedRef<boolean>
  goPage: (page: number) => void
  onRefresh: () => void
  resolveKey: (item: T, index: number) => string
}

export function useDataList<T extends Record<string, unknown> = Record<string, unknown>>(
  options: UseDataListOptions<T> = {},
): UseDataListReturn<T> {
  const items = options.items ?? []
  const loadState = options.loadState ?? {}
  const query = options.query ?? {}

  const pagerConfig = computed(() => {
    const pager = options.pager as IWebPagerConfig | false | undefined
    return pager ? pager : undefined
  })

  const currentPage = ref(pagerConfig.value?.pageIndex ?? 1)
  const pageSize = ref(pagerConfig.value?.pageSize ?? 10)

  const isLoading = computed(() => !!loadState.loading)
  const isEmpty = computed(() => items.length === 0 && !isLoading.value)

  const total = computed(() => pagerConfig.value?.total ?? items.length)
  const totalPages = computed(() => calculateTotalPages(total.value, pageSize.value))
  const hasPager = computed(() => !!pagerConfig.value)

  function goPage(page: number): void {
    const next = clampPageIndex(page, total.value, pageSize.value)
    currentPage.value = next
    options.onPageChange?.(next, pageSize.value)
    options.onUpdateQuery?.(updatePageQuery(query, next, pageSize.value))
  }

  function onRefresh(): void {
    options.onRefresh?.()
  }

  function resolveKey(item: T, index: number): string {
    if (typeof options.itemKey === 'function') return options.itemKey(item, index)
    if (typeof options.itemKey === 'string' && item[options.itemKey as keyof T] != null) {
      return String(item[options.itemKey as keyof T])
    }
    return String(index)
  }

  return {
    currentPage,
    pageSize,
    pagerConfig,
    isLoading,
    isEmpty,
    total,
    totalPages,
    hasPager,
    goPage,
    onRefresh,
    resolveKey,
  }
}
