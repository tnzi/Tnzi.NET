import { computed, watch, type ComputedRef } from 'vue'
import type { ITableColumn, IPaginationConfig } from '@tnzi/core/types/shared-ui'
import { PaginationController, SortController, SelectionController } from '@tnzi/core/headless'

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
  localSelectedKeys: ComputedRef<string[]>
  localSortBy: ComputedRef<string | undefined>
  localSortOrder: ComputedRef<'asc' | 'desc' | undefined>
  currentPage: ComputedRef<number>
  currentPageSize: ComputedRef<number>
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
  const paginationConfig = options.pagination !== undefined && options.pagination !== false
    ? options.pagination
    : undefined

  // --- Compose core controllers ---
  const paginationCtrl = new PaginationController({
    initialPage: paginationConfig?.pageIndex ?? 1,
    initialPageSize: paginationConfig?.pageSize ?? 10,
  })
  if (paginationConfig?.total != null) {
    paginationCtrl.totalCount = paginationConfig.total
  }

  const sortCtrl = new SortController({
    defaultField: options.sortBy,
    defaultDirection: options.sortOrder,
  })
  // When no defaultField, ensure sortFields is empty (sortBy getter returns null)
  if (!options.sortBy) {
    sortCtrl.clear()
  }

  const selectionCtrl = new SelectionController<string>({
    mode: options.selectionType === 'radio' ? 'single' : 'multiple',
    initialKeys: options.selectedKeys ? [...options.selectedKeys] : [],
  })

  // --- Bridge: core controllers → computed refs ---
  const currentPage = computed(() => paginationCtrl.pageIndex)
  const currentPageSize = computed(() => paginationCtrl.pageSize)

  const localSortBy = computed(() => sortCtrl.sortBy ?? undefined)
  const localSortOrder = computed((): 'asc' | 'desc' | undefined => {
    if (!sortCtrl.sortBy) return undefined
    const dir = sortCtrl.sortDirection
    if (dir === 'asc' || dir === 'desc') return dir
    return undefined
  })

  const localSelectedKeys = computed(() => [...selectionCtrl.selectedKeys])

  watch(
    () => options.selectedKeys,
    (next) => { selectionCtrl.setSelectedKeys(next ? [...next] : []) },
    { deep: true },
  )

  // --- Columns ---
  const visibleColumns = computed(() => columns.filter((c) => !c.hidden))

  // --- Row key resolution ---
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

  // --- Selection ---
  const allRowKeys = computed(() => data.map((row, i) => getRowKey(row, i)))

  // Keep selection controller's allKeys in sync
  selectionCtrl.setAllKeys(allRowKeys.value)
  watch(allRowKeys, (keys) => { selectionCtrl.setAllKeys(keys) })

  const allSelected = computed(() => selectionCtrl.isAllSelected)

  function isRowSelected(row: T, index: number): boolean {
    return selectionCtrl.isSelected(getRowKey(row, index))
  }

  function toggleSelectAll(): void {
    selectionCtrl.toggleAll()
    options.onUpdateSelectedKeys?.([...selectionCtrl.selectedKeys])
  }

  function toggleSelectRow(row: T, index: number): void {
    selectionCtrl.toggle(getRowKey(row, index))
    options.onUpdateSelectedKeys?.([...selectionCtrl.selectedKeys])
  }

  // --- Sorting ---
  function handleSort(column: ITableColumn<T>): void {
    if (!column.sortable) return
    sortCtrl.toggle(column.key)
    if (sortCtrl.sortBy) {
      const dir = sortCtrl.sortDirection
      if (dir === 'asc' || dir === 'desc') {
        options.onSort?.(sortCtrl.sortBy, dir)
      }
    }
  }

  // --- Pagination ---
  const hasPagination = computed(() => !!paginationConfig)
  const total = computed(() => paginationCtrl.totalCount)
  const totalPages = computed(() => paginationCtrl.totalPages)

  function goPage(page: number): void {
    paginationCtrl.goTo(page)
    options.onPageChange?.(paginationCtrl.pageIndex, paginationCtrl.pageSize)
  }

  // --- Actions ---
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
