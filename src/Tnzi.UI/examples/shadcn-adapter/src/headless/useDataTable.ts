import { ref, computed, watch, type Ref, type ComputedRef } from 'vue'
import type { ITableColumn, IPaginationConfig } from '@tnzi/core/types/shared-ui'
import { calculateTotalPages, clampPageIndex, toggleSort } from '@tnzi/core/utils'

export interface UseDataTableOptions<T = unknown> {
  data?: T[]
  columns?: ITableColumn<T>[]
  rowKey?: string | ((row: T) => string)
  selectedKeys?: string[]
  selectionType?: 'checkbox' | 'radio'
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
  pagination?: IPaginationConfig | false
  onUpdateSelectedKeys?: (keys: string[]) => void
  onSort?: (field: string, order: 'asc' | 'desc') => void
  onPageChange?: (pageIndex: number, pageSize: number) => void
  onAction?: (actionKey: string, row: T, index: number) => void
}

export interface UseDataTableReturn<T = unknown> {
  localSelectedKeys: Ref<string[]>
  localSortBy: Ref<string | undefined>
  localSortOrder: Ref<'asc' | 'desc' | undefined>
  currentPage: Ref<number>
  currentPageSize: Ref<number>
  visibleColumns: ComputedRef<ITableColumn<T>[]>
  allRowKeys: ComputedRef<string[]>
  allSelected: ComputedRef<boolean>
  hasPagination: ComputedRef<boolean>
  total: ComputedRef<number>
  totalPages: ComputedRef<number>
  getRowKey: (row: T, index: number) => string
  isRowSelected: (row: T, index: number) => boolean
  toggleSelectAll: () => void
  toggleSelectRow: (row: T, index: number) => void
  handleSort: (column: ITableColumn<T>) => void
  goPage: (page: number) => void
  handleAction: (actionKey: string, row: T, index: number) => void
}

export function useDataTable<T extends Record<string, unknown> = Record<string, unknown>>(
  options: UseDataTableOptions<T> = {},
): UseDataTableReturn<T> {
  const data = options.data ?? []
  const columns = options.columns ?? []
  const getPaginationConfig = () => options.pagination || undefined

  const localSelectedKeys = ref<string[]>([...(options.selectedKeys ?? [])])
  const localSortBy = ref<string | undefined>(options.sortBy)
  const localSortOrder = ref<'asc' | 'desc' | undefined>(options.sortOrder)
  const currentPage = ref(getPaginationConfig()?.pageIndex ?? 1)
  const currentPageSize = ref(getPaginationConfig()?.pageSize ?? 10)

  watch(
    () => options.selectedKeys,
    (next) => {
      localSelectedKeys.value = [...(next ?? [])]
    },
    { deep: true },
  )

  const visibleColumns = computed(() => columns.filter((c) => !c.hidden))

  function getRowKey(row: T, index: number): string {
    if (typeof options.rowKey === 'function') {
      const key = options.rowKey(row)
      if (key == null) return String(index)
      return key
    }
    const field = options.rowKey ?? 'id'
    if (row[field as keyof T] != null) return String(row[field as keyof T])
    return String(index)
  }

  function isRowSelected(row: T, index: number): boolean {
    return localSelectedKeys.value.includes(getRowKey(row, index))
  }

  const allRowKeys = computed(() => data.map((row, i) => getRowKey(row, i)))
  const allSelected = computed(
    () => allRowKeys.value.length > 0 && allRowKeys.value.every((k) => localSelectedKeys.value.includes(k)),
  )

  function toggleSelectAll(): void {
    localSelectedKeys.value = allSelected.value ? [] : [...allRowKeys.value]
    options.onUpdateSelectedKeys?.(localSelectedKeys.value)
  }

  function toggleSelectRow(row: T, index: number): void {
    const key = getRowKey(row, index)
    if (localSelectedKeys.value.includes(key)) {
      localSelectedKeys.value = localSelectedKeys.value.filter((k) => k !== key)
    } else if (options.selectionType === 'radio') {
      localSelectedKeys.value = [key]
    } else {
      localSelectedKeys.value = [...localSelectedKeys.value, key]
    }
    options.onUpdateSelectedKeys?.(localSelectedKeys.value)
  }

  function handleSort(column: ITableColumn<T>): void {
    if (!column.sortable) return
    const next = toggleSort(
      { sortBy: localSortBy.value, sortDirection: localSortOrder.value },
      column.key,
      'asc',
    )
    localSortBy.value = next.sortBy
    localSortOrder.value = next.sortDirection
    options.onSort?.(next.sortBy, next.sortDirection)
  }

  const hasPagination = computed(() => !!getPaginationConfig())
  const total = computed(() => getPaginationConfig()?.total ?? data.length)
  const totalPages = computed(() => calculateTotalPages(total.value, currentPageSize.value))

  function goPage(page: number): void {
    const next = clampPageIndex(page, total.value, currentPageSize.value)
    currentPage.value = next
    options.onPageChange?.(next, currentPageSize.value)
  }

  function handleAction(actionKey: string, row: T, index: number): void {
    options.onAction?.(actionKey, row, index)
  }

  return {
    localSelectedKeys,
    localSortBy,
    localSortOrder,
    currentPage,
    currentPageSize,
    visibleColumns,
    allRowKeys,
    allSelected,
    hasPagination,
    total,
    totalPages,
    getRowKey,
    isRowSelected,
    toggleSelectAll,
    toggleSelectRow,
    handleSort,
    goPage,
    handleAction,
  }
}
