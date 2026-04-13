import { computed, type Ref, type ComputedRef } from 'vue'
import type { IDataQuery, IDataLoadState, IWebPagerConfig } from '@tnzi/core/types/shared-ui'
import { PaginationController } from '@tnzi/core/headless'
import { updatePageQuery } from '@tnzi/core/headless'

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

  // --- Compose core PaginationController ---
  const paginationCtrl = new PaginationController({
    initialPage: pagerConfig.value?.pageIndex ?? 1,
    initialPageSize: pagerConfig.value?.pageSize ?? 10,
  })
  if (pagerConfig.value?.total != null) {
    paginationCtrl.totalCount = pagerConfig.value.total
  } else {
    paginationCtrl.totalCount = items.length
  }

  // --- Bridge: core controller -> computed refs ---
  const currentPage = computed(() => paginationCtrl.pageIndex)
  const pageSize = computed(() => paginationCtrl.pageSize)

  const isLoading = computed(() => !!loadState.loading)
  const isEmpty = computed(() => items.length === 0 && !isLoading.value)

  const total = computed(() => paginationCtrl.totalCount)
  const totalPages = computed(() => paginationCtrl.totalPages)
  const hasPager = computed(() => !!pagerConfig.value)

  function goPage(page: number): void {
    paginationCtrl.goTo(page)
    options.onPageChange?.(paginationCtrl.pageIndex, paginationCtrl.pageSize)
    options.onUpdateQuery?.(updatePageQuery(query, paginationCtrl.pageIndex, paginationCtrl.pageSize))
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
