import { computed, ref, type ComputedRef, type Ref } from 'vue'
import {
  useColumnSettings,
  type ColumnDef,
  type UseColumnSettingsReturn,
} from './useColumnSettings'
import { useBatchActions, type UseBatchActionsReturn } from './useBatchActions'
import { useFormModal, type UseFormModalReturn } from './useFormModal'

export interface CrudPageQuery {
  pageIndex: number
  pageSize: number
  searchText: string
  sortField?: string
  sortOrder?: 'asc' | 'desc' | null
  filters: Record<string, unknown>
}

export interface CrudPageResult<T> {
  items: T[]
  totalCount: number
  pageIndex: number
  pageSize: number
}

export interface UseCrudPageOptions<T, TId = string | number> {
  pageId: string
  columns: ColumnDef[]
  rowKey: (row: T) => TId
  initialPageSize?: number
  fetchData: (query: CrudPageQuery) => Promise<CrudPageResult<T>>
  createData: (data: Partial<T>) => Promise<T>
  updateData: (id: TId, data: Partial<T>) => Promise<T>
  deleteData: (ids: TId[]) => Promise<void>
  exportData?: (query: CrudPageQuery) => Promise<Blob>
  importData?: (file: File) => Promise<void>
  onRefresh?: () => void
}

export interface UseCrudPageReturn<T, TId = string | number> {
  query: Ref<CrudPageQuery>
  items: Ref<T[]>
  total: Ref<number>
  loading: Ref<boolean>
  error: Ref<Error | null>
  hasData: ComputedRef<boolean>
  columnSettings: UseColumnSettingsReturn
  batchActions: UseBatchActionsReturn<TId>
  formModal: UseFormModalReturn<T>
  /**
   * The row-key extractor passed to {@link useCrudPage}. Re-exposed so
   * consumer components (notably `TCrudPage`) can default to it instead of
   * forcing callers to pass `rowKey` twice.
   */
  rowKey: (row: T) => TId
  refresh: () => Promise<void>
  setPage: (pageIndex: number) => void
  setPageSize: (pageSize: number) => void
  setSearch: (text: string) => void
  setSort: (field: string | undefined, order: 'asc' | 'desc' | null) => void
  setFilters: (filters: Record<string, unknown>) => void
  resetQuery: () => void
  openCreate: () => void
  openEdit: (row: T) => void
  openView: (row: T) => void
  submit: () => Promise<T | null>
  handleDelete: (ids?: TId[]) => Promise<void>
  exportAll: () => Promise<Blob | null>
  importFile: (file: File) => Promise<void>
}

export function useCrudPage<T, TId = string | number>(
  options: UseCrudPageOptions<T, TId>,
): UseCrudPageReturn<T, TId> {
  const initialPageSize = options.initialPageSize ?? 20

  const makeInitialQuery = (): CrudPageQuery => ({
    pageIndex: 1,
    pageSize: initialPageSize,
    searchText: '',
    sortField: undefined,
    sortOrder: null,
    filters: {},
  })

  const query = ref<CrudPageQuery>(makeInitialQuery()) as Ref<CrudPageQuery>
  const items = ref<T[]>([]) as Ref<T[]>
  const total = ref(0)
  const loading = ref(false)
  const error = ref<Error | null>(null)

  const hasData = computed(() => items.value.length > 0)

  const columnSettings = useColumnSettings({
    pageId: options.pageId,
    columns: options.columns,
  })
  const batchActions = useBatchActions<TId>()
  const formModal = useFormModal<T>()

  async function refresh(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const result = await options.fetchData({ ...query.value })
      items.value = result.items
      total.value = result.totalCount
      options.onRefresh?.()
    } catch (err) {
      error.value = err instanceof Error ? err : new Error(String(err))
      throw error.value
    } finally {
      loading.value = false
    }
  }

  function setPage(pageIndex: number): void {
    query.value = { ...query.value, pageIndex }
    void refresh()
  }

  function setPageSize(pageSize: number): void {
    query.value = { ...query.value, pageSize, pageIndex: 1 }
    void refresh()
  }

  function setSearch(text: string): void {
    query.value = { ...query.value, searchText: text, pageIndex: 1 }
  }

  function setSort(field: string | undefined, order: 'asc' | 'desc' | null): void {
    query.value = { ...query.value, sortField: field, sortOrder: order }
  }

  function setFilters(filters: Record<string, unknown>): void {
    query.value = { ...query.value, filters, pageIndex: 1 }
  }

  function resetQuery(): void {
    query.value = makeInitialQuery()
  }

  function openCreate(): void {
    formModal.open('create', null)
  }

  function openEdit(row: T): void {
    formModal.open('edit', row)
  }

  function openView(row: T): void {
    formModal.open('view', row)
  }

  async function submit(): Promise<T | null> {
    const mode = formModal.mode.value
    const data = formModal.formData.value
    if (mode === 'view' || !mode || !data) {
      formModal.close()
      return null
    }
    if (mode === 'create') {
      const created = await options.createData(data as Partial<T>)
      formModal.close()
      await refresh()
      return created
    }
    // edit
    const id = options.rowKey(data as T)
    const updated = await options.updateData(id, data as Partial<T>)
    formModal.close()
    await refresh()
    return updated
  }

  async function handleDelete(ids?: TId[]): Promise<void> {
    const target = ids ?? batchActions.selectedIds.value
    if (target.length === 0) return
    await options.deleteData(target)
    if (!ids) {
      batchActions.clear()
    } else {
      batchActions.clear()
    }
    await refresh()
  }

  async function exportAll(): Promise<Blob | null> {
    if (!options.exportData) return null
    return options.exportData({ ...query.value })
  }

  async function importFile(file: File): Promise<void> {
    if (!options.importData) return
    await options.importData(file)
    await refresh()
  }

  return {
    query,
    items,
    total,
    loading,
    error,
    hasData,
    columnSettings,
    batchActions,
    formModal,
    rowKey: options.rowKey,
    refresh,
    setPage,
    setPageSize,
    setSearch,
    setSort,
    setFilters,
    resetQuery,
    openCreate,
    openEdit,
    openView,
    submit,
    handleDelete,
    exportAll,
    importFile,
  }
}
